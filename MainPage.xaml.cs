using System.Collections.ObjectModel;
using P2PFil.ChatModule;

namespace P2PFil;

public partial class MainPage : ContentPage
{
    public ObservableCollection<string> Peers { get; } = new();

    public MainPage()
    {
        InitializeComponent();
        PeersList.ItemsSource = Peers;

        // DÜZELTME 1: Event dinleyicilerini buraya taşıdık! 
        // Böylece sen başka sayfadayken bile MainPage arka planda isim değişikliklerini duymaya devam eder.
        App.NetworkService.PeerFound += NetworkService_PeerFound;
        App.NetworkService.PeerNameChanged += NetworkService_PeerNameChanged;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // OnAppearing içinde sadece UI başlangıç ayarlarını bırakıyoruz.
        if (!string.IsNullOrWhiteSpace(App.CurrentUsername) && App.CurrentUsername.Length >= 3)
        {
            UsernameEntry.Text = App.CurrentUsername;
            StartDiscoveryButton.IsEnabled = false;
            StartDiscoveryButton.Text = "Ağ Dinleniyor...";
            App.NetworkService.StartDiscovery();
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        // DÜZELTME 2: Buradaki abonelik iptallerini SİLDİK. 
        // Böylece sayfadan çıkınca olayları dinlemeyi sağır gibi bırakmayacak.
    }

    private void NetworkService_PeerFound(string name)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (!Peers.Contains(name))
                Peers.Add(name);
        });
    }

    private void NetworkService_PeerNameChanged(string ip, string oldName, string newName)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            int index = Peers.IndexOf(oldName);
            if (index != -1)
            {
                // DÜZELTME 3: UI'ın kesinlikle değiştiğini anlaması için (garanti yöntem)
                // Eski ismi siliyoruz ve yeni ismi aynı sıraya ekliyoruz.
                Peers.RemoveAt(index);
                Peers.Insert(index, newName);
            }
        });
    }

    private async void OnPeerSelected(object sender, SelectionChangedEventArgs e)
    {
        var selectedName = e.CurrentSelection.FirstOrDefault() as string;
        if (selectedName == null) return;

        string ip = App.NetworkService.GetIpByName(selectedName);
        await Navigation.PushAsync(new ChatPage(ip, selectedName));

        PeersList.SelectedItem = null;
    }

    private void OnUsernameChanged(object sender, TextChangedEventArgs e)
    {
        string newText = e.NewTextValue ?? "";
        StartDiscoveryButton.IsEnabled = (newText.Length >= 3 && newText.Length <= 32);
    }

    private void OnStartDiscoveryClicked(object sender, EventArgs e)
    {
        string username = UsernameEntry.Text ?? "";
        if (username.Length < 3 || username.Length > 32) return;

        App.CurrentUsername = username;
        App.NetworkService.StartDiscovery();

        StartDiscoveryButton.IsEnabled = false;
        StartDiscoveryButton.Text = "Ağ Dinleniyor...";
    }

    private async void OnGoToFilesClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync(nameof(FilesPage), animate: true);
    }
}