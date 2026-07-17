using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using CommunityToolkit.Maui.Storage;
using P2PFil.Services;
using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Maui.Storage;
using System.IO;

#if ANDROID
using Android.OS;
#endif

namespace P2PFil;

public partial class ProfilePage : ContentPage
{
    private ProfileViewModel _viewModel;

    public ProfilePage()
    {
        InitializeComponent();
        _viewModel = new ProfileViewModel();
        BindingContext = _viewModel;
        UsernameEntry.Text = SettingsService.Username;
        PerformanceModeSwitch.IsToggled = _viewModel.IsPerformanceModeEnabled;
        MessageNotificationsSwitch.IsToggled = SettingsService.MessageNotificationsEnabled;

        // YENİ: Kayıtlı Performans Modu tercihini TransferManager'ın eşzamanlı
        // aktif indirme limitine uygula (sayfa her açıldığında -- ki uygulama
        // yeniden başlatıldığında da bu constructor çalışır -- doğru limit
        // garantilenmiş olur).
        ApplyPerformanceModeToTransferManager(_viewModel.IsPerformanceModeEnabled);

        // YEN�: NetworkService'den gelen h�z g�ncellemelerini dinle
        if (App.NetworkService != null)
        {
            App.NetworkService.GlobalSpeedUpdated += (speed) =>
            {
                // Ana thread �zerinde g�ncelleme yap
                MainThread.BeginInvokeOnMainThread(() => {
                    _viewModel.CurrentDownloadSpeed = speed;
                });
            };
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.RefreshData(); // Sayfa her a��ld���nda disk alan�n� ve payla��m say�s�n� tazele[cite: 9]

        int targetIndex = 2;
        double direction = (targetIndex > App.CurrentTabIndex) ? 1 : -1;
        AnimatedContent.TranslationX = direction * this.Width;
        AnimatedContent.Opacity = 0;
        await Task.Delay(50);
        _ = AnimatedContent.FadeTo(1, 750, Easing.CubicOut);
        await AnimatedContent.TranslateTo(0, 0, 750, Easing.CubicOut);
        MyCustomTabBar?.SetActiveIndex(targetIndex);
        App.CurrentTabIndex = targetIndex;
    }

    // E�er ImageCropper paketini kurarsan OnChangeProfileImageTapped metodun b�yle olmal�:
    private async void OnChangeProfileImageTapped(object sender, TappedEventArgs e)
    {
        try
        {
            // Standart, hatas�z MediaPicker kullan�m�
            var result = await MediaPicker.Default.PickPhotoAsync(new MediaPickerOptions
            {
                Title = "Profil Foto�raf� Se�"
            });

            if (result != null)
            {
                // Se�ilen dosyan�n yolunu kaydediyoruz
                SettingsService.ProfileImagePath = result.FullPath;

                // ViewModel'i tazeliyoruz, UI otomatik g�ncellenecek
                _viewModel.RefreshData();

                // YENİ: Periyodik UDP paketini beklemeden ağa anında bildir + arka
                // plandaki diğer sayfaları (Messenger üzerinden) anında güncelle.
                App.NetworkService.AnnounceProfileChange();
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Hata", "Fotoğraf seçilirken bir sorun oluştu: " + ex.Message, "Tamam");
        }
    }

    private async void OnChangeFolderTapped(object sender, TappedEventArgs e)
    {
        try
        {
            var folderPicker = FolderPicker.Default;
            var result = await folderPicker.PickAsync(default);

            if (result.IsSuccessful && !string.IsNullOrEmpty(result.Folder?.Path))
            {
                SettingsService.DownloadFolder = result.Folder.Path;
                _viewModel.RefreshData();
            }
            else if (result.Exception != null)
            {
                await DisplayAlert("Hata", "Klasör seçilemedi: " + result.Exception.Message, "Tamam");
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Hata", "Beklenmedik bir hata: " + ex.Message, "Tamam");
        }
    }

    private void OnUsernameEntryTextChanged(object sender, TextChangedEventArgs e)
    {
        string newName = e.NewTextValue ?? "";
        if (newName != SettingsService.Username && newName.Length >= 3 && newName.Length <= 32)
        {
            SaveNameButton.IsEnabled = true;
            SaveNameButton.Text = "Değişiklikleri Kaydet";
            SaveNameButton.BackgroundColor = Color.FromArgb("#10B981");
        }
        else
        {
            SaveNameButton.IsEnabled = false;
            SaveNameButton.Text = (newName == SettingsService.Username) ? "Değişiklik Yok" : "Geçersiz İsim";
            SaveNameButton.BackgroundColor = Color.FromArgb("#1E293B");
        }
    }

    private void OnPerformanceModeToggled(object sender, ToggledEventArgs e)
    {
        _viewModel.IsPerformanceModeEnabled = e.Value;
        ApplyPerformanceModeToTransferManager(e.Value);
    }

    // YENİ: Mesaj geldiğinde çıkan bildirim penceresini (App.xaml.cs'deki
    // DisplayAlert) açıp kapatmak için kullanılan anahtar.
    private void OnMessageNotificationsToggled(object sender, ToggledEventArgs e)
    {
        SettingsService.MessageNotificationsEnabled = e.Value;
    }

    // YENİ: Performans Modu açıkken aynı anda sadece 1 aktif transfere izin
    // verilir (CPU/GC/ağ yükünü en aza indirmek için); kapalıyken varsayılan
    // eşzamanlılık limitine (3) dönülür. Bu, TransferManager.cs'deki kuyruk
    // mantığını DEĞİŞTİRMEZ -- sadece aynı anda kaç isteğin aktif çalışacağını
    // belirler, geri kalanlar zaten kuyrukta bekliyor olacaktır.
    private const int NormalModeMaxConcurrency = 3;
    private const int PerformanceModeMaxConcurrency = 1;

    private static void ApplyPerformanceModeToTransferManager(bool performanceModeEnabled)
    {
        TransferManager.Instance.SetMaxConcurrency(
            performanceModeEnabled ? PerformanceModeMaxConcurrency : NormalModeMaxConcurrency);
    }

    private async void OnSaveClicked(object sender, EventArgs e)
    {
        string newName = UsernameEntry.Text ?? "";
        SettingsService.Username = newName;
        App.CurrentUsername = newName;
        // NOT: AnnounceNameChange artık broadcast döngüsünü ANINDA uyandırır
        // (bkz. NetworkService._broadcastWakeSignal) -- önceden en fazla 5 saniyelik
        // bir sonraki tur beklenirdi.
        App.NetworkService.AnnounceNameChange();
        SaveNameButton.Text = "Ayarlar Uygulandı!";
        SaveNameButton.IsEnabled = false;
        SaveNameButton.BackgroundColor = Color.FromArgb("#1E293B");
        _viewModel.RefreshData();

        SavedConfirmationLabel.Opacity = 0;
        SavedConfirmationLabel.IsVisible = true;
        await SavedConfirmationLabel.FadeTo(1, 200);
        await Task.Delay(1800);
        await SavedConfirmationLabel.FadeTo(0, 300);
        SavedConfirmationLabel.IsVisible = false;
    }
}

public class ProfileViewModel : BindableObject
{
    public string DownloadFolder => SettingsService.DownloadFolder;
    public string ProfileImagePath => SettingsService.ProfileImagePath;
    public string DeviceId => App.DeviceId;
    public int SharedFilesCount => FileService.GetSavedFiles().Count;

    // Yeni: H�z verisini tutan de�i�ken
    private string _currentDownloadSpeed = "0 MB/s";
    public string CurrentDownloadSpeed
    {
        get => _currentDownloadSpeed;
        set
        {
            if (_currentDownloadSpeed != value)
            {
                _currentDownloadSpeed = value;
                OnPropertyChanged(); // UI'� otomatik g�ncelle
            }
        }
    }

    public bool IsPerformanceModeEnabled
    {
        get => Preferences.Get("PerformanceMode", false);
        set
        {
            Preferences.Set("PerformanceMode", value);
            OnPropertyChanged();
        }
    }

    // YEN�: Depolama alan�n� hesaplayan ak�ll� property'ler
    public string FreeSpaceText
    {
        get
        {
            try
            {
                var path = DownloadFolder;
                if (string.IsNullOrWhiteSpace(path))
                    path = FileSystem.AppDataDirectory;

#if ANDROID
                var stat = new StatFs(path);

                long totalBytes = stat.BlockCountLong * stat.BlockSizeLong;
                long freeBytes = stat.AvailableBlocksLong * stat.BlockSizeLong;

                double freeGb = freeBytes / 1024.0 / 1024.0 / 1024.0;
                double totalGb = totalBytes / 1024.0 / 1024.0 / 1024.0;
#else
                var drive = new DriveInfo(Path.GetPathRoot(path)!);

                double freeGb = drive.AvailableFreeSpace / 1024.0 / 1024.0 / 1024.0;
                double totalGb = drive.TotalSize / 1024.0 / 1024.0 / 1024.0;
#endif

                return $"{freeGb:F1} GB Boş / {totalGb:F1} GB";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
                return "Hesaplanamadı";
            }
        }
    }

    // Mevcut FreeSpacePercent k�sm�n� S�L ve �UNLARI YAPI�TIR:

    public double UsedSpacePercent
    {
        get
        {
            try
            {
                var path = DownloadFolder;
                if (string.IsNullOrWhiteSpace(path))
                    path = FileSystem.AppDataDirectory;

#if ANDROID
                var stat = new StatFs(path);

                long totalBytes = stat.BlockCountLong * stat.BlockSizeLong;
                long freeBytes = stat.AvailableBlocksLong * stat.BlockSizeLong;
#else
                var drive = new DriveInfo(Path.GetPathRoot(path)!);

                long totalBytes = drive.TotalSize;
                long freeBytes = drive.AvailableFreeSpace;
#endif

                if (totalBytes <= 0)
                    return 0;

                return (double)(totalBytes - freeBytes) / totalBytes;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
                return 0;
            }
        }
    }

    // Alan %85'ten fazla doluysa KIRMIZI, de�ilse YE��L yap
    public Color UsedSpaceColor => UsedSpacePercent > 0.85 ? Color.FromArgb("#EF4444") : Color.FromArgb("#10B981");

    public void RefreshData()
    {
        OnPropertyChanged(nameof(DownloadFolder));
        OnPropertyChanged(nameof(ProfileImagePath));
        OnPropertyChanged(nameof(SharedFilesCount));
        OnPropertyChanged(nameof(FreeSpaceText));
        OnPropertyChanged(nameof(UsedSpacePercent));
        OnPropertyChanged(nameof(UsedSpaceColor));
        // H�z� refresh tetiklendi�inde de g�ncelleyebilirsin
    }
}