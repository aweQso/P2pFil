using P2PFil;
using System;
using System.Collections.Concurrent;
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
        private const int MaxPayloadSize = 25 * 1024 * 1024;

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
                    catch (Exception ex)
                    {
                        // DÜZELTME: Sessizce yutmak yerine en azından debug log
                        // bırakıyoruz; operasyonel görünürlük için önemli.
                        System.Diagnostics.Debug.WriteLine($"Dinleyici kabul hatası: {ex.Message}");
                    }
                }
            });
        }

        private async Task HandleIncomingMessage(TcpClient client)
        {
            IPAddress? remoteIp = null;
            bool acquiredSlot = false;
            string? ipKey = null;
            try
            {
                var remoteEndPoint = client.Client?.RemoteEndPoint as IPEndPoint;
                remoteIp = remoteEndPoint?.Address;
                if (remoteIp == null) return;
                ipKey = remoteIp.ToString();

                if (!await _connectionLimiter.WaitAsync(TimeSpan.FromSeconds(2))) return;
                acquiredSlot = true;

                int current = _perIpConnections.AddOrUpdate(ipKey, 1, (_, c) => c + 1);
                if (current > MaxConnectionsPerIp) return;

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(NegotiationTimeoutSeconds));
                using var stream = client.GetStream();

                byte[] aesKey = await KeyExchangeService.NegotiateSessionAsync(stream, remoteIp, false, cts.Token);

                cts.CancelAfter(TimeSpan.FromMinutes(5));

                int payloadLen = await FrameIO.ReadInt32Async(stream, cts.Token);
                if (payloadLen <= 0 || payloadLen > MaxPayloadSize)
                    throw new InvalidDataException($"Şüpheli paket boyutu algılandı: {payloadLen} bytes.");

                byte[] encryptedPayload = await FrameIO.ReadExactAsync(stream, payloadLen, cts.Token);

                if (encryptedPayload.Length > 0)
                {
                    string decryptedJson = EncryptionHelper.Decrypt(Convert.ToBase64String(encryptedPayload), aesKey);
                    var message = JsonSerializer.Deserialize<ChatMessage>(decryptedJson);

                    if (message != null)
                    {
                        if (SessionManager.Instance.IsMessageProcessed(remoteIp, message.MessageId)) return;

                        if (message.MessageType == "Image" || message.MessageType == "Video")
                        {
                            if (!string.IsNullOrEmpty(message.EncryptedBase64Media))
                            {
                                // DÜZELTME (Çift Şifreleme Bug'ı): Önceki sürümde medya,
                                // önce tek başına AES-GCM ile şifreleniyor, SONRA bu
                                // şifreli veriyi içeren tüm JSON payload TEKRAR
                                // şifreleniyordu. Bu yaklaşık %78'lik bir şişmeye
                                // (15MB dosya -> ~26.7MB) yol açıyor ve gönderenin
                                // kendi 15MB kuralına uyan dosyalar bile alıcıdaki
                                // 25MB MaxPayloadSize kontrolüne takılıp reddediliyordu.
                                //
                                // Artık alan (EncryptedBase64Media) SADECE düz base64
                                // medya verisini taşıyor; gizlilik zaten dış JSON
                                // payload'ının tek katmanlı AES-GCM şifrelemesiyle
                                // sağlanıyor. Burada AYRICA bir Decrypt çağrısı YOK.
                                byte[] mediaBytes = Convert.FromBase64String(message.EncryptedBase64Media);

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
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                var msgObj = new ChatMessage
                {
                    MessageId = Guid.NewGuid().ToString(),
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
                // DÜZELTME (Sessiz Başarısızlık Bug'ı): Önceki sürümde hata burada
                // yutuluyor ve rethrow edilmiyordu. Bunun sonucunda ChatPage.OnSendClicked
                // içindeki `await SendMessageAsync(...)` hiçbir zaman exception
                // görmüyor, mesaj GERÇEKTEN GÖNDERİLEMEMİŞ olsa bile sohbet ekranına
                // "gönderildi" gibi ekleniyordu. Artık hata yeniden fırlatılıyor;
                // UI katmanı (ChatPage) kendi catch bloğunda kullanıcıyı bilgilendiriyor.
                // Servis katmanının doğrudan UI (Shell.Current.DisplayAlert) çağırması
                // da kaldırıldı — bu sorumluluk zaten çağıran tarafta (ChatPage) var.
                System.Diagnostics.Debug.WriteLine($"Mesaj Gönderme Hatası: {ex.Message}");
                throw;
            }
        }

        public async Task SendMediaAsync(string targetIp, string filePath, string mediaType)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                FileInfo fi = new FileInfo(filePath);
                if (fi.Length > 15 * 1024 * 1024)
                    throw new Exception("Sohbet üzerinden en fazla 15 MB boyutunda medya gönderilebilir.");

                byte[] fileBytes = await File.ReadAllBytesAsync(filePath, cts.Token);
                string base64String = Convert.ToBase64String(fileBytes);

                IPAddress remoteIp = IPAddress.Parse(targetIp);
                using var client = new TcpClient();
                await client.ConnectAsync(remoteIp, _chatPort, cts.Token);
                using var stream = client.GetStream();

                byte[] aesKey = await KeyExchangeService.NegotiateSessionAsync(stream, remoteIp, true, cts.Token);

                // DÜZELTME: Medya artık burada AYRICA şifrelenmiyor (bkz. HandleIncomingMessage
                // içindeki not). Düz base64 veri, dış JSON payload'ı ile birlikte
                // TEK KATMAN AES-GCM şifrelemesinden geçiyor. Bu hem ~%33 daha az
                // şişme sağlıyor hem de gönderici tarafındaki 15MB sınırının,
                // alıcı tarafındaki 25MB MaxPayloadSize kontrolüyle çelişmesini
                // (14-15MB arası dosyaların sebepsiz reddedilmesini) engelliyor.
                var msgObj = new ChatMessage
                {
                    MessageId = Guid.NewGuid().ToString(),
                    SenderName = App.CurrentUsername,
                    SenderIp = "LOCAL",
                    Content = fi.Name,
                    MessageType = mediaType,
                    EncryptedBase64Media = base64String,
                    LocalMediaPath = filePath,
                    Timestamp = DateTime.Now
                };

                string jsonPayload = JsonSerializer.Serialize(msgObj);
                string encryptedPayload = EncryptionHelper.Encrypt(jsonPayload, aesKey);
                byte[] encryptedBytes = Convert.FromBase64String(encryptedPayload);

                if (encryptedBytes.Length > MaxPayloadSize)
                    throw new Exception("Şifrelenmiş paket boyutu izin verilen üst sınırı aşıyor.");

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

            byte[] aesKey = await KeyExchangeService.NegotiateSessionAsync(stream, remoteIp, true, token);

            string jsonPayload = JsonSerializer.Serialize(msgObj);
            string encryptedPayload = EncryptionHelper.Encrypt(jsonPayload, aesKey);
            byte[] encryptedBytes = Convert.FromBase64String(encryptedPayload);

            await FrameIO.WriteInt32Async(stream, encryptedBytes.Length, token);
            await FrameIO.WriteBytesAsync(stream, encryptedBytes, token);
            await stream.FlushAsync(token);
        }
    }
}
