using System.Collections.ObjectModel;
using P2PFil.Models;
using P2PFil.Services;

namespace P2PFil;

public partial class FilesPage : ContentPage
{
    public ObservableCollection<SharedFile> MyFiles { get; } = new();
    public ObservableCollection<SharedFile> NetworkFiles { get; } = new();

    public FilesPage()
    {
        InitializeComponent();
        MyFilesCollection.ItemsSource = MyFiles;
        NetworkFilesCollection.ItemsSource = NetworkFiles;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // SIZINTI ÖNLENDÝ: Aboneliði buraya taþýdýk ki "Transient" sayfa çoðaldýkça liste çýldýrmasýn
        App.NetworkService.FilesReceived += NetworkService_FilesReceived;

        LoadMyFiles();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        // Sayfadan çýkýldýðýnda dinlemeyi býrakýr, arkaplaný rahatlatýr
        App.NetworkService.FilesReceived -= NetworkService_FilesReceived;
    }

    private void NetworkService_FilesReceived(string senderIp, List<SharedFile> files)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            // DÜZELTME: NetworkFiles.Clear(); satýrýný SÝLDÝK![cite: 1]
            // Bunun yerine akýllý listeleme (senkronizasyon) yapýyoruz.

            // 1. Gelen yeni dosyalarý listeye ekle
            foreach (var incomingFile in files)
            {
                incomingFile.SenderId = senderIp;

                // Bu dosya (ayný kiþiden gelen) zaten listemizde var mý?
                var existingFile = NetworkFiles.FirstOrDefault(f => f.FileName == incomingFile.FileName && f.SenderId == senderIp);

                if (existingFile == null)
                {
                    // Dosya listede yoksa, ilk defa görüyorsak ekle
                    NetworkFiles.Add(incomingFile);
                }
                // Eðer dosya listede varsa, HÝÇBÝR ÞEY YAPMIYORUZ!
                // Obje ayný kaldýðý için indirme (IsDownloading) durumu ve buton asla bozulmaz.
            }

            // 2. Artýk aðda olmayan (karþý tarafýn sildiði) ama o an ÝNDÝRÝLMEYEN dosyalarý temizle
            var deadFiles = NetworkFiles.Where(f => f.SenderId == senderIp && !files.Any(inf => inf.FileName == f.FileName) && !f.IsDownloading).ToList();

            foreach (var deadFile in deadFiles)
            {
                NetworkFiles.Remove(deadFile);
            }
        });
    }

    private void LoadMyFiles()
    {
        MyFiles.Clear();
        var files = FileService.GetSavedFiles();
        foreach (var f in files)
        {
            MyFiles.Add(new SharedFile
            {
                FileName = f.Name,
                FileSize = $"{f.Length / 1024 / 1024.0:F2} MB"
            });
        }
    }

    private async void OnPickFileClicked(object sender, EventArgs e)
    {
        try
        {
            var result = await FilePicker.Default.PickAsync();
            if (result != null)
            {
                FileService.SaveFile(result.FullPath, result.FileName);
                LoadMyFiles();
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Hata", "Dosya seçilemedi: " + ex.Message, "Tamam");
        }
    }

    private void OnDeleteFileClicked(object sender, EventArgs e)
    {
        var button = sender as Button;
        if (button?.CommandParameter is SharedFile file)
        {
            FileService.DeleteFile(file.FileName);
            LoadMyFiles();
        }
    }

    private async void OnDownloadClicked(object sender, EventArgs e)
    {
        var button = sender as Button;
        if (button?.CommandParameter is SharedFile targetFile && !string.IsNullOrEmpty(targetFile.SenderId))
        {
            targetFile.IsDownloading = true;
            targetFile.StatusMessage = "Baðlanýyor...";

            try
            {
                await App.NetworkService.RequestDownload(targetFile.SenderId, targetFile.FileName, targetFile);
                LoadMyFiles();
                await DisplayAlert("Baþarýlý", $"{targetFile.FileName} baþarýyla indirildi!", "Tamam");
            }
            catch (Exception ex)
            {
                targetFile.StatusMessage = "Hata oluþtu!";
                targetFile.IsDownloading = false;
                await DisplayAlert("Ýndirme Hatasý", ex.Message, "Tamam");
            }
        }
    }

    private void OnRefreshClicked(object sender, EventArgs e)
    {
        NetworkFiles.Clear();
        App.NetworkService.StartDiscovery();
    }

    private async void OnGoToNetworkClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("..", animate: true);
    }
}