using P2PFil;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace P2PFil.ChatModule
{
    public class ChatService
    {
        public static ObservableCollection<ChatMessage> GlobalMessages { get; } = new();

        private TcpListener _listener = null!;
        private readonly int _chatPort = 5002;

        private static readonly string ChatMediaPath = Path.Combine(FileSystem.AppDataDirectory, "P2P_ChatMedia");
        private const int MaxPayloadSize = 25 * 1024 * 1024; // OOM Koruma: 25 MB Limiti

        public event Action<ChatMessage>? OnMessageReceived;

        public ChatService()
        {
            if (!Directory.Exists(ChatMediaPath))
            {
                Directory.CreateDirectory(ChatMediaPath);
            }
        }

        public void StartListening()
        {
            _listener = new TcpListener(IPAddress.Any, _chatPort);
            _listener.Start();

            Task.Run(async () =>
            {
                while (true)
                {
                    try
                    {
                        var client = await _listener.AcceptTcpClientAsync();
                        _ = HandleIncomingMessage(client);
                    }
                    catch { }
                }
            });
        }

        private async Task<byte[]> NegotiateSessionAsync(NetworkStream stream, IPAddress remoteIp, bool isClient, CancellationToken ct)
        {
            if (isClient)
            {
                if (SessionManager.Instance.TryGetSessionKey(remoteIp, out byte[] existingKey))
                {
                    stream.WriteByte(0x01);
                    await stream.FlushAsync(ct);
                    int response = stream.ReadByte();
                    if (response == 0x01) return existingKey;
                }
                stream.WriteByte(0x00);
                await stream.FlushAsync(ct);

                using var keyExchange = new KeyExchangeService();
                var (newKey, fingerprint) = await keyExchange.PerformKeyExchangeAsync(stream, ct);

                SessionManager.Instance.CreateOrUpdateSession(remoteIp, newKey, fingerprint);
                return newKey;
            }
            else
            {
                int flag = stream.ReadByte();
                if (flag == 0x01)
                {
                    if (SessionManager.Instance.TryGetSessionKey(remoteIp, out byte[] existingKey))
                    {
                        stream.WriteByte(0x01);
                        await stream.FlushAsync(ct);
                        return existingKey;
                    }
                    else
                    {
                        stream.WriteByte(0x00);
                        await stream.FlushAsync(ct);
                    }
                }
                using var keyExchange = new KeyExchangeService();
                var (newKey, fingerprint) = await keyExchange.PerformKeyExchangeAsync(stream, ct);

                SessionManager.Instance.CreateOrUpdateSession(remoteIp, newKey, fingerprint);
                return newKey;
            }
        }

        private async Task HandleIncomingMessage(TcpClient client)
        {
            IPAddress? remoteIp = null;
            try
            {
                var remoteEndPoint = client.Client.RemoteEndPoint as IPEndPoint;
                remoteIp = remoteEndPoint?.Address;
                if (remoteIp == null) return;

                using var stream = client.GetStream();
                byte[] aesKey = await NegotiateSessionAsync(stream, remoteIp, false, CancellationToken.None);

                using var reader = new BinaryReader(stream, Encoding.UTF8, true);

                int payloadLen = reader.ReadInt32();
                // OOM (Out Of Memory) DoS Koruması
                if (payloadLen <= 0 || payloadLen > MaxPayloadSize)
                {
                    throw new InvalidDataException($"Şüpheli paket boyutu algılandı: {payloadLen} bytes.");
                }

                byte[] encryptedPayload = reader.ReadBytes(payloadLen);

                if (encryptedPayload.Length > 0)
                {
                    string decryptedJson = EncryptionHelper.Decrypt(Convert.ToBase64String(encryptedPayload), aesKey);
                    var message = JsonSerializer.Deserialize<ChatMessage>(decryptedJson);

                    if (message != null)
                    {
                        // REPLAY ATTACK KORUMASI
                        if (SessionManager.Instance.IsMessageProcessed(remoteIp, message.MessageId)) return;

                        if (message.MessageType == "Image" || message.MessageType == "Video")
                        {
                            if (!string.IsNullOrEmpty(message.EncryptedBase64Media))
                            {
                                string decryptedBase64 = EncryptionHelper.Decrypt(message.EncryptedBase64Media, aesKey);
                                byte[] mediaBytes = Convert.FromBase64String(decryptedBase64);

                                // PATH TRAVERSAL KORUMASI: Sadece dosya adını al, klasör atlamalarını (../) engelle
                                string safeSanitizedName = Path.GetFileName(message.Content);
                                string safeFileName = $"{Guid.NewGuid()}_{safeSanitizedName}";
                                string savePath = Path.Combine(ChatMediaPath, safeFileName);

                                await File.WriteAllBytesAsync(savePath, mediaBytes);

                                message.LocalMediaPath = savePath;
                                message.EncryptedBase64Media = string.Empty;
                            }
                        }

                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            GlobalMessages.Add(message);
                            OnMessageReceived?.Invoke(message);
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Chat Alma Hatası: {ex.Message}");
                if (remoteIp != null) SessionManager.Instance.RemoveSession(remoteIp);
            }
            finally
            {
                client.Close();
            }
        }

        public async Task SendMessageAsync(string targetIp, string content)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                var msgObj = new ChatMessage
                {
                    SenderName = App.CurrentUsername,
                    SenderIp = "LOCAL",
                    Content = content,
                    MessageType = "Text",
                    Timestamp = DateTime.Now
                };

                await SendTcpPayload(targetIp, msgObj, cts.Token);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Chat Gönderme Hatası: {ex.Message}");
            }
        }

        public async Task SendMediaAsync(string targetIp, string filePath, string mediaType)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                FileInfo fi = new FileInfo(filePath);

                if (fi.Length > 15 * 1024 * 1024)
                {
                    throw new Exception("Sohbet üzerinden en fazla 15 MB boyutunda medya gönderilebilir. Daha büyük dosyalar için 'Dosyalar' sekmesini kullanın.");
                }

                byte[] fileBytes = await File.ReadAllBytesAsync(filePath, cts.Token);
                string base64String = Convert.ToBase64String(fileBytes);

                IPAddress remoteIp = IPAddress.Parse(targetIp);
                using var client = new TcpClient();
                await client.ConnectAsync(remoteIp, _chatPort, cts.Token);
                using var stream = client.GetStream();
                byte[] aesKey = await NegotiateSessionAsync(stream, remoteIp, true, cts.Token);

                string encryptedMedia = EncryptionHelper.Encrypt(base64String, aesKey);

                var msgObj = new ChatMessage
                {
                    SenderName = App.CurrentUsername,
                    SenderIp = "LOCAL",
                    Content = fi.Name,
                    MessageType = mediaType,
                    EncryptedBase64Media = encryptedMedia,
                    LocalMediaPath = filePath,
                    Timestamp = DateTime.Now
                };

                string jsonPayload = JsonSerializer.Serialize(msgObj);
                string encryptedPayload = EncryptionHelper.Encrypt(jsonPayload, aesKey);
                byte[] encryptedBytes = Convert.FromBase64String(encryptedPayload);

                using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
                writer.Write(encryptedBytes.Length);
                writer.Write(encryptedBytes);
                writer.Flush();

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    msgObj.EncryptedBase64Media = string.Empty;
                    GlobalMessages.Add(msgObj);
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Medya Gönderme Hatası: {ex.Message}");
                throw;
            }
        }

        private async Task SendTcpPayload(string targetIp, ChatMessage msgObj, CancellationToken token)
        {
            IPAddress remoteIp = IPAddress.Parse(targetIp);
            using var client = new TcpClient();
            await client.ConnectAsync(remoteIp, _chatPort, token);

            using var stream = client.GetStream();
            byte[] aesKey = await NegotiateSessionAsync(stream, remoteIp, true, token);

            string jsonPayload = JsonSerializer.Serialize(msgObj);
            string encryptedPayload = EncryptionHelper.Encrypt(jsonPayload, aesKey);
            byte[] encryptedBytes = Convert.FromBase64String(encryptedPayload);

            using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
            writer.Write(encryptedBytes.Length);
            writer.Write(encryptedBytes);
            writer.Flush();
        }
    }
}