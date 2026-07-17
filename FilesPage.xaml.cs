using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using P2PFil.Models;
using P2PFil.Services;

namespace P2PFil;

public partial class FilesPage : ContentPage
{
    // Ham (filtrelenmemiş) veriler; ekrandaki koleksiyonlar bunların süzülmüş halidir.
    private readonly List<SharedFile> _allMyFiles = new();
    private readonly List<SharedFile> _allNetworkFiles = new();

    public ObservableCollection<SharedFile> MyFiles { get; } = new();
    public ObservableCollection<SharedFile> NetworkFiles { get; } = new();

    private bool _isScanning = false;
    private string _activeTypeFilter = "Tümü";

    private static readonly string[] TypeFilters = { "Tümü", "Resim", "Video", "Belge", "Diğer" };

    public FilesPage()
    {
        InitializeComponent();

        MyFilesList.ItemsSource = MyFiles;
        NetworkFilesCollection.ItemsSource = NetworkFiles;

        BuildFilterChips();

        // Liste değişikliklerini izleyen merkezi yapı
        NetworkFiles.CollectionChanged += (s, e) =>
        {
            // İhtiyaca göre burada otomatik işlem yapabilirsin (örneğin loglama)
        };

        // YENİ: Sayfa aktif sekme olmasa bile, bir dosya sahibinin profil resmi
        // güncellendiğinde ağ dosyaları listesindeki avatarı anında tazele.
        ProfileMessenger.PeerProfileChanged += OnOwnerProfileChanged;
    }

