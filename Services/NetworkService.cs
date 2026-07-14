using P2PFil.ChatModule;
using P2PFil.Models;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;

namespace P2PFil.Services
{
    public class NetworkService : IDisposable
    {
        private const int UdpPort = 8888;
        private const int TcpPort = 8889;

        private UdpClient? _udpClient;
        private TcpListener? _tcpListener;
        private CancellationTokenSource? _serviceCts; // YENİ: Arka plan görevlerini güvenle kapatmak için

        private readonly ConcurrentDictionary<string, string> _deviceIdToName = new();
        private readonly ConcurrentDictionary<string, string> _deviceIdToIp = new();
        private readonly ConcurrentDictionary<string, string> _ipToDeviceId = new();
        private readonly ConcurrentDictionary<string, DateTime> _lastSeen = new();
        private const int NameConflictWindowMinutes = 10;

        private readonly SemaphoreSlim _connectionLimiter = new(50, 50);
        private readonly ConcurrentDictionary<string, int> _perIpConnections = new();
        private const int MaxConnectionsPerIp = 5;

        // Ağ paket boyutunu büyüttük (Önceden 8192 idi, şimdi 64KB)
        private const int FileTransferBufferSize = 65536;

        public event Action<string>? PeerFound;
        public event Action<string, string, string>? PeerNameChanged;
        public event Action<string, List<SharedFile>>? FilesReceived;

        public string GetIpByName(string name)
        {
            var deviceId = _deviceIdToName.FirstOrDefault(x => x.Value == name).Key;
            return string.IsNullOrEmpty(deviceId) ? string.Empty : GetIpByDeviceId(deviceId);
        }

        public string GetDeviceIdByName(string name)
        {
            return _deviceIdToName.FirstOrDefault(x => x.Value == name).Key ?? string.Empty;
        }

        public string GetIpByDeviceId(string deviceId)
        {
            return _deviceIdToIp.TryGetValue(deviceId, out var ip) ? ip : string.Empty;
        }

