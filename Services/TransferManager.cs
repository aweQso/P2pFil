using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using P2PFil.Models;

namespace P2PFil.Services
{
    // YENİ: Aynı anda çok sayıda kullanıcı (50+) indirme başlattığında sistemin
    // kilitlenmesini/RAM'in şişmesini önlemek için merkezi kuyruk yöneticisi.
    //
    // Tasarım:
    // - Aktif indirme sayısı bir SemaphoreSlim ile sınırlanır (varsayılan 3).
    // - Limit dolduğunda yeni istekler FIFO bir kuyruğa (ConcurrentQueue) alınır
    //   ve slot boşaldığında otomatik olarak tetiklenir.
    // - Var olan RequestDownload / soket / şifreleme mantığına DOKUNULMAZ.
    //   Bu sınıf sadece "ne zaman çalışsın" sorusunu yönetir; "nasıl indirilsin"
    //   sorusunu NetworkService.RequestDownload'a bırakır (delegate ile enjekte edilir).
    // - Aynı dosya için (SenderId+FileName) birden fazla kuyruğa girmeyi engeller.
    // - Bir indirme iptal edilirse (henüz başlamadan, kuyruktayken) kuyruktan
    //   temizce çıkarılır; state güncellemesi UI thread üzerinde yapılır.
    public sealed class TransferManager
    {
        private static readonly Lazy<TransferManager> _instance = new(() => new TransferManager());
        public static TransferManager Instance => _instance.Value;

        // Varsayılan: aynı anda en fazla 3 aktif transfer. İhtiyaca göre
        // ayarlanabilir (örn. ProfilePage'deki "Performans Modu" anahtarına
        // bağlanabilir - bkz. SetMaxConcurrency).
        private SemaphoreSlim _slotSemaphore = new(3, 3);
        private int _maxConcurrency = 3;
        private readonly object _resizeLock = new();

        private readonly ConcurrentQueue<TransferRequest> _queue = new();
        // Kuyrukta veya aktif olan transferleri anahtarına göre izler (dup-request engeli + iptal desteği).
        private readonly ConcurrentDictionary<string, TransferRequest> _known = new();

        private TransferManager() { }

        public static string BuildKey(string senderId, string fileName) => $"{senderId}::{fileName}";

        public int MaxConcurrency
        {
            get => _maxConcurrency;
            private set => _maxConcurrency = value;
        }

        public int ActiveCount => _maxConcurrency - _slotSemaphore.CurrentCount;
        public int QueuedCount => _queue.Count;

        // Aktif transfer limitini çalışma zamanında değiştirir (örn. kullanıcı
        // "Performans Modu"nu açtığında 3 -> 1 gibi). Mevcut aktif transferler
        // etkilenmez; yeni slotlar bir sonraki tetiklemede uygulanır.
        public void SetMaxConcurrency(int newLimit)
        {
            if (newLimit < 1) newLimit = 1;

            lock (_resizeLock)
            {
                if (newLimit == _maxConcurrency) return;

                // DÜZELTME: SemaphoreSlim'in maksimum sayısı (maxCount) oluşturulduktan
                // sonra DEĞİŞTİRİLEMEZ. Önceki kod, limit önce düşürülüp (örn. 3 -> 1)
                // sonra tekrar yükseltildiğinde (1 -> 3) Release(delta) çağırıyordu;
                // eğer o ana kadar hiç slot tüketilmemişse (CurrentCount hâlâ eski
                // maxCount olan 3'teyse) bu çağrı 3 + 2 = 5 > maxCount(3) olduğu için
                // SemaphoreFullException fırlatıyordu (uygulama açılışında Performans
                // Modu kapatılırsa anında çöküyordu).
                //
                // Çözüm: sabit maxCount'u büyütmek yerine, mevcut kullanılabilir slot
                // sayısını (CurrentCount) ve şu an aktif olan transfer sayısını koruyarak
                // YENİ bir semafor oluşturuyoruz. Eski semaforun referansını tutan
                // aktif WaitAsync/Release çağrıları etkilenmez, sadece eski nesne GC
                // ile temizlenir.
                int active = _maxConcurrency - _slotSemaphore.CurrentCount; // şu an dolu olan slot sayısı
                if (active < 0) active = 0;

                int newAvailable = newLimit - active;
                if (newAvailable < 0) newAvailable = 0;

                var oldSemaphore = _slotSemaphore;
                _slotSemaphore = new SemaphoreSlim(newAvailable, newLimit);
                _maxConcurrency = newLimit;

                // Kuyrukta bekleyen varsa ve yeni limit daha fazla eşzamanlılığa izin
                // veriyorsa hemen tetikle.
                _ = TryDrainQueueAsync();
            }
        }

