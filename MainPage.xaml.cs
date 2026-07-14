using System.Collections.ObjectModel;
using System.Linq;
using P2PFil.ChatModule;
using Microsoft.Maui.Controls;

namespace P2PFil;

public partial class MainPage : ContentPage
{
    public ObservableCollection<string> Peers { get; } = new();

    public MainPage()
    {
        InitializeComponent();
        PeersList.ItemsSource = Peers;

        App.NetworkService.PeerFound += NetworkService_PeerFound;
        App.NetworkService.PeerNameChanged += NetworkService_PeerNameChanged;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

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
                Peers.RemoveAt(index);
                Peers.Insert(index, newName);
            }
        });
    }

    private async void OnPeerSelected(object sender, SelectionChangedEventArgs e)
    {
        var selectedName = e.CurrentSelection.FirstOrDefault() as string;
        if (selectedName == null) return;

        string deviceId = App.NetworkService.GetDeviceIdByName(selectedName);
        await Navigation.PushAsync(new ChatPage(deviceId, selectedName));

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
        await Shell.Current.GoToAsync("FilesPage", animate: true);
    }
}