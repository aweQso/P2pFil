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

        public string GetIpByName(string name)
        {
            var match = _ipToName.FirstOrDefault(x => x.Value == name);
            return match.Key ?? string.Empty;
        }

        public event Action<string>? PeerFound;
        public event Action<string, string, string>? PeerNameChanged;
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
            if (_ipToName.TryGetValue(ip, out var oldName))
            {
                if (oldName != newName)
                {
                    _ipToName[ip] = newName;
                    MainThread.BeginInvokeOnMainThread(() => PeerNameChanged?.Invoke(ip, oldName, newName));
                }
            }
            else
            {
                _ipToName[ip] = newName;
                MainThread.BeginInvokeOnMainThread(() => PeerFound?.Invoke(newName));
            }
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
                var (key, fingerprint) = await keyExchange.PerformKeyExchangeAsync(stream, ct);
                SessionManager.Instance.CreateOrUpdateSession(remoteIp, key, fingerprint);

                return key;
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

                using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
                using var reader = new BinaryReader(stream, Encoding.UTF8, true);

                string folderPath = FileService.DownloadPath;
                if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);

                string safeFileName = Path.GetFileName(fileName);
                string savePath = Path.Combine(folderPath, safeFileName);
                long existingLength = File.Exists(savePath) ? new FileInfo(savePath).Length : 0;

                string cmd = $"REQ:{safeFileName}:{existingLength}";
                byte[] encryptedCmd = EncryptionHelper.EncryptBytes(Encoding.UTF8.GetBytes(cmd), aesKey);
                writer.Write(encryptedCmd.Length);
                writer.Write(encryptedCmd);
                writer.Flush();

                int nameLen = reader.ReadInt32();
                if (nameLen <= 0 || nameLen > 1024) throw new InvalidDataException("Geçersiz dosya adı boyutu.");
                string responseFileName = Encoding.UTF8.GetString(EncryptionHelper.DecryptBytes(reader.ReadBytes(nameLen), aesKey));

                int lengthLen = reader.ReadInt32();
                if (lengthLen <= 0 || lengthLen > 256) throw new InvalidDataException("Geçersiz boyut verisi.");
                long totalFileLength = BitConverter.ToInt64(EncryptionHelper.DecryptBytes(reader.ReadBytes(lengthLen), aesKey), 0);

                int hashLen = reader.ReadInt32();
                if (hashLen <= 0 || hashLen > 1024) throw new InvalidDataException("Geçersiz hash verisi.");
                string expectedHash = Encoding.UTF8.GetString(EncryptionHelper.DecryptBytes(reader.ReadBytes(hashLen), aesKey));

                cts.CancelAfter(TimeSpan.FromMinutes(5));

                using var fs = new FileStream(savePath, existingLength > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.ReadWrite);

                long totalRead = existingLength;

                while (totalRead < totalFileLength)
                {
                    if (client.Available == 0 && cts.Token.IsCancellationRequested) break;

                    int chunkLen = reader.ReadInt32();
                    if (chunkLen <= 0 || chunkLen > 10 * 1024 * 1024) break;

                    byte[] encryptedChunk = reader.ReadBytes(chunkLen);
                    byte[] plainChunk = EncryptionHelper.DecryptBytes(encryptedChunk, aesKey);

                    await fs.WriteAsync(plainChunk, 0, plainChunk.Length, cts.Token);
                    totalRead += plainChunk.Length;

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
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            IPAddress? remoteIp = null;
            try
            {
                var remoteEndPoint = client.Client.RemoteEndPoint as IPEndPoint;
                remoteIp = remoteEndPoint?.Address;
                if (remoteIp == null) return;

                using var stream = client.GetStream();
                byte[] aesKey = await NegotiateSessionAsync(stream, remoteIp, false, cts.Token);

                using var reader = new BinaryReader(stream, Encoding.UTF8, true);
                using var writer = new BinaryWriter(stream, Encoding.UTF8, true);

                int cmdLen = reader.ReadInt32();
                if (cmdLen <= 0 || cmdLen > 2048) throw new InvalidDataException("Kötü niyetli komut boyutu tespit edildi.");

                byte[] encryptedCmd = reader.ReadBytes(cmdLen);
                string command = Encoding.UTF8.GetString(EncryptionHelper.DecryptBytes(encryptedCmd, aesKey));

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
                        byte[] encName = EncryptionHelper.EncryptBytes(Encoding.UTF8.GetBytes(targetFile.Name), aesKey);
                        byte[] encLength = EncryptionHelper.EncryptBytes(BitConverter.GetBytes(targetFile.Length), aesKey);

                        string fileHash = FileService.GetFileHash(targetFile.FullName);
                        byte[] encHash = EncryptionHelper.EncryptBytes(Encoding.UTF8.GetBytes(fileHash), aesKey);

                        writer.Write(encName.Length); writer.Write(encName);
                        writer.Write(encLength.Length); writer.Write(encLength);
                        writer.Write(encHash.Length); writer.Write(encHash);
                        writer.Flush();

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
                                // C# 12 UYUMLULUĞU: Span yerine doğrudan Buffer ve Okunan Byte Sayısı gönderiliyor.
                                byte[] encryptedChunk = EncryptionHelper.EncryptBytes(buffer, bytesRead, aesKey);

                                writer.Write(encryptedChunk.Length);
                                writer.Write(encryptedChunk);
                            }
                            writer.Write(0);
                            writer.Flush();
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
                client.Close();
            }
        }
    }
}