        // Bir indirme isteğini kuyruğa alır. Slot uygunsa hemen, değilse
        // sıradaki slot boşaldığında worker otomatik çalışır.
        // 'runner' -> asıl indirme işini yapan delegate (NetworkService.RequestDownload).
        public void Enqueue(SharedFile targetFile, string peerIp, string fileName,
            Func<string, string, SharedFile, CancellationToken, Task> runner)
        {
            string key = BuildKey(targetFile.SenderId, fileName);

            // DÜZELTME: Eskiden burada "_known.ContainsKey(key) ise sessizce return"
            // deniyordu. Sorun: transfer bir network kopması/exception sırasında
            // RunRequestAsync'in finally'si her zaman ANINDA çalışmayabilir (örn.
            // NetworkStream okuma/yazma çağrıları platforma göre CancellationToken'ı
            // gecikmeli gözetebilir), ya da bir önceki 'Duraklatildi'/'Hata' durumuna
            // düşen transfer her nasılsa _known içinde "aktif" gibi kalmışsa: kullanıcı
            // "Devam Et"e bastığında bu metot hiçbir şey yapmadan çıkıyordu -- ne hata
            // ne tekrar deneme, buton "kilitli" gibi görünüyordu.
            //
            // Yeni davranış: _known'da bir kayıt varsa ama dosya GERÇEKTEN indiriliyor
            // değilse (State != Indiriliyor), bu stale bir kayıttır -- temizleyip
            // isteği normal şekilde kuyruğa alıyoruz. Sadece gerçekten aktif bir
            // indirme varken (State == Indiriliyor) yeni isteği yok sayıyoruz.
            if (_known.ContainsKey(key))
            {
                // BaglantiBekleniyor da "gerçekten aktif" sayılır -- otomatik
                // yeniden bağlanma döngüsü RequestDownloadInternal içinde zaten
                // sürüyor, buraya ikinci bir istek girmesine gerek yok (zaten UI
                // tarafında CanStartOrResume bu durumda false olduğu için buton
                // görünmez, ama TransferManager kendi başına da bunu korumalı).
                if (targetFile.State is DownloadState.Indiriliyor or DownloadState.BaglantiBekleniyor)
                    return; // Gerçekten aktif; tekrar tetiklemeye gerek yok.

                // Stale kayıt: eski isteği kuyruktan/known'dan düş.
                _known.TryRemove(key, out _);
            }

            var request = new TransferRequest(key, targetFile, peerIp, fileName, runner);
            _known[key] = request;

            targetFile.DownloadCts = new CancellationTokenSource();

            MainThread_BeginInvoke(() =>
            {
                targetFile.State = DownloadState.Bekliyor;
                targetFile.StatusMessage = $"Kuyrukta ({QueuedCount + 1}. sırada)";
            });

            _queue.Enqueue(request);
            _ = TryDrainQueueAsync();
        }

        // Kuyruktaki veya henüz başlamamış bir isteği iptal eder / bilinen
        // listeden düşürür. Aktif çalışan bir transfer zaten kendi CancellationToken'ı
        // ile durdurulur (ChatPage/FilesPage tarafındaki mevcut OnCancelClicked akışı).
        public void Cancel(SharedFile targetFile, string fileName)
        {
            string key = BuildKey(targetFile.SenderId, fileName);
            _known.TryRemove(key, out _);
            // ConcurrentQueue'dan doğrudan silme desteklenmez; worker bu isteği
            // sıradan çektiğinde _known içinde bulamayınca sessizce atlar.
        }

