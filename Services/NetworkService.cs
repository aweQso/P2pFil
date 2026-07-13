using P2PFil.Models;
using P2PFil.ChatModule;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace P2PFil.Services
{
    public class NetworkService
    {
        private const int UdpPort = 8888;
        private const int TcpPort = 8889;
        private UdpClient? udpClient;
        private TcpListener? tcpListener;

        private ConcurrentDictionary<string, string> _ipToName = new();

        // GÜVENLİK DÜZELTMESİ (İsim Sahtekarlığı): Bir IP'nin en son ne zaman duyuru
        // yaptığı takip ediliyor. Aynı ismi başka bir IP'nin sahiplenmeye çalışması
        // (isim çakışması) durumunda hangi kaydın hâlâ "aktif" sayılacağına karar
        // vermek için kullanılıyor.
        private readonly ConcurrentDictionary<string, DateTime> _lastSeen = new();
        private const int NameConflictWindowMinutes = 10;

        // GÜVENLİK DÜZELTMESİ (DoS / Kaynak Tükenmesi): gelen TCP bağlantıları artık
        // hem toplamda hem de IP başına sınırlandırılıyor.
        private readonly SemaphoreSlim _connectionLimiter = new(50, 50);
        private readonly ConcurrentDictionary<string, int> _perIpConnections = new();
        private const int MaxConnectionsPerIp = 5;

        public string GetIpByName(string name)
        {
            var match = _ipToName.FirstOrDefault(x => x.Value == name);
            return match.Key ?? string.Empty;
        }

        public event Action<string>? PeerFound;
        public event Action<string, string, string>? PeerNameChanged;
        public event Action<string, List<SharedFile>>? FilesReceived;

        // GÜVENLİK: Bir peer, halihazırda başka (hâlâ aktif) bir IP'ye ait olan bir
        // ismi sahiplenmeye çalıştığında tetiklenir: (denemeyi yapan ip, denenen isim,
        // ismin mevcut sahibinin IP'si). UI katmanı bunu kullanıcıya uyarı olarak
        // gösterebilir.
        public event Action<string, string, string>? PeerNameConflict;

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
                        if (result.Buffer.Length > 8192) continue;

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

        private void HandlePeerDiscovery(string ip, string newName)
        {
            _lastSeen[ip] = DateTime.Now;

            if (_ipToName.TryGetValue(ip, out var oldName))
            {
                if (oldName != newName)
                {
                    _ipToName[ip] = newName;
                    MainThread.BeginInvokeOnMainThread(() => PeerNameChanged?.Invoke(ip, oldName, newName));
                }
                return;
            }

            // GÜVENLİK DÜZELTMESİ (İsim Sahtekarlığı): Bu isim zaten başka bir IP'ye
            // kayıtlıysa ve o IP hâlâ aktifse (son N dakika içinde duyuru yaptıysa),
            // yeni IP'nin bu ismi sessizce "çalmasına" izin verilmez. Böylece bir
            // saldırgan, ağdaki gerçek bir kullanıcı adını yayınlayarak kendi IP'sini
            // o isme bağlatamaz ve GetIpByName() üzerinden kullanıcıyı kendi IP'sine
            // yönlendiremez.
            var conflict = _ipToName.FirstOrDefault(x => x.Value == newName && x.Key != ip);
            if (!string.IsNullOrEmpty(conflict.Key))
            {
                bool conflictingPeerIsActive = _lastSeen.TryGetValue(conflict.Key, out var lastSeen)
                    && (DateTime.Now - lastSeen) < TimeSpan.FromMinutes(NameConflictWindowMinutes);

                if (conflictingPeerIsActive)
                {
                    MainThread.BeginInvokeOnMainThread(() => PeerNameConflict?.Invoke(ip, newName, conflict.Key));
                    return;
                }

                // Eski eşleme artık aktif değil (muhtemelen cihaz IP'si değişti/kapandı) - temizle
                _ipToName.TryRemove(conflict.Key, out _);
            }

            _ipToName[ip] = newName;
            MainThread.BeginInvokeOnMainThread(() => PeerFound?.Invoke(newName));
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
                var (key, fingerprint) = await keyExchange.PerformKeyExchangeAsync(stream, ct);
                VerifyTrustOrThrow(remoteIp, fingerprint);
                SessionManager.Instance.CreateOrUpdateSession(remoteIp, key, fingerprint);

                return key;
            }
        }

        // GÜVENLİK DÜZELTMESİ (MITM): Trust-On-First-Use. Bkz. PeerTrustStore.cs.
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

        public async Task RequestDownload(string peerIp, string fileName, SharedFile targetFile)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            IPAddress peerIpAddress = IPAddress.Parse(peerIp);

            try
            {
                MainThread.BeginInvokeOnMainThread(() => targetFile.IsDownloading = true);

                using var client = new TcpClient();
                await client.ConnectAsync(peerIpAddress, TcpPort, cts.Token);

                using var stream = client.GetStream();
                byte[] aesKey = await NegotiateSessionAsync(stream, peerIpAddress, true, cts.Token);

                string folderPath = FileService.DownloadPath;
                if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);

                string safeFileName = Path.GetFileName(fileName);
                string savePath = Path.Combine(folderPath, safeFileName);
                long existingLength = File.Exists(savePath) ? new FileInfo(savePath).Length : 0;

                string cmd = $"REQ:{safeFileName}:{existingLength}";
                byte[] encryptedCmd = EncryptionHelper.EncryptBytes(Encoding.UTF8.GetBytes(cmd), aesKey);
                await FrameIO.WriteFrameAsync(stream, encryptedCmd, cts.Token);

                byte[] nameFrame = await FrameIO.ReadFrameAsync(stream, 1024, cts.Token, "Geçersiz dosya adı boyutu.");
                string responseFileName = Encoding.UTF8.GetString(EncryptionHelper.DecryptBytes(nameFrame, aesKey));

                byte[] lengthFrame = await FrameIO.ReadFrameAsync(stream, 256, cts.Token, "Geçersiz boyut verisi.");
                long totalFileLength = BitConverter.ToInt64(EncryptionHelper.DecryptBytes(lengthFrame, aesKey), 0);

                byte[] hashFrame = await FrameIO.ReadFrameAsync(stream, 1024, cts.Token, "Geçersiz hash verisi.");
                string expectedHash = Encoding.UTF8.GetString(EncryptionHelper.DecryptBytes(hashFrame, aesKey));

                cts.CancelAfter(TimeSpan.FromMinutes(5));

                using var fs = new FileStream(savePath, existingLength > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.ReadWrite);

                long totalRead = existingLength;

                while (totalRead < totalFileLength)
                {
                    byte[] encryptedChunk = await FrameIO.ReadFrameAsync(stream, 10 * 1024 * 1024, cts.Token, "Geçersiz parça boyutu.");
                    if (encryptedChunk.Length == 0) break; // Gönderici transferi tamamladığını bildirdi

                    byte[] plainChunk = EncryptionHelper.DecryptBytes(encryptedChunk, aesKey);

                    await fs.WriteAsync(plainChunk, 0, plainChunk.Length, cts.Token);
                    totalRead += plainChunk.Length;

                    MainThread.BeginInvokeOnMainThread(() => targetFile.Progress = (double)totalRead / totalFileLength);
                }

                await fs.FlushAsync(cts.Token);
                fs.Close();

                // GÜVENLİK DÜZELTMESİ (Dosya Bütünlüğü): Önceden hash kontrolü sadece
                // sıfırdan indirmelerde (existingLength == 0) yapılıyordu; devam eden
                // (resume) indirmeler HİÇ doğrulanmıyordu. Artık her durumda tam dosya
                // hash'i kontrol ediliyor.
                string downloadedHash = FileService.GetFileHash(savePath);
                if (downloadedHash != expectedHash)
                {
                    File.Delete(savePath);
                    throw new Exception("Dosya Bütünlüğü Doğrulanamadı! (Man-In-The-Middle/Bozuk Veri/Kesintiye Uğramış Aktarım)");
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
                SessionManager.Instance.RemoveSession(peerIpAddress);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"İndirme Hatası: {ex.Message}");
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    targetFile.StatusMessage = "Hata ❌";
                    targetFile.IsDownloading = false;
                });
                SessionManager.Instance.RemoveSession(peerIpAddress);
            }
        }

        private async Task HandleIncomingConnection(TcpClient client)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
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

                using var stream = client.GetStream();
                byte[] aesKey = await NegotiateSessionAsync(stream, remoteIp, false, cts.Token);

                byte[] encryptedCmd = await FrameIO.ReadFrameAsync(stream, 2048, cts.Token, "Kötü niyetli komut boyutu tespit edildi.");
                string command = Encoding.UTF8.GetString(EncryptionHelper.DecryptBytes(encryptedCmd, aesKey));

                if (command.StartsWith("REQ:"))
                {
                    var parts = command.Split(':');
                    if (parts.Length < 2) return;
                    string requestedFileName = Path.GetFileName(parts[1]);

                    long offset = 0;
                    if (parts.Length > 2) long.TryParse(parts[2], out offset);

                    var myFiles = FileService.GetSavedFiles();
                    var targetFile = myFiles.FirstOrDefault(f => f.Name == requestedFileName);

                    if (targetFile != null)
                    {
                        byte[] encName = EncryptionHelper.EncryptBytes(Encoding.UTF8.GetBytes(targetFile.Name), aesKey);
                        byte[] encLength = EncryptionHelper.EncryptBytes(BitConverter.GetBytes(targetFile.Length), aesKey);

                        string fileHash = FileService.GetFileHash(targetFile.FullName);
                        byte[] encHash = EncryptionHelper.EncryptBytes(Encoding.UTF8.GetBytes(fileHash), aesKey);

                        await FrameIO.WriteFrameAsync(stream, encName, cts.Token);
                        await FrameIO.WriteFrameAsync(stream, encLength, cts.Token);
                        await FrameIO.WriteFrameAsync(stream, encHash, cts.Token);

                        // Büyük dosya transferi için zaman aşımı genişletiliyor
                        // (el sıkışma/komut fazı hâlâ 20 sn ile sınırlıydı).
                        cts.CancelAfter(TimeSpan.FromMinutes(5));

                        using var fs = new FileStream(targetFile.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        if (offset > 0 && offset < fs.Length)
                        {
                            fs.Seek(offset, SeekOrigin.Begin);
                        }

                        byte[] buffer = ArrayPool<byte>.Shared.Rent(8192);
                        try
                        {
                            int bytesRead;
                            while ((bytesRead = await fs.ReadAsync(buffer, 0, buffer.Length, cts.Token)) > 0)
                            {
                                byte[] encryptedChunk = EncryptionHelper.EncryptBytes(buffer, bytesRead, aesKey);
                                await FrameIO.WriteFrameAsync(stream, encryptedChunk, cts.Token);
                            }
                            await FrameIO.WriteFrameAsync(stream, Array.Empty<byte>(), cts.Token);
                        }
                        finally
                        {
                            ArrayPool<byte>.Shared.Return(buffer);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Transfer Hatası: {ex.Message}");
                if (remoteIp != null) SessionManager.Instance.RemoveSession(remoteIp);
            }
            finally
            {
                if (ipKey != null) _perIpConnections.AddOrUpdate(ipKey, 0, (_, c) => Math.Max(0, c - 1));
                if (acquiredSlot) _connectionLimiter.Release();
                client.Close();
            }
        }
    }
}
