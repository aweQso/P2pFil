using P2PFil.Models;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;

namespace P2PFil.Services
{
    public class NetworkService
    {
        private const int UdpPort = 8888;
        private const int TcpPort = 8889;
        private UdpClient? udpClient;
        private TcpListener? tcpListener;

        // DÜZELTME: İsim güncellemelerini algılayabilmek için IP -> İsim eşleşmesine geçildi
        private ConcurrentDictionary<string, string> _ipToName = new();

        public string GetIpByName(string name)
        {
            var match = _ipToName.FirstOrDefault(x => x.Value == name);
            return match.Key ?? string.Empty;
        }

        public event Action<string>? PeerFound;
        public event Action<string, string, string>? PeerNameChanged; // YENİ: İsim değişti olayı
        public event Action<string, List<SharedFile>>? FilesReceived;

        public void StartDiscovery()
        {
            if (udpClient != null)
            {
                udpClient.Close();
                udpClient.Dispose();
            }

            udpClient = new UdpClient(UdpPort);
            udpClient.EnableBroadcast = true;

            Task.Run(async () =>
            {
                while (true)
                {
                    try
                    {
                        var result = await udpClient.ReceiveAsync();
                        var message = Encoding.UTF8.GetString(result.Buffer);
                        var senderIp = result.RemoteEndPoint.Address.ToString();

                        if (message.StartsWith("{"))
                        {
                            var networkMsg = JsonSerializer.Deserialize<NetworkMessage>(message);
                            if (networkMsg != null && networkMsg.Sender != App.CurrentUsername)
                            {
                                HandlePeerDiscovery(senderIp, networkMsg.Sender);
                                MainThread.BeginInvokeOnMainThread(() => FilesReceived?.Invoke(senderIp, networkMsg.Files));
                            }
                        }
                        else if (message.StartsWith("USER:"))
                        {
                            var peerName = message.Substring(5);
                            if (peerName != App.CurrentUsername)
                            {
                                HandlePeerDiscovery(senderIp, peerName);
                            }
                        }
                    }
                    catch { break; }
                }
            });

            if (tcpListener == null)
            {
                Task.Run(async () =>
                {
                    tcpListener = new TcpListener(IPAddress.Any, TcpPort);
                    tcpListener.Start();
                    while (true)
                    {
                        try
                        {
                            var client = await tcpListener.AcceptTcpClientAsync();
                            _ = HandleIncomingConnection(client);
                        }
                        catch { break; }
                    }
                });
            }

            Task.Run(async () =>
            {
                var endpoint = new IPEndPoint(IPAddress.Broadcast, UdpPort);
                while (true)
                {
                    try
                    {
                        if (udpClient == null || udpClient.Client == null) break;

                        var myFiles = FileService.GetSavedFiles().Take(20).Select(f => new SharedFile
                        {
                            FileName = f.Name,
                            OwnerName = App.CurrentUsername,
                            FileSize = $"{f.Length / 1024 / 1024.0:F2} MB",
                            UploadDate = f.LastWriteTime
                        }).ToList();

                        var messageObj = new NetworkMessage { Type = "FILE_LIST", Sender = App.CurrentUsername, Files = myFiles };
                        var messageBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(messageObj));
                        await udpClient.SendAsync(messageBytes, messageBytes.Length, endpoint);
                    }
                    catch { break; }

                    await Task.Delay(5000);
                }
            });
        }

        // YARDIMCI METOT: IP üzerinden isim değişimini kontrol eder
        private void HandlePeerDiscovery(string ip, string newName)
        {
            if (_ipToName.TryGetValue(ip, out var oldName))
            {
                if (oldName != newName)
                {
                    _ipToName[ip] = newName;
                    // YENİ: IP adresini de gönderiyoruz
                    MainThread.BeginInvokeOnMainThread(() => PeerNameChanged?.Invoke(ip, oldName, newName));
                }
            }
            else
            {
                _ipToName[ip] = newName;
                MainThread.BeginInvokeOnMainThread(() => PeerFound?.Invoke(newName));
            }
        }

