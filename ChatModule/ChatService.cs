using P2PFil;
using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
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

        // GÜVENLİK DÜZELTMESİ (DoS / Kaynak Tükenmesi): Eşzamanlı bağlantı sayısı artık
        // hem toplamda hem de IP başına sınırlandırılıyor. El sıkışma ve mesaj okuma
        // artık gerçek bir zaman aşımına tabi (önceden CancellationToken.None
        // kullanılıyordu; veri göndermeyen bir istemci bağlantıyı süresiz açık
        // tutup thread-pool/soket kaynaklarını tüketebiliyordu).
        private readonly SemaphoreSlim _connectionLimiter = new(50, 50);
        private readonly ConcurrentDictionary<string, int> _perIpConnections = new();
        private const int MaxConnectionsPerIp = 5;
        private const int NegotiationTimeoutSeconds = 20;

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
                    await FrameIO.WriteByteAsync(stream, 0x01, ct);
                    await stream.FlushAsync(ct);
                    int response = await FrameIO.ReadByteOrEofAsync(stream, ct);
                    if (response == 0x01) return existingKey;
                }
                await FrameIO.WriteByteAsync(stream, 0x00, ct);
                await stream.FlushAsync(ct);

                using var keyExchange = new KeyExchangeService();
                var (newKey, fingerprint) = await keyExchange.PerformKeyExchangeAsync(stream, ct);

                VerifyTrustOrThrow(remoteIp, fingerprint);
                SessionManager.Instance.CreateOrUpdateSession(remoteIp, newKey, fingerprint);
                return newKey;
            }
            else
            {
                int flag = await FrameIO.ReadByteOrEofAsync(stream, ct);
                if (flag == 0x01)
                {
                    if (SessionManager.Instance.TryGetSessionKey(remoteIp, out byte[] existingKey))
                    {
                        await FrameIO.WriteByteAsync(stream, 0x01, ct);
                        await stream.FlushAsync(ct);
                        return existingKey;
                    }
                    else
                    {
                        await FrameIO.WriteByteAsync(stream, 0x00, ct);
                        await stream.FlushAsync(ct);
                    }
                }
                using var keyExchange = new KeyExchangeService();
                var (newKey, fingerprint) = await keyExchange.PerformKeyExchangeAsync(stream, ct);

                VerifyTrustOrThrow(remoteIp, fingerprint);
                SessionManager.Instance.CreateOrUpdateSession(remoteIp, newKey, fingerprint);
                return newKey;
            }
        }

        // GÜVENLİK DÜZELTMESİ (MITM): Trust-On-First-Use (TOFU). Bir IP ile ilk kez
        // anahtar değişimi yapıldığında fingerprint kalıcı olarak kaydedilir; sonraki
        // bağlantılarda fingerprint değişmişse (araya giren aktif bir saldırgan
        // olabilir) bağlantı reddedilir. Bkz. PeerTrustStore.cs için sınırlamalar.
        private static void VerifyTrustOrThrow(IPAddress remoteIp, string fingerprint)
        {
            var result = PeerTrustStore.Instance.VerifyOrTrust(remoteIp.ToString(), fingerprint);
            if (result == TrustResult.Mismatch)
            {
                SessionManager.Instance.RemoveSession(remoteIp);
                throw new CryptographicException(
                    $"GÜVENLİK UYARISI: {remoteIp} için beklenen kimlik parmak izi eşleşmiyor. " +
                    "Bağlantı olası bir Man-in-the-Middle saldırısı şüphesiyle reddedildi.");
            }
        }

        private async Task HandleIncomingMessage(TcpClient client)
        {
            IPAddress? remoteIp = null;
            bool acquiredSlot = false;
            string? ipKey = null;
            try
            {
                var remoteEndPoint = client.Client.RemoteEndPoint as IPEndPoint;
                remoteIp = remoteEndPoint?.Address;
                if (remoteIp == null) return;
                ipKey = remoteIp.ToString();

                if (!await _connectionLimiter.WaitAsync(TimeSpan.FromSeconds(2)))
                {
                    return; // Sunucu meşgul - DoS koruması için bağlantı reddedildi
                }
                acquiredSlot = true;

                int current = _perIpConnections.AddOrUpdate(ipKey, 1, (_, c) => c + 1);
                if (current > MaxConnectionsPerIp)
                {
                    return; // Bu IP'den çok fazla eşzamanlı bağlantı - olası DoS denemesi
                }

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(NegotiationTimeoutSeconds));

                using var stream = client.GetStream();
                byte[] aesKey = await NegotiateSessionAsync(stream, remoteIp, false, cts.Token);

                int payloadLen = await FrameIO.ReadInt32Async(stream, cts.Token);
                // OOM (Out Of Memory) DoS Koruması
                if (payloadLen <= 0 || payloadLen > MaxPayloadSize)
                {
                    throw new InvalidDataException($"Şüpheli paket boyutu algılandı: {payloadLen} bytes.");
                }

                byte[] encryptedPayload = await FrameIO.ReadExactAsync(stream, payloadLen, cts.Token);

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
                if (ipKey != null) _perIpConnections.AddOrUpdate(ipKey, 0, (_, c) => Math.Max(0, c - 1));
                if (acquiredSlot) _connectionLimiter.Release();
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

                await FrameIO.WriteInt32Async(stream, encryptedBytes.Length, cts.Token);
                await FrameIO.WriteBytesAsync(stream, encryptedBytes, cts.Token);
                await stream.FlushAsync(cts.Token);

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

            await FrameIO.WriteInt32Async(stream, encryptedBytes.Length, token);
            await FrameIO.WriteBytesAsync(stream, encryptedBytes, token);
            await stream.FlushAsync(token);
        }
    }
}
