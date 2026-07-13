using P2PFil;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Net.Sockets;
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

        // Medyaların sohbet için kaydedileceği gizli klasör
        private static readonly string ChatMediaPath = Path.Combine(FileSystem.AppDataDirectory, "P2P_ChatMedia");

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

        private async Task HandleIncomingMessage(TcpClient client)
        {
            try
            {
                using var reader = new StreamReader(client.GetStream());
                string encryptedPayload = await reader.ReadToEndAsync();

                if (!string.IsNullOrWhiteSpace(encryptedPayload))
                {
                    string decryptedJson = EncryptionHelper.Decrypt(encryptedPayload);
                    var message = JsonSerializer.Deserialize<ChatMessage>(decryptedJson);

                    if (message != null)
                    {
                        // EĞER GELEN MESAJ MEDYA İSE ŞİFREYİ ÇÖZ VE KLASÖRE KAYDET
                        if (message.MessageType == "Image" || message.MessageType == "Video")
                        {
                            if (!string.IsNullOrEmpty(message.EncryptedBase64Media))
                            {
                                // Şifrelenmiş medyayı çöz
                                string decryptedBase64 = EncryptionHelper.Decrypt(message.EncryptedBase64Media);
                                byte[] mediaBytes = Convert.FromBase64String(decryptedBase64);

                                // Güvenli isim oluştur ve kaydet
                                string safeFileName = $"{Guid.NewGuid()}_{message.Content}";
                                string savePath = Path.Combine(ChatMediaPath, safeFileName);

                                await File.WriteAllBytesAsync(savePath, mediaBytes);

                                // UI'da göstermek için yerel yolu modele ata
                                message.LocalMediaPath = savePath;
                                message.EncryptedBase64Media = string.Empty; // RAM'i rahatlatmak için veriyi sil
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
            }
            finally
            {
                client.Close();
            }
        }

        // NORMAL METİN GÖNDERME (Değişmedi)
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

        // YENİ: ŞİFRELİ MEDYA (FOTOĞRAF/VİDEO) GÖNDERME
        public async Task SendMediaAsync(string targetIp, string filePath, string mediaType)
        {
            // Medya transferi uzun sürebilir, timeout süresini 30 saniye yapıyoruz
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                FileInfo fi = new FileInfo(filePath);

                // RAM ÇÖKME KORUMASI: 15 MB Sınırı
                if (fi.Length > 15 * 1024 * 1024)
                {
                    throw new Exception("Sohbet üzerinden en fazla 15 MB boyutunda medya gönderilebilir. Daha büyük dosyalar için 'Dosyalar' sekmesini kullanın.");
                }

                // 1. Dosyayı Bayt olarak oku ve Base64'e çevir
                byte[] fileBytes = await File.ReadAllBytesAsync(filePath, cts.Token);
                string base64String = Convert.ToBase64String(fileBytes);

                // 2. Medya verisini şifrele
                string encryptedMedia = EncryptionHelper.Encrypt(base64String);

                var msgObj = new ChatMessage
                {
                    SenderName = App.CurrentUsername,
                    SenderIp = "LOCAL",
                    Content = fi.Name, // Dosya adını gönder
                    MessageType = mediaType, // "Image" veya "Video"
                    EncryptedBase64Media = encryptedMedia,
                    LocalMediaPath = filePath, // Gönderen kişinin kendi ekranında görmesi için
                    Timestamp = DateTime.Now
                };

                await SendTcpPayload(targetIp, msgObj, cts.Token);

                // Kendi ekranımıza da ekleyelim (RAM'i temizleyerek)
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    msgObj.EncryptedBase64Media = string.Empty;
                    GlobalMessages.Add(msgObj);
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Medya Gönderme Hatası: {ex.Message}");
                throw; // Arayüzde hatayı göstermek için fırlat
            }
        }

        // Kod tekrarını önlemek için ortak TCP gönderme metodu
        private async Task SendTcpPayload(string targetIp, ChatMessage msgObj, CancellationToken token)
        {
            string jsonPayload = JsonSerializer.Serialize(msgObj);
            string encryptedPayload = EncryptionHelper.Encrypt(jsonPayload);

            using var client = new TcpClient();
            await client.ConnectAsync(targetIp, _chatPort, token);

            using var writer = new StreamWriter(client.GetStream());
            await writer.WriteAsync(encryptedPayload.AsMemory(), token);
            await writer.FlushAsync(token);
        }
    }
}