        public async Task RequestDownload(string peerIp, string fileName, SharedFile targetFile)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

            try
            {
                MainThread.BeginInvokeOnMainThread(() => targetFile.IsDownloading = true);

                using var client = new TcpClient();
                await client.ConnectAsync(peerIp, TcpPort, cts.Token);

                using var stream = client.GetStream();
                using var writer = new BinaryWriter(stream);
                using var reader = new BinaryReader(stream);

                // --- KRİTİK DÜZELTME BURASI ---
                // Artık indirilenler, paylaşılanlardan bağımsız olan 'DownloadPath' klasörüne gidiyor!
                string folderPath = FileService.DownloadPath;
                if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);

                string safeFileName = Path.GetFileName(fileName);
                string savePath = Path.Combine(folderPath, safeFileName);
                long existingLength = File.Exists(savePath) ? new FileInfo(savePath).Length : 0;

                writer.Write($"REQ:{safeFileName}:{existingLength}");
                writer.Flush();

                string responseFileName = reader.ReadString();
                long totalFileLength = reader.ReadInt64();
                string expectedHash = reader.ReadString();

                cts.CancelAfter(TimeSpan.FromMinutes(5));

                using var fs = new FileStream(savePath, existingLength > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.ReadWrite);

                byte[] buffer = new byte[8192];
                long totalRead = existingLength;
                int bytesRead;

                while (totalRead < totalFileLength && (bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cts.Token)) > 0)
                {
                    await fs.WriteAsync(buffer, 0, bytesRead, cts.Token);
                    totalRead += bytesRead;

                    MainThread.BeginInvokeOnMainThread(() => targetFile.Progress = (double)totalRead / totalFileLength);
                }

                await fs.FlushAsync(cts.Token);
                fs.Close();

                if (existingLength == 0)
                {
                    string downloadedHash = FileService.GetFileHash(savePath);
                    if (downloadedHash != expectedHash)
                    {
                        File.Delete(savePath);
                        throw new Exception("Dosya Bütünlüğü Doğrulanamadı! (Man-In-The-Middle/Bozuk Veri)");
                    }
                }

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    targetFile.StatusMessage = "İndirildi ✅";
                    targetFile.IsDownloading = false;
                });
            }
            catch (OperationCanceledException)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    targetFile.StatusMessage = "Zaman Aşımı ❌";
                    targetFile.IsDownloading = false;
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"İndirme Hatası: {ex.Message}");
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    targetFile.StatusMessage = "Hata ❌";
                    targetFile.IsDownloading = false;
                });
            }
        }

        private async Task HandleIncomingConnection(TcpClient client)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5)); // Karşı taraf donarsa bağlantıyı kes
            try
            {
                using var stream = client.GetStream();
                using var reader = new BinaryReader(stream);
                using var writer = new BinaryWriter(stream);

                string command = reader.ReadString();

                if (command.StartsWith("REQ:"))
                {
                    var parts = command.Split(':');
                    string requestedFileName = Path.GetFileName(parts[1]);

                    long offset = 0;
                    if (parts.Length > 2) long.TryParse(parts[2], out offset);

                    var myFiles = FileService.GetSavedFiles();
                    var targetFile = myFiles.FirstOrDefault(f => f.Name == requestedFileName);

                    if (targetFile != null)
                    {
                        writer.Write(targetFile.Name);
                        writer.Write(targetFile.Length);

                        // GÜVENLİK: Dosyayı yollamadan önce kimliğini (hash) karşıya bildir
                        string fileHash = FileService.GetFileHash(targetFile.FullName);
                        writer.Write(fileHash);
                        writer.Flush();

                        using var fs = new FileStream(targetFile.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        if (offset > 0 && offset < fs.Length)
                        {
                            fs.Seek(offset, SeekOrigin.Begin);
                        }

                        await fs.CopyToAsync(stream, cts.Token);
                        await stream.FlushAsync(cts.Token);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Transfer Hatası: {ex.Message}");
            }
            finally
            {
                client.Close();
            }
        }
    }
}