    private void OnOwnerProfileChanged(string deviceId)
    {
        string imagePath = Path.Combine(FileSystem.CacheDirectory, $"{deviceId}_profile.png");
        if (!File.Exists(imagePath)) return;

        // DÜZELTME: f.SenderId artık zaten DeviceId (IP değil), bu yüzden
        // doğrudan karşılaştırılıyor -- GetDeviceIdByIp(f.SenderId) çağrısı
        // burada f.SenderId'ye bir IP gibi davranıp yanlış sonuç dönerdi.
        var affected = _allNetworkFiles.Where(f => f.SenderId == deviceId).ToList();
        if (affected.Count == 0) return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            foreach (var f in affected)
            {
                f.OwnerProfileImagePath = imagePath;
            }
        });
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        App.NetworkService.FilesReceived += NetworkService_FilesReceived;
        LoadMyFiles();

        int targetIndex = 1;
        double direction = (targetIndex > App.CurrentTabIndex) ? 1 : -1;

        if (AnimatedContent != null)
        {
            AnimatedContent.TranslationX = direction * this.Width;
            AnimatedContent.Opacity = 0;
        }

        await Task.Delay(50);

        if (AnimatedContent != null)
        {
            _ = AnimatedContent.FadeTo(1, 750, Easing.CubicOut);
            await AnimatedContent.TranslateTo(0, 0, 750, Easing.CubicOut);
        }

        MyCustomTabBar?.SetActiveIndex(targetIndex);
        App.CurrentTabIndex = targetIndex;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        App.NetworkService.FilesReceived -= NetworkService_FilesReceived;
    }

    private void NetworkService_FilesReceived(string senderIp, List<SharedFile> files)
    {
        // İşlemleri arka planda yapıyoruz
        Task.Run(() =>
        {
            var newFilesToAdd = new List<SharedFile>();

            // Gelen dosyaların isimlerini O(1) hızında aramak için HashSet'e alıyoruz.
            var incomingFileNames = new HashSet<string>(files.Select(f => f.FileName));

            // Gelen dosyaları işleme
            foreach (var incomingFile in files)
            {
                string deviceId = App.NetworkService.GetDeviceIdByIp(senderIp);

                // DÜZELTME: SenderId artık IP DEĞİL, KALICI DeviceId. Önceden
                // burada "incomingFile.SenderId = senderIp" yapılıyordu; karşı
                // taraf ağ kopması sonrası yeniden bağlanıp farklı bir IP aldığında
                // (DHCP, wifi<->hotspot geçişi, vb.) aynı fiziksel dosya için
                // aşağıdaki "existingEntry" araması (FileName + SenderId) artık
                // eşleşmiyordu ve ikinci, birbirinin AYNISI görünen bir kart
                // oluşuyordu. DeviceId sabit kaldığı için bu artık olmuyor.
                // Henüz deviceId çözülemediyse (ilk paket, discovery daha yeni
                // tamamlandı) geçici olarak IP'ye düşüyoruz; bir sonraki pakette
                // deviceId çözülünce zaten güncellenecek.
                incomingFile.SenderId = !string.IsNullOrEmpty(deviceId) ? deviceId : senderIp;

                // İndirme sırasında gerçek bağlantı için GÜNCEL IP gerekiyor --
                // bunu ayrı bir alanda tutuyoruz (NetworkService zaten DeviceId'den
                // IP'ye anlık çözüm yapabiliyor, ama indirme başlarken hangi IP'nin
                // kullanılacağını burada da elde tutmak istiyoruz).
                incomingFile.CurrentIp = senderIp;

                string cachedProfilePic = Path.Combine(FileSystem.CacheDirectory, $"{deviceId}_profile.png");

                if (File.Exists(cachedProfilePic))
                    incomingFile.OwnerProfileImagePath = cachedProfilePic;

                // YENİ: Bu SPESİFİK dosya (aynı FileName + aynı gönderen DeviceId)
                // için diskte yarım kalmış bir indirme kaydı var mı diye bakıyoruz.
                // Rastgele/toplu bir kontrol DEĞİL -- her incomingFile kendi
                // FileName+deviceId'siyle sorgulanıyor, bu yüzden başka bir dosyanın
                // ilerlemesi yanlışlıkla buraya karışamaz. Bu kontrol hem ağ kopması
                // sonrası yeniden keşifte, hem de uygulama tamamen kapatılıp günler
                // sonra açıldığında aynı şekilde çalışır -- kayıt sadece diskte
                // olduğu için kalıcıdır.
                var progressRecord = DownloadRecordService.FindRecord(deviceId, incomingFile.FileName);
                if (progressRecord != null && progressRecord.TotalBytes > 0)
                {
                    string expectedSavePath = Path.Combine(SettingsService.DownloadFolder, incomingFile.FileName);
                    long actualOnDisk = File.Exists(expectedSavePath) ? new FileInfo(expectedSavePath).Length : 0;

                    // Diskteki gerçek dosya boyutu ile kayıttaki bilgi tutarlıysa
                    // (kullanıcı .indirilen dosyayı elle silmemişse) resume göster.
                    if (actualOnDisk > 0 && actualOnDisk <= progressRecord.TotalBytes)
                    {
                        incomingFile.State = DownloadState.Duraklatildi;
                        incomingFile.Progress = (double)actualOnDisk / progressRecord.TotalBytes;
                        incomingFile.DownloadedSizeText = $"{(actualOnDisk / 1024.0 / 1024.0):F1} MB / {(progressRecord.TotalBytes / 1024.0 / 1024.0):F1} MB";
                        incomingFile.StatusMessage = "Bağlantı koptu";
                    }
                    else
                    {
                        // Diskte dosya yok/kayıtla uyuşmuyor -- artık geçersiz bir
                        // kalıntı kayıt, temizleyip baştan indirilebilir bırakıyoruz.
                        DownloadRecordService.ClearProgress(deviceId, incomingFile.FileName);
                    }
                }

                var existingEntry = _allNetworkFiles.FirstOrDefault(f => f.FileName == incomingFile.FileName && f.SenderId == incomingFile.SenderId);
                if (existingEntry == null)
                {
                    newFilesToAdd.Add(incomingFile);
                }
                else
                {
                    // DÜZELTME: IP her paket geldiğinde güncelleniyor -- karşı taraf
                    // reconnect olup yeni bir IP aldıysa, mevcut kartın CurrentIp'i
                    // buradan tazelenir. Böylece "Devam Et"e basıldığında (ya da
                    // otomatik retry döngüsü) her zaman GÜNCEL IP kullanılır.
                    existingEntry.CurrentIp = incomingFile.CurrentIp;

                    // Aktif indiriliyorsa (Indiriliyor) ya da otomatik retry
                    // döngüsündeyse (BaglantiBekleniyor) state'e DOKUNMUYORUZ --
                    // NetworkService zaten kendi retry mantığını yönetiyor, burada
                    // ezersek yarım kalan bir transferi "Bekliyor"a geri düşürebiliriz.
                    if (!existingEntry.IsDownloading && !existingEntry.IsAutoRetrying)
                    {
                        // YENİ: Dosya zaten listede duruyor (sayfadan çıkılıp geri
                        // girilmiş olabilir) ama aktif olarak indirilmiyorsa, yukarıda
                        // hesapladığımız resume bilgisini var olan nesneye de yansıtıyoruz
                        // -- aksi halde eski nesnenin state'i "Bekliyor"da takılı kalırdı.
                        existingEntry.State = incomingFile.State;
                        existingEntry.Progress = incomingFile.Progress;
                        existingEntry.DownloadedSizeText = incomingFile.DownloadedSizeText;
                        existingEntry.StatusMessage = incomingFile.State == DownloadState.Duraklatildi
                            ? incomingFile.StatusMessage
                            : existingEntry.StatusMessage;
                    }
                }
            }

            // Ölü (bağlantısı kopan) dosyaları arka planda tespit etme.
            // DÜZELTME: deviceId üzerinden karşılaştırıyoruz (senderIp değil) --
            // aksi halde karşı taraf yeni bir IP ile keşfedildiğinde eski IP'ye
            // ait kart hâlâ "canlı" görünen bir DeviceId'nin dosyasını yanlışlıkla
            // "ölü" sayıp silebiliyordu. Ayrıca BaglantiBekleniyor durumundaki
            // (otomatik yeniden bağlanan) dosyalar da IsDownloading gibi korunmalı --
            // discovery paketi gelmedi diye retry döngüsü ortasındaki kart silinmemeli.
            string incomingDeviceId = App.NetworkService.GetDeviceIdByIp(senderIp);
            var deadFilesToRemove = _allNetworkFiles
                .Where(f => f.SenderId == (!string.IsNullOrEmpty(incomingDeviceId) ? incomingDeviceId : senderIp)
                            && !incomingFileNames.Contains(f.FileName)
                            && !f.IsDownloading
                            && !f.IsAutoRetrying)
                .ToList();

            // UI Güncellemeleri
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                if (_isScanning)
                {
                    _isScanning = false;
                    await ScanProgressBar.ProgressTo(1.0, 300, Easing.CubicOut);
                    ScanProgressBar.IsVisible = false;
                    ScanProgressBar.Progress = 0;
                }

                foreach (var file in newFilesToAdd)
                {
                    _allNetworkFiles.Add(file);
                }

                foreach (var deadFile in deadFilesToRemove)
                {
                    _allNetworkFiles.Remove(deadFile);
                }

                ApplyNetworkFilter();
            });
        });
    }

    private void LoadMyFiles()
    {
        _allMyFiles.Clear();
        var files = FileService.GetSavedFiles();
        foreach (var f in files)
        {
            f.OwnerProfileImagePath = SettingsService.ProfileImagePath;
            _allMyFiles.Add(f);
        }
        ApplyMyFilesFilter();
    }

    private async void OnSelectFileClicked(object sender, EventArgs e)
    {
        try
        {
            var result = await FilePicker.Default.PickAsync();
            if (result != null)
            {
                FileService.SaveFile(result.FullPath, result.FileName);
                LoadMyFiles();
                await DisplayAlert("Eklendi", $"{result.FileName} paylaşıma açıldı.", "Tamam");
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Hata", "Dosya paylaşıma açılamadı: " + ex.Message, "Tamam");
        }
    }

    private void OnDeleteFileClicked(object sender, EventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is SharedFile file)
        {
            DeleteMyFile(file);
        }
    }

    private void OnDeleteFileSwiped(object sender, EventArgs e)
    {
        if (sender is SwipeItem item && item.CommandParameter is SharedFile file)
        {
            DeleteMyFile(file);
        }
    }

    private void DeleteMyFile(SharedFile file)
    {
        FileService.DeleteFile(file.OriginalPath);
        LoadMyFiles();
    }

    private async void OnStartDownloadClicked(object sender, EventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is SharedFile file)
        {
            // Zaten indiriliyorsa (örn. hızlı çift tıklama) tekrar tetikleme.
            if (file.IsDownloading) return;

            // NOT: DownloadCts'i burada elle sıfırlamıyoruz -- TransferManager.Enqueue
            // her çağrıldığında zaten "targetFile.DownloadCts = new CancellationTokenSource()"
            // ile taze bir token atıyor (bkz. TransferManager.cs). Asıl kilitlenme sebebi
            // orada Enqueue'nin stale kayıtları sessizce yok sayması idi; onu düzelttik.

            file.StatusMessage = file.State switch
            {
                DownloadState.Duraklatildi => "Devam ediliyor...",
                DownloadState.Hata => "Tekrar deneniyor...",
                DownloadState.IptalEdildi => "Yeniden başlatılıyor...",
                _ => "Bağlanıyor..."
            };

            // DÜZELTME: file.SenderId artık IP değil KALICI DeviceId -- gerçek
            // bağlantı için file.CurrentIp kullanılıyor (en son alınan güncel IP).
            await App.NetworkService.RequestDownload(file.CurrentIp, file.FileName, file);
        }
    }

    private async void OnCancelClicked(object sender, EventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is SharedFile file)
        {
            try
            {
                file.DownloadCts?.Cancel();
                // YENİ: Kuyrukta bekleyen bir istekse TransferManager'a da haber ver ki
                // diğer kuyruktaki dosyaların "X. sırada" bilgisi hemen güncellensin
                // (aksi halde bir sonraki slot boşalana kadar eski sıra numarası kalır).
                TransferManager.Instance.Cancel(file, file.FileName);
                file.State = DownloadState.IptalEdildi;
                file.Progress = 0;

                await Task.Delay(300);

                string savePath = Path.Combine(SettingsService.DownloadFolder, file.FileName);
                if (File.Exists(savePath)) File.Delete(savePath);

                // YENİ: Kullanıcı bilerek iptal edip dosyayı diskten sildi -- kalıcı
                // resume kaydı da (varsa) temizlenmeli, yoksa bir sonraki keşifte
                // artık var olmayan bir dosya için "Bağlantı koptu, devam et" yanlış
                // bir şekilde gösterilebilir.
                // DÜZELTME: file.SenderId artık zaten DeviceId (bkz. NetworkService_FilesReceived) --
                // eskiden burada f.SenderId'ye (bir IP olduğu varsayılarak) tekrar
                // GetDeviceIdByIp uygulanıyordu, bu da yanlış (ya da boş) bir
                // deviceId üretip ClearProgress'in yanlış kaydı temizlemesine/
                // hiç temizlememesine yol açabiliyordu.
                DownloadRecordService.ClearProgress(file.SenderId, file.FileName);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"İptal sırasında hata: {ex.Message}");
            }
        }
    }

    private async void OnRefreshClicked(object sender, EventArgs e)
    {
        // DÜZELTME: Burada iki hata birlikte "İndir/Devam Et" butonunun tepki
        // vermez hale gelmesine yol açıyordu:
        //
        // 1) App.NetworkService.StartDiscovery() her refresh'te TEKRAR çağrılıyordu.
        //    StartDiscovery zaten uygulama açılışında bir kez başlatılıp arka planda
        //    sürekli çalışan UDP dinleme/broadcast task'ları kuruyor (bkz.
        //    NetworkService.StartDiscovery). Tekrar çağırmak _serviceCts'i iptal edip
        //    yeni bir set task başlatıyor; ama içerideki while
        //    (!token.IsCancellationRequested) döngüleri anında durmayabildiği için
        //    kısa süreliğine BİRDEN FAZLA UDP dinleme/broadcast task'ı aynı anda
        //    çalışabiliyor, bu da FilesReceived event'inin aynı peer için art arda
        //    birden fazla kez (çakışarak) tetiklenmesine yol açıyordu. Artık burada
        //    çağrılmıyor -- discovery zaten sürekli çalışıyor, refresh'in tek işi
        //    ekrandaki listeyi tazelemek.
        //
        // 2) _allNetworkFiles.Clear() / NetworkFiles.Clear() -- indirilmekte veya
        //    Duraklatildi durumundaki dosyaların SharedFile nesnesini de siliyordu.
        //    Karşı taraf tekrar keşfedildiğinde NetworkService_FilesReceived bu
        //    dosya için YENİ bir SharedFile nesnesi oluşturuyordu (existingEntry
        //    artık null olduğu için). Bu yeni nesne UI'a arka plan thread'inden
        //    Task.Run + MainThread.BeginInvokeOnMainThread ile ekleniyor; olası
        //    çakışan discovery event'leriyle (madde 1) birleşince CollectionView'in
        //    ItemsSource'daki nesne ile ekranda render ettiği view arasındaki bağ
        //    kopabiliyor, buton görünür kalıyor ama CommandParameter artık ekrandaki
        //    view'a karşılık gelmiyor -- tıklama hiçbir şey tetiklemiyor.
        //    Çözüm: indirilmekte/duraklamış dosyaların nesnesini KORU, sadece
        //    gerçekten ilgisiz/ölü (indirme durumunda olmayan) kayıtları temizle.
        var toRemove = _allNetworkFiles.Where(f => !f.IsDownloading && f.State != DownloadState.Duraklatildi).ToList();
        foreach (var f in toRemove)
        {
            _allNetworkFiles.Remove(f);
        }
        ApplyNetworkFilter();

        _isScanning = true;
        ScanProgressBar.IsVisible = true;
        ScanProgressBar.Progress = 0;

        await ScanProgressBar.ProgressTo(0.9, 4000, Easing.Linear);

        if (_isScanning)
        {
            _isScanning = false;
            await ScanProgressBar.ProgressTo(1.0, 300, Easing.CubicOut);
            ScanProgressBar.IsVisible = false;
            ScanProgressBar.Progress = 0;
        }
    }

    // ARAMA

    private void OnFileSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        ClearFileSearchButton.IsVisible = !string.IsNullOrEmpty(e.NewTextValue);
        ApplyMyFilesFilter();
        ApplyNetworkFilter();
    }

    private void OnClearFileSearchClicked(object sender, EventArgs e)
    {
        FileSearchEntry.Text = string.Empty;
        ClearFileSearchButton.IsVisible = false;
        ApplyMyFilesFilter();
        ApplyNetworkFilter();
    }

    private void ApplyMyFilesFilter()
    {
        string query = FileSearchEntry?.Text?.Trim() ?? string.Empty;

        var filtered = string.IsNullOrEmpty(query)
            ? _allMyFiles
            : _allMyFiles.Where(f => f.FileName.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

        MyFiles.Clear();
        foreach (var f in filtered) MyFiles.Add(f);
    }

    // TÜR FİLTRESİ (chip'ler)

    private void BuildFilterChips()
    {
        FilterChipsLayout.Children.Clear();

        foreach (var type in TypeFilters)
        {
            var chip = new Border
            {
                Padding = new Thickness(14, 6),
                StrokeThickness = 1,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 16 },
                BackgroundColor = type == _activeTypeFilter ? Color.FromArgb("#6366F1") : Color.FromArgb("#111827"),
                Stroke = type == _activeTypeFilter ? Color.FromArgb("#6366F1") : Color.FromArgb("#1E293B")
            };

            var label = new Label
            {
                Text = type,
                FontSize = 12,
                FontAttributes = FontAttributes.Bold,
                TextColor = type == _activeTypeFilter ? Colors.White : Color.FromArgb("#94A3B8")
            };

            chip.Content = label;

            var tap = new TapGestureRecognizer();
            tap.Tapped += (s, e) =>
            {
                _activeTypeFilter = type;
                BuildFilterChips();
                ApplyNetworkFilter();
            };
            chip.GestureRecognizers.Add(tap);

            FilterChipsLayout.Children.Add(chip);
        }
    }

    private void ApplyNetworkFilter()
    {
        string query = FileSearchEntry?.Text?.Trim() ?? string.Empty;

        IEnumerable<SharedFile> filtered = _allNetworkFiles;

        if (!string.IsNullOrEmpty(query))
        {
            filtered = filtered.Where(f =>
                f.FileName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                f.OwnerName.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        if (_activeTypeFilter != "Tümü")
        {
            filtered = filtered.Where(f => MatchesTypeFilter(f.FileName, _activeTypeFilter));
        }

        var result = filtered.ToList();

        NetworkFiles.Clear();
        foreach (var f in result) NetworkFiles.Add(f);
    }

    private static bool MatchesTypeFilter(string fileName, string filter)
    {
        string ext = Path.GetExtension(fileName).ToLowerInvariant();

        bool isImage = ext is ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" or ".bmp";
        bool isVideo = ext is ".mp4" or ".mov" or ".avi" or ".mkv" or ".webm";
        bool isDoc = ext is ".pdf" or ".doc" or ".docx" or ".xls" or ".xlsx" or ".ppt" or ".pptx" or ".txt";

        return filter switch
        {
            "Resim" => isImage,
            "Video" => isVideo,
            "Belge" => isDoc,
            "Diğer" => !isImage && !isVideo && !isDoc,
            _ => true
        };
    }
}