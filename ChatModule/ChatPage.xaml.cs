using P2PFil;
using System.Collections.ObjectModel;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;

namespace P2PFil.ChatModule
{
    public partial class ChatPage : ContentPage
    {
        private readonly string _targetDeviceId;
        private string _targetName;
        private readonly Action<string, string, string> _peerNameChangedHandler;

        public string TargetDeviceId => _targetDeviceId;
        public ObservableCollection<ChatMessage> Messages { get; } = new();

        public ChatPage(string targetDeviceId, string targetName)
        {
            InitializeComponent();
            _targetDeviceId = targetDeviceId;
            _targetName = targetName;
            Title = $"{_targetName} ile DM";

            MessagesListView.ItemsSource = Messages;

            // DÜZELTME (Küçük Race Condition): Önceki sürümde önce LoadHistory()
            // çağrılıp SONRA OnMessageReceived event'ine abone olunuyordu. Bu iki
            // satır arasındaki (çok kısa ama sıfır olmayan) pencerede gelen bir
            // mesaj hem geçmişte yoktu hem de artık dinlenmiyordu -> kaybolabiliyordu.
            // Artık önce abone oluyoruz, sonra geçmişi yüklüyoruz. LoadHistory
            // zaten GlobalMessages üzerinden çalıştığı ve mesajlar MessageId ile
            // deduplike edilebildiği için bu sıralama değişikliği güvenlidir.
            App.ChatService.OnMessageReceived += ChatService_OnMessageReceived;

            LoadHistory();

            _peerNameChangedHandler = (ip, oldName, newName) =>
            {
                _targetName = newName;

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
                    Title = $"{_targetName} ile DM";
                    Messages.Clear();
                    LoadHistory();
                });
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
                // Yukarıdaki sıralama değişikliğiyle teorik olarak aynı mesajın
                // hem event üzerinden hem history üzerinden eklenmesi ihtimaline
                // karşı basit bir güvenlik: zaten listede olan MessageId'yi atla.
                if (!Messages.Any(m => m.MessageId == msg.MessageId))
                {
                    Messages.Add(msg);
                }
            }

            if (Messages.Count > 0)
            {
                MessagesListView.ScrollTo(Messages.Count - 1);
            }
        }

        private void ChatService_OnMessageReceived(ChatMessage msg)
        {
            if (msg.SenderName == _targetName)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (!Messages.Any(m => m.MessageId == msg.MessageId))
                    {
                        Messages.Add(msg);
                        if (Messages.Count > 0)
                        {
                            MessagesListView.ScrollTo(Messages.Count - 1);
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

            try
            {
                // NOT: ChatService.SendMessageAsync artık başarısızlık durumunda
                // exception'ı YUTMUYOR, yeniden fırlatıyor (bkz. ChatService.cs).
                // Önceki sürümde hata burada asla yakalanmıyordu ve mesaj,
                // gerçekte gönderilmemiş olsa bile aşağıdaki "başarılı gönderim"
                // kodu çalışıp ekrana ekleniyordu. Artık catch bloğu gerçekten
                // devreye giriyor ve kullanıcı doğru şekilde bilgilendiriliyor.
                await App.ChatService.SendMessageAsync(currentIp, text);

                var myMsg = new ChatMessage
                {
                    MessageId = Guid.NewGuid().ToString(),
                    SenderName = App.CurrentUsername,
                    TargetName = _targetName,
                    Content = text,
                    MessageType = "Text",
                    Timestamp = DateTime.Now,
                    IsMe = true
                };

                Messages.Add(myMsg);
                ChatService.GlobalMessages.Add(myMsg);

                if (Messages.Count > 0)
                {
                    MessagesListView.ScrollTo(Messages.Count - 1);
                }
            }
            catch (Exception ex)
            {
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

                if (btn != null) btn.Text = "⏳";

                await App.ChatService.SendMediaAsync(currentIp, result.FullPath, mediaType);

                if (btn != null) btn.Text = "📎";

                var myMsg = new ChatMessage
                {
                    MessageId = Guid.NewGuid().ToString(),
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
                if (Messages.Count > 0)
                {
                    MessagesListView.ScrollTo(Messages.Count - 1);
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Hata", "Medya gönderilemedi: " + ex.Message, "Tamam");
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
