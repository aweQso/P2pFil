using P2PFil;
using System.Collections.ObjectModel;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using Microsoft.Maui.ApplicationModel;
using System;
using P2PFil.Services;

namespace P2PFil.ChatModule
{
    public partial class ChatPage : ContentPage
    {
        private readonly string _targetDeviceId;
        private string _targetName = string.Empty;
        private string _targetProfileImagePath = string.Empty;
        private readonly Action<string, string, string> _peerNameChangedHandler;

        // Kullanıcı listeyi yukarı kaydırdıysa yeni gelen mesajları otomatik göstermek yerine rozet göster
        private bool _isNearBottom = true;

        public string TargetDeviceId => _targetDeviceId;

        // MVVM yapısına uyum için ObservableCollection
        public ObservableCollection<ChatMessage> Messages { get; } = new();

        public string TargetName
        {
            get => _targetName;
            set
            {
                _targetName = value;
                OnPropertyChanged(nameof(TargetName));
            }
        }

        public string TargetProfileImagePath
        {
            get => _targetProfileImagePath;
            set
            {
                _targetProfileImagePath = value;
                OnPropertyChanged(nameof(TargetProfileImagePath));
            }
        }

        public ChatPage(string targetDeviceId, string targetName, string? targetProfileImagePath = null)
        {
            InitializeComponent();
            BindingContext = this;

            _targetDeviceId = targetDeviceId;
            TargetName = targetName;
            TargetProfileImagePath = targetProfileImagePath ?? "dotnet_bot.png";

            // XAML'daki CollectionView ile veriyi bağlıyoruz
            MessagesListView.ItemsSource = Messages;

            LoadHistory();
            UpdateOnlineStatus();

            _peerNameChangedHandler = (ip, oldName, newName) =>
            {
                TargetName = newName;

                var messagesToUpdate = ChatService.GlobalMessages
                    .Where(m => m.SenderName == oldName || m.TargetName == oldName)
                    .ToList();

                foreach (var msg in messagesToUpdate)
                {
                    if (msg.SenderName == oldName) msg.SenderName = newName;
                    if (msg.TargetName == oldName) msg.TargetName = newName;
                }

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    Messages.Clear();
                    LoadHistory();
                });

