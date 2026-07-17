using System.Collections.ObjectModel;
using System.Linq;
using System.IO;
using P2PFil.ChatModule;
using Microsoft.Maui.Controls;
using P2PFil.Services;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace P2PFil
{
    public partial class MainPage : ContentPage
    {
        // Ham (filtrelenmemiş) tüm peer'lar burada tutulur; ekrandaki liste bunun süzülmüş halidir.
        private readonly List<Peer> _allPeers = new();

        public ObservableCollection<Peer> Peers { get; } = new();

        public ICommand OpenChatCommand { get; }

        public MainPage()
        {
            InitializeComponent();
            BindingContext = this;
            PeersList.ItemsSource = Peers;

            OpenChatCommand = new Command<Peer>(async (peer) => await OpenChat(peer));

            App.NetworkService.PeerFound += NetworkService_PeerFound;
            App.NetworkService.PeerNameChanged += NetworkService_PeerNameChanged;

            // YENİ: Constructor'da (sayfa yaşam döngüsünden bağımsız) abone
            // oluyoruz, böylece MainPage arka planda olsa bile bir peer'ın
            // profil resmi güncellendiğinde anında yakalanır -- eskiden bu
            // sadece OnAppearing/OnDisappearing arasında dinleniyordu.
            ProfileMessenger.PeerProfileChanged += OnPeerProfileImageUpdated;

            UpdatePeerCount();
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            MyProfileImageMain.Source = SettingsService.ProfileImagePath;
            // NOT: OnPeerProfileImageUpdated artık constructor'da ProfileMessenger'a
            // abone (bkz. yukarısı); burada tekrar App.NetworkService.ProfileImageUpdated'a
            // abone olmuyoruz ki bildirim iki kez işlenmesin.
            SettingsService.ProfileImageChanged += UpdateProfileImage;

            int targetIndex = 0;
            double direction = (targetIndex > App.CurrentTabIndex) ? 1 : -1;

            AnimatedContent.TranslationX = direction * this.Width;
            AnimatedContent.Opacity = 0;

            await Task.Delay(50);

            _ = AnimatedContent.FadeTo(1, 750, Easing.CubicOut);
            await AnimatedContent.TranslateTo(0, 0, 750, Easing.CubicOut);

            MyCustomTabBar?.SetActiveIndex(targetIndex);
            App.CurrentTabIndex = targetIndex;
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            SettingsService.ProfileImageChanged -= UpdateProfileImage;
        }

        private void UpdateProfileImage()
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                MyProfileImageMain.Source = null; // Önce temizle
                MyProfileImageMain.Source = SettingsService.ProfileImagePath;
            });
        }

        private void OnPeerProfileImageUpdated(string deviceId)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                var peer = _allPeers.FirstOrDefault(p => p.DeviceId == deviceId);
                if (peer != null)
                {
                    string imagePath = Path.Combine(FileSystem.CacheDirectory, $"{deviceId}_profile.png");
                    if (File.Exists(imagePath))
                    {
                        // MAUI Cache'ini tetiklemek için önce adresi sıfırlıyoruz, sonra yeni resmi basıyoruz
                        peer.ProfileImagePath = string.Empty;
                        peer.ProfileImagePath = imagePath;
                    }
                }
            });
        }

        private void NetworkService_PeerFound(string name)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (name == SettingsService.Username) return;

                // Önce DeviceId'yi alıyoruz ki kontrolü bu eşsiz ID üzerinden yapabilelim
                string deviceId = App.NetworkService.GetDeviceIdByName(name);

                // İsim yerine DeviceId ile benzersizlik kontrolü yapıyoruz
                if (!_allPeers.Any(p => p.DeviceId == deviceId))
                {
                    string imagePath = Path.Combine(FileSystem.CacheDirectory, $"{deviceId}_profile.png");
                    string initialImagePath = File.Exists(imagePath) ? imagePath : "dotnet_bot.png";

                    var peer = new Peer
                    {
                        Name = name,
                        DeviceId = deviceId,
                        ProfileImagePath = initialImagePath
                    };

                    _allPeers.Add(peer);
                    ApplyFilter();
                }
            });
        }

        private void NetworkService_PeerNameChanged(string ip, string oldName, string newName)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                var peer = _allPeers.FirstOrDefault(p => p.Name == oldName);

                if (newName == SettingsService.Username)
                {
                    if (peer != null) _allPeers.Remove(peer);
                    ApplyFilter();
                    return;
                }

                if (peer != null)
                {
                    peer.Name = newName;
                    ApplyFilter();
                }
            });
        }

        private async void OnPeerSelected(object sender, SelectionChangedEventArgs e)
        {
            var selectedPeer = e.CurrentSelection.FirstOrDefault() as Peer;
            if (selectedPeer == null) return;

            // Önce sohbete git ve kullanıcının sohbetten geri dönmesini bekle (Await)
            await OpenChat(selectedPeer);

            // Kullanıcı sohbetten geri çıktığı an, listenin seçimini MainThread üzerinde güvenle sıfırla
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (PeersList != null)
                {
                    PeersList.SelectedItem = null;
                }
            });
        }

        private async Task OpenChat(Peer? peer)
        {
            if (peer == null) return;
            await Navigation.PushAsync(new ChatPage(peer.DeviceId, peer.Name, peer.ProfileImagePath));
        }

        private async void OnProfileAvatarTapped(object sender, TappedEventArgs e)
        {
            await Navigation.PushAsync(new ProfilePage());
        }

        // ARAMA / FİLTRELEME

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            ClearSearchButton.IsVisible = !string.IsNullOrEmpty(e.NewTextValue);
            ApplyFilter();
        }

        private void OnClearSearchClicked(object sender, EventArgs e)
        {
            SearchEntry.Text = string.Empty;
            ClearSearchButton.IsVisible = false;
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            string query = SearchEntry?.Text?.Trim() ?? string.Empty;

            var filtered = string.IsNullOrEmpty(query)
                ? _allPeers
                : _allPeers.Where(p => p.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

            Peers.Clear();
            foreach (var p in filtered)
            {
                Peers.Add(p);
            }

            UpdatePeerCount();
        }

        private void UpdatePeerCount()
        {
            if (PeerCountLabel != null)
            {
                PeerCountLabel.Text = _allPeers.Count.ToString();
            }
        }

        // AŞAĞI ÇEKİP YENİLEME: mevcut ağ taramasını tetikler, listeyi sıfırlamaz (peer'lar kalıcı kalsın)
        private async void OnPeersRefreshing(object sender, EventArgs e)
        {
            App.NetworkService.StartDiscovery();
            await Task.Delay(1200);
            PeersRefreshView.IsRefreshing = false;
        }
    }

    public class Peer : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private string _deviceId = string.Empty;
        private string _profileImagePath = string.Empty;

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        public string DeviceId
        {
            get => _deviceId;
            set { _deviceId = value; OnPropertyChanged(); }
        }

        public string ProfileImagePath
        {
            get => _profileImagePath;
            set { _profileImagePath = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