        public void StartDiscovery()
        {
            // YENİ: Eğer servis zaten çalışıyorsa, eski thread'leri temizle (Zombi thread sızıntısını önler)
            _serviceCts?.Cancel();
            _serviceCts?.Dispose();
            _serviceCts = new CancellationTokenSource();
            var token = _serviceCts.Token;

            if (_udpClient != null)
            {
                _udpClient.Close();
                _udpClient.Dispose();
            }

            _udpClient = new UdpClient(UdpPort) { EnableBroadcast = true };

            // 1. UDP Dinleme Görevi
            Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        var result = await _udpClient.ReceiveAsync(token);
                        if (result.Buffer.Length > 8192) continue;

                        var message = Encoding.UTF8.GetString(result.Buffer);
                        var senderIp = result.RemoteEndPoint.Address.ToString();

                        if (message.StartsWith("{"))
                        {
                            var networkMsg = JsonSerializer.Deserialize<NetworkMessage>(message);

                            if (networkMsg != null &&
                                networkMsg.Sender != App.CurrentUsername &&
                                !string.IsNullOrWhiteSpace(networkMsg.DeviceId))
                            {
                                HandlePeerDiscovery(senderIp, networkMsg.DeviceId, networkMsg.Sender);

                                if (networkMsg.Files != null && networkMsg.Files.Count > 0)
                                {
                                    MainThread.BeginInvokeOnMainThread(() =>
                                        FilesReceived?.Invoke(senderIp, networkMsg.Files));
                                }
                            }
                        }
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"UDP Receive Hata: {ex.Message}"); }
                }
            }, token);

            // 2. TCP Dinleme Görevi
            if (_tcpListener == null)
            {
                Task.Run(async () =>
                {
                    _tcpListener = new TcpListener(IPAddress.Any, TcpPort);
                    _tcpListener.Start();
                    while (!token.IsCancellationRequested)
                    {
                        try
                        {
                            var client = await _tcpListener.AcceptTcpClientAsync(token);
                            _ = HandleIncomingConnection(client, token);
                        }
                        catch (OperationCanceledException) { break; }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"TCP Accept Hata: {ex.Message}");
                        }
                    }
                    _tcpListener.Stop();
                    _tcpListener = null;
                }, token);
            }

            // 3. UDP Yayın (Broadcast) Görevi
            Task.Run(async () =>
            {
                var endpoint = new IPEndPoint(IPAddress.Broadcast, UdpPort);
                DateTime lastFileCheck = DateTime.MinValue;
                byte[]? cachedMessageBytes = null;

                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        // YENİ PERFORMANS İYİLEŞTİRMESİ: Disk I/O darboğazını engellemek için dosyaları 5 saniyede bir değil, 30 saniyede bir diske soruyoruz.
                        if (cachedMessageBytes == null || (DateTime.Now - lastFileCheck).TotalSeconds > 30)
                        {
                            var myFiles = FileService.GetSavedFiles().Take(20).Select(f => new SharedFile
                            {
                                FileName = f.Name,
                                OwnerName = App.CurrentUsername,
                                FileSize = $"{f.Length / 1024 / 1024.0:F2} MB",
                                UploadDate = f.LastWriteTime
                            }).ToList();

                            var messageObj = new NetworkMessage
                            {
                                Type = "FILE_LIST",
                                Sender = App.CurrentUsername,
                                DeviceId = App.DeviceId,
                                Files = myFiles
                            };

                            cachedMessageBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(messageObj));
                            lastFileCheck = DateTime.Now;
                        }

                        if (_udpClient?.Client != null)
                        {
                            await _udpClient.SendAsync(cachedMessageBytes, cachedMessageBytes.Length, endpoint);
                        }
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"UDP Send Hata: {ex.Message}"); }

                    await Task.Delay(5000, token);
                }
            }, token);
        }

        private void HandlePeerDiscovery(string ip, string deviceId, string newName)
        {
            _lastSeen[deviceId] = DateTime.Now;

            if (!PeerTrustStore.Instance.ValidateOrBindName(newName, deviceId)) return;

            // IP Değişimi kontrolü
            if (_deviceIdToIp.TryGetValue(deviceId, out var oldIp) && oldIp != ip)
            {
                SessionManager.Instance.RemoveSession(IPAddress.Parse(oldIp));
                _ipToDeviceId.TryRemove(oldIp, out _);
            }

            if (_ipToDeviceId.TryGetValue(ip, out var oldDeviceId) && oldDeviceId != deviceId)
            {
                _deviceIdToIp.TryRemove(oldDeviceId, out _);
            }

            // İsim değişimi kontrolü
            if (_deviceIdToName.TryGetValue(deviceId, out var oldName))
            {
                _deviceIdToIp[deviceId] = ip;
                _ipToDeviceId[ip] = deviceId;

                if (oldName != newName)
                {
                    _deviceIdToName[deviceId] = newName;
                    MainThread.BeginInvokeOnMainThread(() => PeerNameChanged?.Invoke(ip, oldName, newName));
                }
                return;
            }

            // İsim Çakışması Kontrolü
            var conflict = _deviceIdToName.FirstOrDefault(x => x.Value == newName && x.Key != deviceId);
            if (!string.IsNullOrEmpty(conflict.Key))
            {
                bool conflictingPeerIsActive = _lastSeen.TryGetValue(conflict.Key, out var lastSeen)
                    && (DateTime.Now - lastSeen) < TimeSpan.FromMinutes(NameConflictWindowMinutes);

                if (conflictingPeerIsActive) return;

                _deviceIdToName.TryRemove(conflict.Key, out _);
                if (_deviceIdToIp.TryRemove(conflict.Key, out var conflictIp))
                {
                    _ipToDeviceId.TryRemove(conflictIp, out _);
                }
            }

            _deviceIdToName[deviceId] = newName;
            _deviceIdToIp[deviceId] = ip;
            _ipToDeviceId[ip] = deviceId;

            MainThread.BeginInvokeOnMainThread(() => PeerFound?.Invoke(newName));
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

                byte[] aesKey = await KeyExchangeService.NegotiateSessionAsync(stream, peerIpAddress, true, cts.Token);

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

                cts.CancelAfter(TimeSpan.FromMinutes(10)); // Büyük dosyalar için süre uzatıldı

                using var fs = new FileStream(savePath, existingLength > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                long totalRead = existingLength;

                while (totalRead < totalFileLength)
                {
                    byte[] encryptedChunk = await FrameIO.ReadFrameAsync(stream, FileTransferBufferSize * 2, cts.Token, "Geçersiz parça boyutu.");
                    if (encryptedChunk.Length == 0) break;

                    byte[] plainChunk = EncryptionHelper.DecryptBytes(encryptedChunk, aesKey);
                    await fs.WriteAsync(plainChunk, 0, plainChunk.Length, cts.Token);
                    totalRead += plainChunk.Length;

                    MainThread.BeginInvokeOnMainThread(() => targetFile.Progress = (double)totalRead / totalFileLength);
                }

                await fs.FlushAsync(cts.Token);
                fs.Close();

                string downloadedHash = FileService.GetFileHash(savePath);
                if (downloadedHash != expectedHash)
                {
                    File.Delete(savePath);
                    throw new Exception("Dosya Bütünlüğü Doğrulanamadı!");
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

        private async Task HandleIncomingConnection(TcpClient client, CancellationToken globalToken)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(globalToken);
            cts.CancelAfter(TimeSpan.FromSeconds(20));

            IPAddress? remoteIp = null;
            bool acquiredSlot = false;
            string? ipKey = null;

            try
            {
                var remoteEndPoint = client.Client?.RemoteEndPoint as IPEndPoint;
                remoteIp = remoteEndPoint?.Address;
                if (remoteIp == null) return;
                ipKey = remoteIp.ToString();

                if (!await _connectionLimiter.WaitAsync(TimeSpan.FromSeconds(2), cts.Token)) return;
                acquiredSlot = true;

                int current = _perIpConnections.AddOrUpdate(ipKey, 1, (_, c) => c + 1);
                if (current > MaxConnectionsPerIp) return;

                using var stream = client.GetStream();

                byte[] aesKey = await KeyExchangeService.NegotiateSessionAsync(stream, remoteIp, false, cts.Token);

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

                        cts.CancelAfter(TimeSpan.FromMinutes(10)); // Büyük dosyalar için

                        using var fs = new FileStream(targetFile.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        if (offset > 0 && offset < fs.Length) fs.Seek(offset, SeekOrigin.Begin);

                        byte[] buffer = ArrayPool<byte>.Shared.Rent(FileTransferBufferSize);
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

        public void Dispose()
        {
            _serviceCts?.Cancel();
            _serviceCts?.Dispose();
            _udpClient?.Dispose();
            _tcpListener?.Stop();
        }
    }
}