                UpdateOnlineStatus();
            };
        }

        // METOD GÜNCELLENDİ: MAUI'deki Image caching hatasını aşmak için iyileştirildi
        private void OnPeerProfileChanged(string deviceId)
        {
            if (deviceId != _targetDeviceId) return;

            string imagePath = Path.Combine(FileSystem.CacheDirectory, $"{deviceId}_profile.png");
            if (File.Exists(imagePath))
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    // MAUI Cache bug'ını aşmak için mülkü önce boşaltıyoruz, ardından yeni resmi bağlıyoruz
                    TargetProfileImagePath = string.Empty;
                    TargetProfileImagePath = imagePath;
                });
            }
        }

        private async void OnBackClicked(object sender, EventArgs e)
        {
            await Navigation.PopAsync();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            UpdateOnlineStatus();

            // Sayfa her açıldığında profil resmini kontrol et (sen arkadayken değiştiyse anında güncellenir)
            OnPeerProfileChanged(_targetDeviceId);

            App.ChatService.OnMessageReceived += ChatService_OnMessageReceived;

            // YENİ: Hafıza sızıntısını önlemek için dinleyiciyi sayfa görünür olduğunda ekliyoruz
            ProfileMessenger.PeerProfileChanged += OnPeerProfileChanged;

            if (_peerNameChangedHandler != null)
            {
                App.NetworkService.PeerNameChanged += _peerNameChangedHandler;
            }
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            App.ChatService.OnMessageReceived -= ChatService_OnMessageReceived;

            // YENİ: Sayfadan çıkıldığında dinleyiciyi kaldırarak ChatPage nesnesinin RAM'de kilitli kalmasını engelliyoruz
            ProfileMessenger.PeerProfileChanged -= OnPeerProfileChanged;

            if (_peerNameChangedHandler != null)
            {
                App.NetworkService.PeerNameChanged -= _peerNameChangedHandler;
            }
        }

        private void UpdateOnlineStatus()
        {
            string ip = App.NetworkService.GetIpByDeviceId(_targetDeviceId);
            bool online = !string.IsNullOrWhiteSpace(ip);

            OnlineDot.IsVisible = online;
            StatusLabel.Text = online ? "Çevrimiçi" : "Çevrimdışı";
            StatusLabel.TextColor = online ? Color.FromArgb("#10B981") : Color.FromArgb("#64748B");
        }

        private void LoadHistory()
        {
            var history = ChatService.GlobalMessages.Where(m =>
                (m.SenderName == _targetName && !m.IsMe) ||
                (m.TargetName == _targetName && m.IsMe)
            ).OrderBy(m => m.Timestamp).ToList();

            foreach (var msg in history)
            {
                if (!Messages.Any(m => m.MessageId == msg.MessageId))
                {
                    AppendWithDateSeparator(msg);
                }
            }

            // Sayfa açıldığında veya geçmiş yüklendiğinde en alta kaydır
            ScrollToBottom();
        }

        // Yeni mesajı listeye eklerken, bir önceki mesajla farklı güne denk geliyorsa
        // üzerine "Bugün / Dün / tarih" ayırıcı etiketi koyar.
        private void AppendWithDateSeparator(ChatMessage msg)
        {
            var lastMsg = Messages.LastOrDefault();
            msg.ShowDateSeparator = lastMsg == null || lastMsg.Timestamp.Date != msg.Timestamp.Date;
            Messages.Add(msg);
        }

        private void ChatService_OnMessageReceived(ChatMessage msg)
        {
            if (msg.SenderName == _targetName)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (!Messages.Any(m => m.MessageId == msg.MessageId))
                    {
                        AppendWithDateSeparator(msg);

                        if (_isNearBottom)
                        {
                            ScrollToBottom();
                        }
                        else
                        {
                            NewMessageBadge.IsVisible = true;
                        }
                    }
                });
            }
        }

        private async void OnSendClicked(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(MessageEntry.Text)) return;
            string text = MessageEntry.Text;
            MessageEntry.Text = string.Empty;

            string currentIp = App.NetworkService.GetIpByDeviceId(_targetDeviceId);

            if (string.IsNullOrWhiteSpace(currentIp))
            {
                await DisplayAlert("Hata", "Bu cihaz şu an ağda bulunamadı veya kullanıcı çevrimdışı.", "Tamam");
                return;
            }

            var myMsg = new ChatMessage
            {
                MessageId = Guid.NewGuid().ToString(),
                SenderName = App.CurrentUsername,
                TargetName = _targetName,
                Content = text,
                MessageType = "Text",
                Timestamp = DateTime.Now,
                IsMe = true,
                Status = MessageStatus.Sending
            };

            AppendWithDateSeparator(myMsg);
            ChatService.GlobalMessages.Add(myMsg);
            ScrollToBottom();

            try
            {
                await App.ChatService.SendMessageAsync(currentIp, text);
                myMsg.Status = MessageStatus.Sent;
            }
            catch (Exception ex)
            {
                myMsg.Status = MessageStatus.Failed;
                await DisplayAlert("Hata", $"Mesaj gönderilemedi: {ex.Message}", "Tamam");
            }
        }

        private async void OnAttachClicked(object sender, EventArgs e)
        {
            Button? btn = sender as Button;

            try
            {
                var result = await FilePicker.Default.PickAsync(new PickOptions
                {
                    PickerTitle = "Fotoğraf veya Video Seç"
                });

                if (result == null) return;

                var fileInfo = new FileInfo(result.FullPath);

                if (fileInfo.Length > 15 * 1024 * 1024)
                {
                    await DisplayAlert("Hata", "Dosya boyutu 15 MB'den büyük olamaz! Lütfen 'Dosyalar' sekmesini kullanın.", "Tamam");
                    return;
                }

                string ext = fileInfo.Extension.ToLower();
                string mediaType;

                if (ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".gif")
                    mediaType = "Image";
                else if (ext == ".mp4" || ext == ".mov" || ext == ".avi" || ext == ".mkv")
                    mediaType = "Video";
                else
                {
                    await DisplayAlert("Hata", "Sadece fotoğraf ve video gönderebilirsiniz.", "Tamam");
                    return;
                }

                string currentIp = App.NetworkService.GetIpByDeviceId(_targetDeviceId);

                if (string.IsNullOrWhiteSpace(currentIp))
                {
                    await DisplayAlert("Hata", "Kullanıcı şu anda çevrimdışı.", "Tamam");
                    return;
                }

                if (btn != null) { btn.Text = "⏳"; btn.IsEnabled = false; }

                var myMsg = new ChatMessage
                {
                    MessageId = Guid.NewGuid().ToString(),
                    SenderName = App.CurrentUsername,
                    TargetName = _targetName,
                    Content = result.FileName,
                    MessageType = mediaType,
                    LocalMediaPath = result.FullPath,
                    Timestamp = DateTime.Now,
                    IsMe = true,
                    Status = MessageStatus.Sending
                };

                AppendWithDateSeparator(myMsg);
                ChatService.GlobalMessages.Add(myMsg);
                ScrollToBottom();

                try
                {
                    await App.ChatService.SendMediaAsync(currentIp, result.FullPath, mediaType);
                    myMsg.Status = MessageStatus.Sent;
                }
                catch (Exception sendEx)
                {
                    myMsg.Status = MessageStatus.Failed;
                    await DisplayAlert("Hata", "Medya gönderilemedi: " + sendEx.Message, "Tamam");
                }
                finally
                {
                    if (btn != null) { btn.Text = "📎"; btn.IsEnabled = true; }
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Hata", "Medya seçilemedi: " + ex.Message, "Tamam");
                if (btn != null) { btn.Text = "📎"; btn.IsEnabled = true; }
            }
        }

        private async void OnMediaTapped(object sender, TappedEventArgs e)
        {
            if (e.Parameter is string localPath && !string.IsNullOrEmpty(localPath))
            {
                if (File.Exists(localPath))
                {
                    await Launcher.Default.OpenAsync(new OpenFileRequest("Medya Aç", new ReadOnlyFile(localPath)));
                }
                else
                {
                    await DisplayAlert("Hata", "Dosya cihazda bulunamadı veya silinmiş.", "Tamam");
                }
            }
        }

        private void OnNewMessageBadgeTapped(object sender, TappedEventArgs e)
        {
            NewMessageBadge.IsVisible = false;
            ScrollToBottom();
        }

        // Kullanıcı listede yukarı kaydırdığında yeni mesajları sessizce eklemek yerine
        // rozet göstermek için "en altta mıyız" durumunu takip eder.
        private void OnMessagesScrolled(object sender, ItemsViewScrolledEventArgs e)
        {
            int lastVisible = e.LastVisibleItemIndex;
            int total = Messages.Count - 1;

            _isNearBottom = total <= 0 || lastVisible >= total - 1;

            if (_isNearBottom)
            {
                NewMessageBadge.IsVisible = false;
            }
        }

        // KOD TEKRARINI ÖNLEMEK VE GÜVENLİ KAYDIRMA İÇİN YARDIMCI METOT
        private void ScrollToBottom()
        {
            if (Messages.Count > 0)
            {
                // UI güncellemeleri MainThread üzerinde olmalıdır
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    try
                    {
                        MessagesListView.ScrollTo(index: Messages.Count - 1, position: ScrollToPosition.End, animate: true);
                        _isNearBottom = true;
                        NewMessageBadge.IsVisible = false;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Scroll Hatası: {ex.Message}");
                    }
                });
            }
        }
    }
}