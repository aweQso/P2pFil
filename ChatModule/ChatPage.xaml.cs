using P2PFil;
using System.Collections.ObjectModel;
using System.Linq;
using System.IO;

namespace P2PFil.ChatModule
{
    public partial class ChatPage : ContentPage
    {
        private readonly string _targetIp;

        // DÜZELTME 1: TargetIp dışarıdan (App.xaml.cs tarafından) okunabilir hale getirildi
        public string TargetIp => _targetIp;

        private string _targetName;
        private Action<string, string, string> _peerNameChangedHandler;

        public ObservableCollection<ChatMessage> Messages { get; } = new();

        public ChatPage(string targetIp, string targetName)
        {
            InitializeComponent();
            _targetIp = targetIp;
            _targetName = targetName;
            Title = $"{_targetName} ile DM";

            MessagesListView.ItemsSource = Messages;

            LoadHistory();
            App.ChatService.OnMessageReceived += ChatService_OnMessageReceived;

            // DÜZELTME 2: İsim değişiminde hem başlığı hem de geçmiş mesajları güncelliyoruz
            _peerNameChangedHandler = (ip, oldName, newName) =>
            {
                if (ip == _targetIp)
                {
                    _targetName = newName;

                    // Global mesaj listesindeki eski isme sahip mesajları bul ve yeni isimle güncelle
                    var messagesToUpdate = ChatService.GlobalMessages
    .Where(m => m.SenderName == oldName || m.TargetName == oldName)
    .ToList();

                    foreach (var msg in messagesToUpdate)
                    {
                        if (msg.SenderName == oldName) msg.SenderName = newName;
                        if (msg.TargetName == oldName) msg.TargetName = newName;
                    }

                    // Arayüzü (UI) yeni isimler ve başlıkla yenile
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        Title = $"{_targetName} ile DM";
                        Messages.Clear(); // Mevcut listeyi temizle
                        LoadHistory();    // Güncel isimlerle geçmişi tekrar yükle
                    });
                }
            };
            App.NetworkService.PeerNameChanged += _peerNameChangedHandler;
        }

        private void LoadHistory()
        {
            var history = ChatService.GlobalMessages.Where(m =>
                (m.SenderName == _targetName && !m.IsMe) ||
                (m.TargetName == _targetName && m.IsMe)
            ).ToList();

            foreach (var msg in history)
            {
                Messages.Add(msg);
            }

            if (Messages.Count > 0)
            {
                MessagesListView.ScrollTo(Messages.Count - 1);
            }
        }

        private void ChatService_OnMessageReceived(ChatMessage msg)
        {
            // İsim eşleşiyorsa (PeerNameChanged zaten arka planda güncellediği için burası çalışır) mesajı ekrana bas
            if (msg.SenderName == _targetName)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    Messages.Add(msg);
                    MessagesListView.ScrollTo(Messages.Count - 1);
                });
            }
        }

        private async void OnSendClicked(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(MessageEntry.Text)) return;

            string text = MessageEntry.Text;
            MessageEntry.Text = string.Empty;

            await App.ChatService.SendMessageAsync(_targetIp, text);

            var myMsg = new ChatMessage
            {
                SenderName = App.CurrentUsername,
                TargetName = _targetName,
                Content = text,
                MessageType = "Text",
                Timestamp = DateTime.Now,
                IsMe = true
            };

            Messages.Add(myMsg);
            ChatService.GlobalMessages.Add(myMsg);
            MessagesListView.ScrollTo(Messages.Count - 1);
        }

        private async void OnAttachClicked(object sender, EventArgs e)
        {
            try
            {
                var result = await FilePicker.Default.PickAsync(new PickOptions
                {
                    PickerTitle = "Fotoğraf veya Video Seç"
                });

                if (result != null)
                {
                    var fileInfo = new FileInfo(result.FullPath);

                    if (fileInfo.Length > 15 * 1024 * 1024)
                    {
                        await DisplayAlert("Hata", "Dosya boyutu 15 MB'den büyük olamaz! Lütfen 'Dosyalar' sekmesini kullanın.", "Tamam");
                        return;
                    }

                    string ext = fileInfo.Extension.ToLower();
                    string mediaType = "Text";
                    if (ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".gif")
                        mediaType = "Image";
                    else if (ext == ".mp4" || ext == ".mov" || ext == ".avi" || ext == ".mkv")
                        mediaType = "Video";
                    else
                    {
                        await DisplayAlert("Hata", "Sadece fotoğraf ve video gönderebilirsiniz.", "Tamam");
                        return;
                    }

                    var btn = sender as Button;
                    if (btn != null) btn.Text = "⏳";

                    await App.ChatService.SendMediaAsync(_targetIp, result.FullPath, mediaType);

                    if (btn != null) btn.Text = "📎";

                    var myMsg = new ChatMessage
                    {
                        SenderName = App.CurrentUsername,
                        TargetName = _targetName,
                        Content = result.FileName,
                        MessageType = mediaType,
                        LocalMediaPath = result.FullPath,
                        Timestamp = DateTime.Now,
                        IsMe = true
                    };

                    Messages.Add(myMsg);
                    ChatService.GlobalMessages.Add(myMsg);
                    MessagesListView.ScrollTo(Messages.Count - 1);
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Hata", "Medya gönderilemedi: " + ex.Message, "Tamam");
                var btn = sender as Button;
                if (btn != null) btn.Text = "📎";
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

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            App.ChatService.OnMessageReceived -= ChatService_OnMessageReceived;

            if (_peerNameChangedHandler != null)
            {
                App.NetworkService.PeerNameChanged -= _peerNameChangedHandler;
            }
        }
    }
}