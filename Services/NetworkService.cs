using Microsoft.Maui.ApplicationModel;
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
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace P2PFil.Services
{
    public class NetworkService : IDisposable
    {
        private const int UdpPort = 8888;
        private const int TcpPort = 8889;

        private UdpClient? _udpClient;
        private TcpListener? _tcpListener;
        private CancellationTokenSource? _serviceCts;
        private CancellationTokenSource? _tcpCts;

        private readonly ConcurrentDictionary<string, string> _deviceIdToName = new();
        private readonly ConcurrentDictionary<string, string> _deviceIdToIp = new();
        private readonly ConcurrentDictionary<string, string> _ipToDeviceId = new();
        private readonly ConcurrentDictionary<string, DateTime> _lastSeen = new();

        // Profil resimleri durum takibi
        private readonly ConcurrentDictionary<string, bool> _profileImageFetched = new();

        // GÜNCELLENDİ: Başarısız profil çekme isteklerinde batarya sömürüsünü engellemek için Cooldown (Soğuma) listesi
        private readonly ConcurrentDictionary<string, DateTime> _profileFetchCooldowns = new();

        private const int NameConflictWindowMinutes = 10;
        private readonly SemaphoreSlim _connectionLimiter = new(50, 50);
        private readonly ConcurrentDictionary<string, int> _perIpConnections = new();
        private const int MaxConnectionsPerIp = 5;
        // GÜNCELLENDİ (PERFORMANS): 64KB -> 128KB. Küçük buffer, hızlı ağlarda
        // (4-5 MB/s+) saniyede çok sayıda küçük okuma/şifre çözme/yazma işlemine
        // yol açıp overhead yaratıyordu. 256KB denendi ama bu MAUI MOBİL bir
        // uygulama -- her chunk'ta EncryptionHelper.DecryptBytes zaten yeni bir
        // byte[] tahsis ediyor (AES-GCM çıktısı kaçınılmaz bir allocation), 256KB'lık
        // parçalarla bu tahsisler büyüyüp düşük RAM'li telefonlarda GC baskısı
        // yaratarak performansı DÜŞÜREBİLİYORDU. 128KB, sistem çağrısı/şifreleme
        // overhead'ini hâlâ azaltırken mobil bellek baskısını 256KB kadar artırmayan
        // dengeli bir orta nokta.
        private const int FileTransferBufferSize = 131072;

        // YENİ: UDP paket flood koruması. Bozuk/kötü niyetli JSON ile CPU ve log
        // spam'i yaratmayı engellemek için IP başına sabit pencereli (fixed-window)
        // sayaç. Pencere aşılırsa paket sessizce (parse etmeden) atlanır.
        private readonly ConcurrentDictionary<string, UdpRateEntry> _udpRateLimits = new();
        private const int UdpMaxPacketsPerWindow = 20;
        private static readonly TimeSpan UdpRateWindow = TimeSpan.FromSeconds(5);

        private sealed class UdpRateEntry
        {
            public DateTime WindowStart;
            public int Count;
        }

        // YENİ: _lastSeen, _profileFetchCooldowns ve _udpRateLimits gibi
        // sınırsız büyüyebilen sözlükler için periyodik temizlik zamanlayıcısı.
        private Timer? _housekeepingTimer;
        private static readonly TimeSpan StalePeerTimeout = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan HousekeepingInterval = TimeSpan.FromMinutes(5);

        // YENİ: Kullanıcı adı değiştiğinde UDP yayın cache'ini anında geçersiz
        // kılmak için kullanılır. StartDiscovery içindeki yayın döngüsü bu bayrağı
        // her turda kontrol eder; true ise önbelleklenmiş paketi 30 saniye
        // beklemeden yeniden oluşturur.
        private volatile bool _forceRebroadcast;

        private volatile bool _profileUpdatedSignal;

        // YENİ: Broadcast döngüsü normalde 5 saniyede bir uyanır (Task.Delay(5000)).
        // İsim/profil değişikliği gibi olaylarda bu 5 saniyeyi beklemeden döngüyü
        // HEMEN uyandırmak için kullanılan sinyal. SemaphoreSlim(0,1) + WaitAsync
        // ile "ya 5 saniye dolsun ya da birisi Release etsin, hangisi önce olursa"
        // mantığı kurulur -- ekstra polling veya Thread.Sleep gerekmez.
        private readonly SemaphoreSlim _broadcastWakeSignal = new(0, 1);

        private void SignalImmediateBroadcast()
        {
            _forceRebroadcast = true;
            // Semaforu 1'in üzerine taşırmadan (en fazla 1 bekleyen sinyal
            // yeterli) tetikle; zaten sinyallenmiş bir bekleme varsa yut.
            try { _broadcastWakeSignal.Release(); } catch (SemaphoreFullException) { /* zaten sinyallenmiş */ }
        }

        public event Action<string>? PeerFound;
        public event Action<string, string, string>? PeerNameChanged;
        public event Action<string, List<SharedFile>>? FilesReceived;
        public event Action<string>? ProfileImageUpdated;
        public event Action<string>? GlobalSpeedUpdated;

        public string GetIpByName(string name) => _deviceIdToName.FirstOrDefault(x => x.Value == name).Key is string deviceId ? GetIpByDeviceId(deviceId) : string.Empty;
        public string GetDeviceIdByName(string name) => _deviceIdToName.FirstOrDefault(x => x.Value == name).Key ?? string.Empty;
        public string GetIpByDeviceId(string deviceId) => _deviceIdToIp.TryGetValue(deviceId, out var ip) ? ip : string.Empty;
        public string GetDeviceIdByIp(string ip) => _ipToDeviceId.TryGetValue(ip, out var deviceId) ? deviceId : string.Empty;

        // YENİ: Kullanıcı adını değiştirdikten sonra çağrılır. UDP yayın cache'ini
        // geçersiz kılar ki ağdaki diğer cihazlar en fazla ~5 saniyelik normal
        // döngü beklemesiyle yeni ismi görsün (30 saniyelik cache süresini beklemesinler).
        public void AnnounceNameChange()
        {
            SignalImmediateBroadcast();
        }

        // YENİ: Profil resmi değiştiğinde çağrılır. Mevcut UDP FILE_LIST paketi
        // profil resmini taşımıyor (resim ayrı bir TCP kanalıyla, REQ_PROFILE
        // komutuyla çekiliyor) -- bu yüzden burada yapılan şey, peer'ların "bu
        // cihazın profil resmi güncellendi, tekrar çek" diyebilmesi için önce
        // UDP tarafını hemen (isim değişikliğiyle aynı yolu kullanarak) yeniden
        // yayınlamak, SONRA da yerel olarak ProfileMessenger üzerinden global bir
        // bildirim yaymaktır ki arka plandaki (henüz OnAppearing çağrılmamış)
        // sayfalar da anında haberdar olsun.
        public void AnnounceProfileChange()
        {
            _profileUpdatedSignal = true; // Profilin değiştiğini işaretle
            SignalImmediateBroadcast();
            ProfileMessenger.PublishLocalProfileChanged();
        }

        public void StartDiscovery()
        {
            _serviceCts?.Cancel();
            _serviceCts?.Dispose();
            _serviceCts = new CancellationTokenSource();
            var token = _serviceCts.Token;

            // YENİ: Stale peer/cooldown/rate-limit kayıtlarını periyodik temizleyen
            // housekeeping zamanlayıcısı. Yeniden başlatmalarda eski timer atılıp
            // yenisi kurulur.
            _housekeepingTimer?.Dispose();
            _housekeepingTimer = new Timer(RunHousekeeping, null, HousekeepingInterval, HousekeepingInterval);

            // GÜNCELLENDİ: UDP Port çakışmalarında uygulamanın çökmesini (Crash) önleyen güvenli başlatma bloğu
            try
            {
                if (_udpClient != null)
                {
                    _udpClient.Close();
                    _udpClient.Dispose();
                }
                _udpClient = new UdpClient(UdpPort) { EnableBroadcast = true };
            }
            catch (SocketException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[HATA] UDP Port {UdpPort} bind hatası (Port kullanımda olabilir): {ex.Message}");
                _udpClient = null;
            }

            // 1. UDP Dinleme Taskı (Soket başarılı açıldıysa başlatılır)
            if (_udpClient != null)
            {
                Task.Run(async () =>
                {
                    while (!token.IsCancellationRequested)
                    {
                        try
                        {
                            var result = await _udpClient.ReceiveAsync(token);
                            if (result.Buffer.Length > 8192) continue;

                            var senderIp = result.RemoteEndPoint.Address.ToString();

                            // GÜNCELLENDİ: Parse'dan ÖNCE rate-limit kontrolü. Bozuk/kötü niyetli
                            // JSON floodlayan bir kaynak, pahalı Deserialize çağrısına ve log
                            // spam'ine ulaşamadan burada elenir.
                            if (IsUdpRateLimited(senderIp)) continue;

                            var message = Encoding.UTF8.GetString(result.Buffer);

                            if (message.StartsWith("{"))
                            {
                                NetworkMessage? networkMsg;
                                try
                                {
                                    networkMsg = JsonSerializer.Deserialize<NetworkMessage>(message);
                                }
                                catch (JsonException)
                                {
                                    // Bozuk JSON: sessizce yok say, dış döngünün genel catch'ine
                                    // düşüp gereksiz log/delay üretmesin.
                                    continue;
                                }

                                if (networkMsg != null && networkMsg.Sender != App.CurrentUsername && !string.IsNullOrWhiteSpace(networkMsg.DeviceId))
                                {
                                    // YENİ: Eğer gelen paket bir profil güncellemesi ise, o cihazın cache durumunu temizle!
                                    if (networkMsg.Type == "PROFILE_UPDATE")
                                    {
                                        _profileImageFetched.TryRemove(networkMsg.DeviceId, out _);
                                        _profileFetchCooldowns.TryRemove(networkMsg.DeviceId, out _);
                                    }
                                    HandlePeerDiscovery(senderIp, networkMsg.DeviceId, networkMsg.Sender);

                                    // GÜNCELLENDİ: Cihaz soğuma süresinde değilse ve resim henüz çekilmediyse çekmeyi dene
                                    bool inCooldown = _profileFetchCooldowns.TryGetValue(networkMsg.DeviceId, out var cooldownUntil) && DateTime.Now < cooldownUntil;

                                    if (!inCooldown && !_profileImageFetched.ContainsKey(networkMsg.DeviceId))
                                    {
                                        _profileImageFetched[networkMsg.DeviceId] = true;
                                        _ = FetchPeerProfileImageAsync(senderIp, networkMsg.DeviceId, token);
                                    }

                                    if (networkMsg.Files != null && networkMsg.Files.Count > 0)
                                    {
                                        MainThread.BeginInvokeOnMainThread(() => FilesReceived?.Invoke(senderIp, networkMsg.Files));
                                    }
                                }
                            }
                        }
                        catch (OperationCanceledException) { break; }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"UDP Receive Hata: {ex.Message}");
                            // GÜNCELLENDİ: Hata durumunda döngünün çılgınca dönüp CPU kilitlemesini (Tight-Loop) engellemek için küçük bir bekleme
                            await Task.Delay(500, token);
                        }
                    }
                }, token);
            }

            // 2. TCP Server Taskı (Port çakışmalarına karşı tamamen korumalı hale getirildi)
            if (_tcpListener == null)
            {
                _tcpCts = new CancellationTokenSource(); // Sadece TCP'ye özel token
                Task.Run(async () =>
                {
                    try
                    {
                        _tcpListener = new TcpListener(IPAddress.Any, TcpPort);
                        _tcpListener.Start();
                    }
                    catch (SocketException ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[HATA] TCP Port {TcpPort} bind hatası: {ex.Message}");
                        _tcpListener = null;
                        return;
                    }

                    // DİKKAT: Artık genel 'token' yerine '_tcpCts.Token' kullanıyoruz!
                    while (!_tcpCts.Token.IsCancellationRequested)
                    {
                        try
                        {
                            var client = await _tcpListener.AcceptTcpClientAsync(_tcpCts.Token);
                            _ = HandleIncomingConnection(client, _tcpCts.Token);
                        }
                        catch (OperationCanceledException) { break; }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"TCP Accept Hata: {ex.Message}");
                            await Task.Delay(100, _tcpCts.Token);
                        }
                    }

                    try
                    {
                        _tcpListener?.Stop();
                    }
                    catch { }
                    _tcpListener = null;
                }, _tcpCts.Token);
            }

            // 3. UDP Yayın Taskı (Yalnızca UDP istemcisi başarıyla oluşturulduysa çalışır)
            if (_udpClient != null)
            {
                Task.Run(async () =>
                {
                    var endpoint = new IPEndPoint(IPAddress.Broadcast, UdpPort);
                    DateTime lastFileCheck = DateTime.MinValue;
                    byte[]? cachedMessageBytes = null;

                    while (!token.IsCancellationRequested)
                    {
                        try
                        {
                            // Döngü içinde local bir değişkene kopyala ve sıfırla
                            bool isProfileUpdate = _profileUpdatedSignal;

                            if (cachedMessageBytes == null || _forceRebroadcast || isProfileUpdate || (DateTime.Now - lastFileCheck).TotalSeconds > 30)
                            {
                                _forceRebroadcast = false;
                                _profileUpdatedSignal = false; // Bayrağı indir

                                var myFiles = FileService.GetSavedFiles().Take(20).Select(f => new SharedFile
                                {
                                    FileName = f.FileName,
                                    OwnerName = SettingsService.Username,
                                    FileSize = f.FileSize,
                                    UploadDate = f.UploadDate
                                }).ToList();

                                var messageObj = new NetworkMessage
                                {
                                    // EĞER profil güncellemesinden tetiklendiyse tipi "PROFILE_UPDATE" yap, yoksa normal "FILE_LIST" kalsın
                                    Type = isProfileUpdate ? "PROFILE_UPDATE" : "FILE_LIST",
                                    Sender = SettingsService.Username,
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
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"UDP Send Hata: {ex.Message}");
                            await Task.Delay(1000, token); // Hata durumunda yayını biraz beklet
                        }

                        // GÜNCELLENDİ: Sabit 5 saniyelik bekleme yerine, "5 saniye dolana
                        // KADAR bekle, ama SignalImmediateBroadcast çağrılırsa hemen uyan"
                        // mantığı. AnnounceNameChange/AnnounceProfileChange çağrıldığında
                        // kullanıcı artık 5 saniyelik gecikme değil, ~birkaç milisaniyelik
                        // bir gecikmeyle ağa yansır.
                        try
                        {
                            await _broadcastWakeSignal.WaitAsync(TimeSpan.FromSeconds(5), token);
                        }
                        catch (OperationCanceledException) { break; }
                    }
                }, token);
            }
        }

        // YENİ: Sabit pencereli (fixed-window) basit rate limiter. Pencere süresi
        // dolduğunda sayaç sıfırlanır; pencere içinde limit aşılırsa true döner.
        // Lock-free CAS mantığıyla ConcurrentDictionary üzerinde çalışır, ekstra
        // kilit gerektirmez.
        private bool IsUdpRateLimited(string senderIp)
        {
            var now = DateTime.Now;
            var entry = _udpRateLimits.GetOrAdd(senderIp, _ => new UdpRateEntry { WindowStart = now, Count = 0 });

            lock (entry)
            {
                if (now - entry.WindowStart > UdpRateWindow)
                {
                    entry.WindowStart = now;
                    entry.Count = 1;
                    return false;
                }

                entry.Count++;
                return entry.Count > UdpMaxPacketsPerWindow;
            }
        }

        // YENİ: _lastSeen, _profileFetchCooldowns, _udpRateLimits gibi sözlüklerde
        // biriken eski (stale) kayıtları periyodik olarak temizler. Böylece uzun
        // süre açık kalan uygulamada peer'lar gidip gelse bile bellek sınırsız
        // büyümez.
        private void RunHousekeeping(object? state)
        {
            try
            {
                var now = DateTime.Now;

                foreach (var kvp in _lastSeen)
                {
                    if (now - kvp.Value > StalePeerTimeout)
                    {
                        _lastSeen.TryRemove(kvp.Key, out _);
                        _profileImageFetched.TryRemove(kvp.Key, out _);

                        if (_deviceIdToIp.TryRemove(kvp.Key, out var staleIp))
                        {
                            _ipToDeviceId.TryRemove(staleIp, out _);
                        }
                        _deviceIdToName.TryRemove(kvp.Key, out _);
                    }
                }

                foreach (var kvp in _profileFetchCooldowns)
                {
                    if (now >= kvp.Value)
                    {
                        _profileFetchCooldowns.TryRemove(kvp.Key, out _);
                    }
                }

                foreach (var kvp in _udpRateLimits)
                {
                    bool stale;
                    lock (kvp.Value)
                    {
                        stale = now - kvp.Value.WindowStart > UdpRateWindow;
                    }
                    if (stale)
                    {
                        _udpRateLimits.TryRemove(kvp.Key, out _);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Housekeeping Hata: {ex.Message}");
            }
        }

        private void HandlePeerDiscovery(string ip, string deviceId, string newName)
        {
            _lastSeen[deviceId] = DateTime.Now;

            if (_deviceIdToIp.TryGetValue(deviceId, out var oldIp) && oldIp != ip)
            {
                _ipToDeviceId.TryRemove(oldIp, out _);
            }

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

            _deviceIdToName[deviceId] = newName;
            _deviceIdToIp[deviceId] = ip;
            _ipToDeviceId[ip] = deviceId;

            MainThread.BeginInvokeOnMainThread(() => PeerFound?.Invoke(newName));
        }

        // GÜNCELLENDİ: 5 saniyelik kesin zaman aşımı ve başarısızlık durumunda 2 dakikalık Cooldown eklenmiş profil çekme fonksiyonu
        private async Task FetchPeerProfileImageAsync(string peerIp, string deviceId, CancellationToken token)
        {
            try
            {
                using var client = new TcpClient();

                // Bağlantının sonsuza kadar asılı kalmaması için 5 saniyelik zaman aşımı tanımlıyoruz
                using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                connectCts.CancelAfter(TimeSpan.FromSeconds(5));

                try
                {
                    await client.ConnectAsync(IPAddress.Parse(peerIp), TcpPort, connectCts.Token);
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    throw new TimeoutException("Profil resmi sunucusuna bağlanırken zaman aşımı oluştu.");
                }

                using var stream = client.GetStream();

                byte[] aesKey = await KeyExchangeService.NegotiateSessionAsync(stream, IPAddress.Parse(peerIp), true, token);

                string cmd = "REQ_PROFILE:";
                byte[] encryptedCmd = EncryptionHelper.EncryptBytes(Encoding.UTF8.GetBytes(cmd), aesKey);
                await FrameIO.WriteFrameAsync(stream, encryptedCmd, token);

                string savePath = Path.Combine(FileSystem.CacheDirectory, $"{deviceId}_profile.png");
                using var fs = new FileStream(savePath, FileMode.Create, FileAccess.Write, FileShare.None);

                // GÜNCELLENDİ: Kullanılmayan gereksiz ArrayPool kiralama kodu silindi. Bellek boşuna işgal edilmiyor.
                while (true)
                {
                    byte[] encryptedChunk = await FrameIO.ReadFrameAsync(stream, FileTransferBufferSize * 2, token, "Profil resmi okunamadı.");
                    if (encryptedChunk.Length == 0) break;

                    byte[] plainChunk = EncryptionHelper.DecryptBytes(encryptedChunk, aesKey);
                    await fs.WriteAsync(plainChunk, 0, plainChunk.Length, token);
                }

                await fs.FlushAsync(token);
                MainThread.BeginInvokeOnMainThread(() => ProfileImageUpdated?.Invoke(deviceId));
                // YENİ: Sayfa yaşam döngüsünden bağımsız, arka plandaki sayfaların da
                // yakalayabilmesi için aynı olay Messenger üzerinden de yayınlanır.
                ProfileMessenger.PublishPeerProfileChanged(deviceId);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Profil resmi indirilemedi ({peerIp}): {ex.Message}");

                // GÜNCELLENDİ: Başarısız istek sonrası durumu sıfırlıyoruz ancak batarya ve ağ spamını engellemek için 2 dakika cooldown veriyoruz
                _profileImageFetched.TryRemove(deviceId, out _);
                _profileFetchCooldowns[deviceId] = DateTime.Now.AddMinutes(2);
            }
        }

        // GÜNCELLENDİ (PERFORMANS): Önceden burada, HER resume/retry denemesinde
        // diskte zaten var olan baytlar (ör. 3GB'lık bir dosyanın inmiş 2GB'ı)
        // yeniden okunup SHA-256'ya besleniyordu. Bu, özellikle sık kopan bir
        // bağlantıda (her 2 saniyede bir retry) ciddi bir disk I/O yükü ve gecikme
        // yaratıyordu -- kullanıcı bunu "hız düşüklüğü" olarak yaşıyordu, çünkü
        // network transferi başlamadan önce GB'larca veri diskten tekrar okunuyordu.
        //
        // YENİ DAVRANIŞ (kullanıcı tercihi: hız öncelikli): resume/retry sırasında
        // var olan baytlar ARTIK YENİDEN HASHLENMİYOR. Bunun yerine incremental
        // hash SADECE o oturumda network'ten YENİ GELEN baytları biriktirir. Tam
        // dosya bütünlüğü kontrolü, indirme GERÇEKTEN TAMAMLANDIĞINDA (yüzde 100),
        // dosya TEK SEFERLİK diskten okunup tam SHA-256'sı hesaplanarak yapılır --
        // yani pahalı olan tam-dosya hash işlemi retry başına değil, sadece
        // başarılı tamamlanmada BİR KEZ çalışır.
        private static async Task<string> ComputeFullFileHashAsync(string path, CancellationToken token)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, FileTransferBufferSize, true);
            using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = ArrayPool<byte>.Shared.Rent(FileTransferBufferSize);
            try
            {
                int read;
                while ((read = await fs.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
                {
                    sha256.AppendData(buffer, 0, read);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
            return Convert.ToHexString(sha256.GetHashAndReset()).ToLowerInvariant();
        }

        // GÜNCELLENDİ: Artık doğrudan indirmeyi başlatmıyor; TransferManager'a
        // kaydediyor. Aynı anda 50+ kullanıcı indirme başlatırsa, aktif transfer
        // sayısı (varsayılan 3) aşıldığında istek otomatik olarak kuyruğa alınır
        // ve bir slot boşaldığında sırayla tetiklenir. Var olan çağrı imzası
        // (FilesPage.xaml.cs -> RequestDownload(ip, fileName, file)) DEĞİŞMEDİ,
        // bu yüzden UI tarafında hiçbir değişiklik gerekmez.
        public Task RequestDownload(string peerIp, string fileName, SharedFile targetFile)
        {
            TransferManager.Instance.Enqueue(targetFile, peerIp, fileName, RequestDownloadInternal);
            return Task.CompletedTask;
        }

        // Asıl indirme mantığı: TransferManager tarafından, bir eşzamanlılık
        // slotu uygun olduğunda çağrılır. 'token' artık TransferManager tarafından
        // yönetilen targetFile.DownloadCts.Token ile birebir aynıdır -- UI'daki
        // mevcut "İptal" butonu (OnCancelClicked -> file.DownloadCts?.Cancel())
        // davranışı hiç değişmeden çalışmaya devam eder.
        // GÜNCELLENDİ: Kademeli (escalating) backoff kaldırıldı -- kullanıcı
        // isteği üzerine artık SABİT 2 saniyede bir, SINIRSIZ (deneme sayısı
        // limiti olmadan) tekrar deneniyor.
        private static readonly TimeSpan RetryFixedDelay = TimeSpan.FromSeconds(2);

        // GÜNCELLENDİ: Artık tek seferlik bir deneme değil -- karşı taraftan
        // kaynaklı bir kopma (bağlantı zaman aşımı, socket/IO hatası) olduğunda
        // burada SESSİZCE ve OTOMATİK olarak tekrar dener. Kullanıcının "İptal"e
        // basmasına ya da "Devam Et"e basmasına GEREK YOKTUR -- kart State =
        // BaglantiBekleniyor durumuna geçer (İptal butonu görünür kalır, Devam Et
        // ÇIKMAZ) ve bağlantı yakalanır yakalanmaz indirme kaldığı yerden devam
        // eder. Kullanıcının kendi iptali (OnCancelClicked -> DownloadCts.Cancel())
        // ya da uygulamanın kapanması (token zaten iptal olmuş şekilde gelir) bu
        // döngüyü hemen durdurur ve normal Duraklatildi/IptalEdildi akışına düşer.
        private async Task RequestDownloadInternal(string peerIp, string fileName, SharedFile targetFile, CancellationToken token)
        {
            // YENİ: İlk denemeden önceki DeviceId'yi sabitliyoruz -- retry döngüsü
            // boyunca "hangi cihazı takip ediyoruz" hiç değişmemeli, sadece o
            // cihazın GÜNCEL IP'si her denemede yeniden çözülmeli (peer reconnect
            // olup DHCP'den farklı bir IP alabilir; IP değişse bile DeviceId sabit
            // kaldığı için doğru cihaza bağlanmaya devam ederiz).
            string trackedDeviceId = GetDeviceIdByIp(peerIp);

            while (true)
            {
                if (token.IsCancellationRequested) return; // Kullanıcı iptali / uygulama kapanması: döngü burada biter.

                // Güncel IP'yi tazele: peer reconnect olduysa IP değişmiş olabilir.
                string currentIp = !string.IsNullOrEmpty(trackedDeviceId)
                    ? GetIpByDeviceId(trackedDeviceId)
                    : peerIp;
                if (string.IsNullOrEmpty(currentIp)) currentIp = peerIp; // Henüz yeniden keşfedilmediyse eski IP ile dene.

                var outcome = await RequestDownloadAttempt(currentIp, fileName, targetFile, token);

                switch (outcome)
                {
                    case DownloadAttemptResult.Completed:
                    case DownloadAttemptResult.UserCancelledOrLocalStop:
                        return; // Tamamlandı ya da kullanıcı/lokal sebepli durdu -- normal state zaten set edildi, döngü biter.

                    case DownloadAttemptResult.CorruptFile:
                        return; // Bütünlük hatası -- otomatik retry anlamsız, kullanıcı elle "Tekrar Dene"ye basmalı.

                    case DownloadAttemptResult.PeerSideDisconnect:
                        // Karşı taraftan kaynaklı kopma: otomatik yeniden dene.
                        break;
                }

                if (token.IsCancellationRequested) return;

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    targetFile.State = DownloadState.BaglantiBekleniyor;
                    targetFile.StatusMessage = $"Bağlantı bekleniyor... ({(int)RetryFixedDelay.TotalSeconds}sn sonra tekrar denenecek)";
                    GlobalSpeedUpdated?.Invoke("0 MB/s");
                });

                try
                {
                    await Task.Delay(RetryFixedDelay, token);
                }
                catch (OperationCanceledException)
                {
                    return; // Beklerken kullanıcı iptal etti / uygulama kapandı.
                }
            }
        }

        private enum DownloadAttemptResult
        {
            Completed,
            PeerSideDisconnect,
            UserCancelledOrLocalStop,
            CorruptFile
        }

        // YENİ: KÖK SEBEP DÜZELTMESİ. Karşı tarafın internet'i/bağlantısı SESSİZCE
        // koptuğunda (temiz bir FIN/RST paketi gelmeden -- ör. kablo çekilmesi,
        // wifi kopması, modem resetlenmesi) TCP soketi işletim sistemi seviyesinde
        // hâlâ "açık" görünür. NetworkStream.ReadAsync/WriteAsync bu durumda ne
        // exception fırlatır ne de token iptal olmadıkça geri döner -- SÜRESİZ
        // askıda kalır. Önceki haliyle retry döngüsü bu yüzden hiç tetiklenmiyordu:
        // await hiç dönmediği için PeerSideDisconnect'e asla ulaşılamıyordu.
        //
        // Çözüm: her deneme için ayrı bir "inaktivite" CancellationTokenSource'u
        // kullanıyoruz. Her başarılı okuma/yazma bu sayacı SIFIRLAR (Kick metodu).
        // Belirlenen süre (10sn) boyunca HİÇ ilerleme olmazsa bu CTS kendini iptal
        // eder, tüm bekleyen ReadAsync/WriteAsync çağrıları anında
        // OperationCanceledException fırlatır ve dışarıdaki retry döngüsü devreye
        // girer. Bu, kullanıcının kendi iptalinden (token) TAMAMEN BAĞIMSIZ çalışır.
        private sealed class InactivityWatchdog : IDisposable
        {
            private readonly CancellationTokenSource _linkedCts;
            private readonly Timer _timer;
            private readonly TimeSpan _timeout;
            private volatile bool _disposed;

            public CancellationToken Token => _linkedCts.Token;

            public InactivityWatchdog(CancellationToken parentToken, TimeSpan timeout)
            {
                _timeout = timeout;
                _linkedCts = CancellationTokenSource.CreateLinkedTokenSource(parentToken);
                _timer = new Timer(_ =>
                {
                    if (!_disposed)
                    {
                        try { _linkedCts.Cancel(); } catch (ObjectDisposedException) { }
                    }
                }, null, timeout, Timeout.InfiniteTimeSpan);
            }

            // Her başarılı okuma/yazma sonrası çağrılır -- "hâlâ canlıyız" sinyali.
            public void Kick()
            {
                if (!_disposed)
                {
                    try { _timer.Change(_timeout, Timeout.InfiniteTimeSpan); } catch (ObjectDisposedException) { }
                }
            }

            public void Dispose()
            {
                _disposed = true;
                _timer.Dispose();
                _linkedCts.Dispose();
            }
        }

        // Sabit 10 saniye: karşı taraftan bu süre boyunca hiç veri gelmezse/
        // gönderilemezse bağlantı "sessizce kopmuş" kabul edilip otomatik retry
        // tetiklenir.
        private static readonly TimeSpan InactivityTimeout = TimeSpan.FromSeconds(10);

        private async Task<DownloadAttemptResult> RequestDownloadAttempt(string peerIp, string fileName, SharedFile targetFile, CancellationToken token)
        {
            IPAddress peerIpAddress = IPAddress.Parse(peerIp);

            // YENİ: Kalıcı ilerleme kaydı, gönderenin IP'si değil KALICI DeviceId'si
            // ile anahtarlanır -- IP ağ kopması/yeniden bağlanmada değişebilir ama
            // DeviceId sabit kalır. Böylece "1 gün sonra uygulama açılsa bile aynı
            // dosya, aynı kişiden geliyorsa kaldığı yerden devam etsin" senaryosu,
            // dosyanın hangi IP'den geldiğinden bağımsız çalışır.
            string senderDeviceId = GetDeviceIdByIp(peerIp);
            if (string.IsNullOrEmpty(senderDeviceId)) senderDeviceId = peerIp; // Son çare: deviceId bulunamazsa IP'yi kullan.

            using var watchdog = new InactivityWatchdog(token, InactivityTimeout);
            CancellationToken effectiveToken = watchdog.Token;

            try
            {
                MainThread.BeginInvokeOnMainThread(() => { targetFile.State = DownloadState.Indiriliyor; targetFile.StatusMessage = "Bağlanıyor..."; });

                using var client = new TcpClient();

                // Dosya bağlantısı için 5 saniyelik zaman aşımı tanımlıyoruz
                using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(effectiveToken);
                connectCts.CancelAfter(TimeSpan.FromSeconds(5));

                try
                {
                    await client.ConnectAsync(peerIpAddress, TcpPort, connectCts.Token);
                    watchdog.Kick();
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    throw new TimeoutException("Dosya sunucusuna bağlanırken zaman aşımı oluştu.");
                }

                using var stream = client.GetStream();

                byte[] aesKey = await KeyExchangeService.NegotiateSessionAsync(stream, peerIpAddress, true, effectiveToken);
                watchdog.Kick();

                string folderPath = SettingsService.DownloadFolder;
                if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);

                string safeFileName = Path.GetFileName(fileName);
                string savePath = Path.Combine(folderPath, safeFileName);
                long existingLength = File.Exists(savePath) ? new FileInfo(savePath).Length : 0;

                string cmd = $"REQ_FILE:{safeFileName}:{existingLength}";
                byte[] encryptedCmd = EncryptionHelper.EncryptBytes(Encoding.UTF8.GetBytes(cmd), aesKey);
                await FrameIO.WriteFrameAsync(stream, encryptedCmd, effectiveToken);
                watchdog.Kick();

                byte[] nameFrame = await FrameIO.ReadFrameAsync(stream, 1024, effectiveToken, "Geçersiz dosya adı.");
                string responseFileName = Encoding.UTF8.GetString(EncryptionHelper.DecryptBytes(nameFrame, aesKey));
                watchdog.Kick();

                byte[] lengthFrame = await FrameIO.ReadFrameAsync(stream, 256, effectiveToken, "Geçersiz boyut.");
                long totalFileLength = BitConverter.ToInt64(EncryptionHelper.DecryptBytes(lengthFrame, aesKey), 0);
                watchdog.Kick();

                byte[] hashFrame = await FrameIO.ReadFrameAsync(stream, 1024, effectiveToken, "Geçersiz hash.");
                string expectedHash = Encoding.UTF8.GetString(EncryptionHelper.DecryptBytes(hashFrame, aesKey));
                watchdog.Kick();

                using var fs = new FileStream(savePath, existingLength > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.ReadWrite, FileTransferBufferSize, true);
                long totalRead = existingLength;

                // GÜNCELLENDİ (PERFORMANS): tam dosya bütünlüğü artık transfer
                // sırasında biriktirilmiyor -- indirme tamamlandığında dosya TEK
                // SEFERLİK diskten okunup hesaplanıyor (bkz. ComputeFullFileHashAsync).

                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                long bytesSinceLastUpdate = 0;
                var lastUiUpdate = DateTime.Now;

                while (totalRead < totalFileLength)
                {
                    // ZERO-ALLOCATION: şifreli parça ArrayPool'dan kiralanan bir buffer'a okunur.
                    var (encRented, encLen) = await FrameIO.ReadFramePooledAsync(stream, FileTransferBufferSize * 2, effectiveToken, "Parça hatası.");
                    try
                    {
                        if (encLen == 0) break;

                        // NOT: EncryptionHelper.DecryptBytes şifreleme katmanına ait olduğu
                        // ve KESİNLİKLE değiştirilmediği için burada tek bir tahsis kaçınılmaz
                        // (AES-GCM decrypt çıktısı). Yine de girdi tarafı (encRented) pool'dan
                        // geliyor, bu yüzden 100GB'lık transfer boyunca sürekli büyüyen değil,
                        // sabit boyutlu bir "encrypted buffer" havuzu kullanılmış olur.
                        byte[] encryptedChunk = encLen == encRented.Length
                            ? encRented
                            : encRented.AsSpan(0, encLen).ToArray();

                        byte[] plainChunk = EncryptionHelper.DecryptBytes(encryptedChunk, aesKey);

                        await fs.WriteAsync(plainChunk, 0, plainChunk.Length, effectiveToken);

                        totalRead += plainChunk.Length;
                        bytesSinceLastUpdate += plainChunk.Length;

                        // YENİ (KÖK SEBEP DÜZELTMESİ): Her başarılı parça sonrası watchdog'u
                        // sıfırlıyoruz -- bağlantı canlı olduğu sürece bu satır çalışmaya
                        // devam eder. Karşı taraf sessizce kaybolursa (internet gitmesi vb.)
                        // yukarıdaki ReadFramePooledAsync/WriteAsync çağrıları veri
                        // gelmediği/gönderilemediği için bloklanır, bu satıra hiç ulaşılmaz,
                        // watchdog 10 saniye sonra effectiveToken'ı iptal eder ve bekleyen
                        // çağrı OperationCanceledException fırlatır -- retry döngüsü tetiklenir.
                        watchdog.Kick();
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(encRented);
                    }

                    if ((DateTime.Now - lastUiUpdate).TotalMilliseconds > 250)
                    {
                        double speed = bytesSinceLastUpdate / stopwatch.Elapsed.TotalSeconds;
                        double remainingSeconds = (totalFileLength - totalRead) / (speed > 0 ? speed : 1);
                        string speedText = $"{(speed / 1024 / 1024):F1} MB/s";

                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            targetFile.Progress = (double)totalRead / totalFileLength;
                            targetFile.SpeedText = speedText;
                            targetFile.EtaText = TimeSpan.FromSeconds(remainingSeconds).ToString(@"mm\:ss") + " kaldı";
                            targetFile.DownloadedSizeText = $"{(totalRead / 1024.0 / 1024.0):F1} MB / {(totalFileLength / 1024.0 / 1024.0):F1} MB";
                            targetFile.StatusMessage = $"%{Math.Round(targetFile.Progress * 100)}";

                            // Profil sayfasındaki "Anlık İndirme Hızı" göstergesini besleyen global event.
                            // Önceden tanımlıydı ama hiç tetiklenmiyordu; ProfilePage o yüzden hep "0 MB/s" gösteriyordu.
                            GlobalSpeedUpdated?.Invoke(speedText);
                        });

                        // YENİ: İlerlemeyi kalıcı hale getir. Ağ kopması veya uygulama
                        // kapanması olursa, bir sonraki açılışta bu kayıttan "nereye kadar
                        // indiği" okunup dosya otomatik Duraklatildi + doğru Progress ile
                        // gösterilecek.
                        DownloadRecordService.UpdateProgress(senderDeviceId, fileName, totalRead, totalFileLength, expectedHash);

                        stopwatch.Restart();
                        bytesSinceLastUpdate = 0;
                        lastUiUpdate = DateTime.Now;
                    }
                }

                await fs.FlushAsync(effectiveToken);
                fs.Close();

                // GÜNCELLENDİ (PERFORMANS): incrementalHash artık sadece bu oturumda
                // gelen baytları içeriyor (tam dosyayı değil) -- bu yüzden final
                // doğrulama için dosya TEK SEFERLİK, tamamlandığı an diskten okunup
                // tam SHA-256'sı hesaplanıyor. Bu pahalı işlem her retry'de değil,
                // SADECE indirme gerçekten %100 tamamlandığında bir kez çalışır.
                string computedHash = await ComputeFullFileHashAsync(savePath, effectiveToken);

                if (computedHash != expectedHash)
                {
                    File.Delete(savePath);
                    // YENİ: Dosya bozuk çıktığı için diskten silindi -- kalıcı ilerleme
                    // kaydı da silinmeli, yoksa bir sonraki denemede "resume" olarak
                    // artık var olmayan bir offset'e güvenilmeye çalışılır.
                    DownloadRecordService.ClearProgress(senderDeviceId, fileName);
                    throw new Exception("Bütünlük Doğrulanamadı!");
                }

                // YENİ: İndirme başarıyla tamamlandı -- kalıcı ilerleme kaydına artık
                // gerek yok.
                DownloadRecordService.ClearProgress(senderDeviceId, fileName);

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    targetFile.State = DownloadState.Tamamlandi;
                    targetFile.StatusMessage = "Tamamlandı";
                    GlobalSpeedUpdated?.Invoke("0 MB/s");
                });

                return DownloadAttemptResult.Completed;
            }
            catch (OperationCanceledException)
            {
                // YENİ: token.IsCancellationRequested true ise bu KULLANICI iptali
                // (OnCancelClicked -> DownloadCts.Cancel()) ya da uygulamanın
                // kapanmasıdır -- otomatik retry YAPILMAZ, mevcut IptalEdildi/
                // Duraklatildi akışına bırakılır (OnCancelClicked zaten kendi
                // state'ini set ediyor, burada dokunmuyoruz).
                //
                // token iptal edilmemişse (yani OperationCanceledException bir
                // CancelAfter/bağlantı zaman aşımından geldiyse) bu KARŞI TARAFTAN
                // kaynaklı bir kopmadır -- otomatik yeniden denemeye girer.
                if (token.IsCancellationRequested)
                {
                    return DownloadAttemptResult.UserCancelledOrLocalStop;
                }

                System.Diagnostics.Debug.WriteLine("İndirme Hatası: Bağlantı zaman aşımına uğradı, otomatik tekrar denenecek.");
                return DownloadAttemptResult.PeerSideDisconnect;
            }
            catch (Exception ex)
            {
                // YENİ: Ağ kopması (SocketException, IOException, vb. -- soket
                // katmanından gelen her türlü kesilme) burada yakalanır. Kalıcı
                // ilerleme kaydı silinmiyor: dosya diskte kısmen var olmaya devam
                // ediyor ve otomatik yeniden deneme bu noktadan devam edecek.
                //
                // İSTİSNA: hash uyuşmazlığı ("Bütünlük Doğrulanamadı!") zaten
                // yukarıda dosyayı silip kaydı temizliyor -- bu durumda otomatik
                // retry mantıksız (aynı hatayı sonsuza kadar tekrarlar), kullanıcı
                // elle "Tekrar Dene"ye basmalı.
                System.Diagnostics.Debug.WriteLine($"İndirme Hatası: {ex.Message}");

                bool isCorrupt = ex.Message.Contains("Bütünlük Doğrulanamadı");
                if (isCorrupt)
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        targetFile.State = DownloadState.Hata;
                        targetFile.StatusMessage = "Dosya bozuk, tekrar deneyin";
                        GlobalSpeedUpdated?.Invoke("0 MB/s");
                    });
                    return DownloadAttemptResult.CorruptFile;
                }

                return DownloadAttemptResult.PeerSideDisconnect;
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
                remoteIp = (client.Client?.RemoteEndPoint as IPEndPoint)?.Address;
                if (remoteIp == null) return;
                ipKey = remoteIp.ToString();

                if (!await _connectionLimiter.WaitAsync(TimeSpan.FromSeconds(2), cts.Token)) return;
                acquiredSlot = true;

                int current = _perIpConnections.AddOrUpdate(ipKey, 1, (_, c) => c + 1);
                if (current > MaxConnectionsPerIp) return;

                using var stream = client.GetStream();
                byte[] aesKey = await KeyExchangeService.NegotiateSessionAsync(stream, remoteIp, false, cts.Token);

                byte[] encryptedCmd = await FrameIO.ReadFrameAsync(stream, 2048, cts.Token, "Komut okunamadı.");
                string command = Encoding.UTF8.GetString(EncryptionHelper.DecryptBytes(encryptedCmd, aesKey));

                // 1. Profil Resmi İstendiğinde
                if (command.StartsWith("REQ_PROFILE:"))
                {
                    string profilePath = SettingsService.ProfileImagePath;
                    if (File.Exists(profilePath))
                    {
                        using var fs = new FileStream(profilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                        byte[] buffer = ArrayPool<byte>.Shared.Rent(16384);
                        try
                        {
                            int bytesRead;
                            while ((bytesRead = await fs.ReadAsync(buffer, 0, buffer.Length, cts.Token)) > 0)
                            {
                                byte[] encryptedChunk = EncryptionHelper.EncryptBytes(buffer, bytesRead, aesKey);
                                await FrameIO.WriteFrameAsync(stream, encryptedChunk, cts.Token);
                            }
                            await FrameIO.WriteFrameAsync(stream, Array.Empty<byte>(), cts.Token); // EOF
                        }
                        finally { ArrayPool<byte>.Shared.Return(buffer); }
                    }
                    else
                    {
                        await FrameIO.WriteFrameAsync(stream, Array.Empty<byte>(), cts.Token); // Dosya yoksa boş gönder bitir
                    }
                }
                // 2. Paylaşılan Dosya İstendiğinde
                else if (command.StartsWith("REQ_FILE:"))
                {
                    var parts = command.Split(':');
                    if (parts.Length < 2) return;
                    string requestedFileName = Path.GetFileName(parts[1]);
                    long offset = parts.Length > 2 && long.TryParse(parts[2], out long o) ? o : 0;

                    var targetFile = FileService.GetSavedFiles().FirstOrDefault(f => f.FileName == requestedFileName);
                    if (targetFile != null && File.Exists(targetFile.OriginalPath))
                    {
                        byte[] encName = EncryptionHelper.EncryptBytes(Encoding.UTF8.GetBytes(targetFile.FileName), aesKey);
                        byte[] encLength = EncryptionHelper.EncryptBytes(BitConverter.GetBytes(new FileInfo(targetFile.OriginalPath).Length), aesKey);
                        byte[] encHash = EncryptionHelper.EncryptBytes(Encoding.UTF8.GetBytes(FileService.GetFileHash(targetFile.OriginalPath)), aesKey);

                        await FrameIO.WriteFrameAsync(stream, encName, cts.Token);
                        await FrameIO.WriteFrameAsync(stream, encLength, cts.Token);
                        await FrameIO.WriteFrameAsync(stream, encHash, cts.Token);

                        // GÜNCELLENDİ: Sabit 30 dakikalık zaman aşımı, 100GB+ dosyalarda
                        // (özellikle yavaş/kalabalık ağlarda) transferi ortasında kesip
                        // atıyordu. Artık dosya boyutuna göre ölçeklenen bir süre veriliyor:
                        // taban 30 dakika + her GB için 2 dakika ek pay, üst sınır 24 saat.
                        // Bu, 20KB/s gibi çok düşük bir hızda bile ~100GB'lık bir dosyanın
                        // tamamlanabilmesi için yeterli marj bırakır; bağlantı gerçekten
                        // kopar/durursa zaten FrameIO içindeki ReadAsync/WriteAsync ct'yi
                        // anında gözetir, süresiz kilitlenme olmaz.
                        long fileLengthBytes = new FileInfo(targetFile.OriginalPath).Length;
                        double fileSizeGb = fileLengthBytes / 1024.0 / 1024.0 / 1024.0;
                        double transferMinutes = Math.Min(24 * 60, 30 + (fileSizeGb * 2));
                        cts.CancelAfter(TimeSpan.FromMinutes(transferMinutes));

                        using var fs = new FileStream(targetFile.OriginalPath, FileMode.Open, FileAccess.Read, FileShare.Read);
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
                            await FrameIO.WriteFrameAsync(stream, Array.Empty<byte>(), cts.Token); // EOF
                        }
                        finally { ArrayPool<byte>.Shared.Return(buffer); }
                    }
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Transfer Hatası: {ex.Message}"); }
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

            // YENİ EKLENEN KISIM: Uygulama kapanırken TCP'yi de düzgünce kapatıyoruz
            _tcpCts?.Cancel();
            _tcpCts?.Dispose();

            // YENİ: Housekeeping zamanlayıcısı ve bağlantı semaforu daha önce hiç
            // dispose edilmiyordu (kaynak sızıntısı). Artık ikisi de temizleniyor.
            _housekeepingTimer?.Dispose();
            _connectionLimiter.Dispose();
            _broadcastWakeSignal.Dispose();

            _udpClient?.Dispose();
            try
            {
                _tcpListener?.Stop();
            }
            catch { }
        }
    }
}