        private async Task TryDrainQueueAsync()
        {
            while (_queue.TryPeek(out var next))
            {
                bool entered = await _slotSemaphore.WaitAsync(0);
                if (!entered) return; // Şu an boş slot yok; slot boşaldığında ReleaseAndDrain tekrar dener.

                if (!_queue.TryDequeue(out var request))
                {
                    _slotSemaphore.Release();
                    continue;
                }

                // İptal edilmiş / artık geçerli olmayan istek: slotu boşa harcamadan geç.
                // Referans karşılaştırması ÖNEMLİ: aynı key için kuyrukta hem eski
                // (stale) hem yeni bir TransferRequest sırayla durabilir (Enqueue
                // stale kaydı silip yenisini eklediğinde). _known[key] artık YENİ
                // request'i gösterir; burada çektiğimiz 'request' o değilse bu
                // kuyruktaki eski/artık geçersiz kopyadır, çalıştırmadan atlarız.
                if (!_known.TryGetValue(request.Key, out var current) || !ReferenceEquals(current, request))
                {
                    _slotSemaphore.Release();

                    // YENİ: _known'da bu key için HİÇBİR kayıt yoksa (current == null),
                    // bu gerçek bir Cancel() sonucu -- state'in "Bekliyor"da asılı
                    // kalmadığından emin oluyoruz. Ama _known'da BAŞKA (daha taze) bir
                    // request varsa, o zaten kendi durumunu yönetiyor -- burada state'e
                    // dokunursak yeni isteğin durumunu ezebiliriz, o yüzden dokunmuyoruz.
                    if (current == null)
                    {
                        var staleFile = request.TargetFile;
                        if (staleFile.State == DownloadState.Bekliyor)
                        {
                            MainThread_BeginInvoke(() =>
                            {
                                staleFile.State = DownloadState.IptalEdildi;
                                staleFile.StatusMessage = "İptal edildi";
                            });
                        }
                    }

                    continue;
                }

                _ = RunRequestAsync(request);
            }
        }

        private async Task RunRequestAsync(TransferRequest request)
        {
            try
            {
                var token = request.TargetFile.DownloadCts?.Token ?? CancellationToken.None;

                if (token.IsCancellationRequested)
                    return;

                await request.Runner(request.PeerIp, request.FileName, request.TargetFile, token);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TransferManager] Transfer hatası ({request.FileName}): {ex.Message}");
            }
            finally
            {
                _known.TryRemove(request.Key, out _);
                _slotSemaphore.Release();
                _ = TryDrainQueueAsync(); // Bu slot boşaldı, kuyrukta bekleyen varsa tetikle.
                UpdateQueuePositions();
            }
        }

        // Kuyrukta bekleyenlerin "X. sırada" mesajını günceller (best-effort; snapshot alır).
        private void UpdateQueuePositions()
        {
            int position = 1;
            foreach (var req in _queue)
            {
                if (!_known.ContainsKey(req.Key)) continue;
                var file = req.TargetFile;
                int capturedPosition = position;
                MainThread_BeginInvoke(() =>
                {
                    if (file.State == DownloadState.Bekliyor)
                        file.StatusMessage = $"Kuyrukta ({capturedPosition}. sırada)";
                });
                position++;
            }
        }

        // MAUI'ye doğrudan bağımlı olmamak için ince bir sarmalayıcı (test edilebilirlik
        // ve olası ileride farklı bir UI dispatcher'a geçiş için). Gerçek projede
        // doğrudan Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread
        // kullanılabilir; burada isim çakışmasını önlemek için yerel yardımcı.
        private static void MainThread_BeginInvoke(Action action)
        {
            Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(action);
        }

        private sealed class TransferRequest
        {
            public string Key { get; }
            public SharedFile TargetFile { get; }
            public string PeerIp { get; }
            public string FileName { get; }
            public Func<string, string, SharedFile, CancellationToken, Task> Runner { get; }

            public TransferRequest(string key, SharedFile targetFile, string peerIp, string fileName,
                Func<string, string, SharedFile, CancellationToken, Task> runner)
            {
                Key = key;
                TargetFile = targetFile;
                PeerIp = peerIp;
                FileName = fileName;
                Runner = runner;
            }
        }
    }
}