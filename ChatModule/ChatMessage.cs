using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace P2PFil.ChatModule
{
    public enum MessageStatus
    {
        Sending,
        Sent,
        Failed
    }

    public class ChatMessage : INotifyPropertyChanged
    {
        private string _senderName = string.Empty; //[cite: 15]
        private string _senderIp = string.Empty; //[cite: 15]
        private string _targetName = string.Empty; //[cite: 15]
        private string _content = string.Empty; //[cite: 15]
        private string _messageType = "Text"; //[cite: 15]
        private string _encryptedBase64Media = string.Empty; //[cite: 15]
        private string _localMediaPath = string.Empty; //[cite: 15]
        private DateTime _timestamp; //[cite: 15]
        private bool _isMe; //[cite: 15]
        private string _messageId = Guid.NewGuid().ToString(); // Replay Attack koruması için[cite: 15]

        public string MessageId
        {
            get => _messageId; //[cite: 15]
            set { _messageId = value; OnPropertyChanged(); } //[cite: 15]
        }

        public string SenderName
        {
            get => _senderName; //[cite: 15]
            set { _senderName = value; OnPropertyChanged(); } //[cite: 15]
        }

        public string SenderIp
        {
            get => _senderIp; //[cite: 15]
            set { _senderIp = value; OnPropertyChanged(); } //[cite: 15]
        }

        public string TargetName
        {
            get => _targetName; //[cite: 15]
            set { _targetName = value; OnPropertyChanged(); } //[cite: 15]
        }

        public string Content
        {
            get => _content; //[cite: 15]
            set { _content = value; OnPropertyChanged(); } //[cite: 15]
        }

        public string MessageType
        {
            get => _messageType; //[cite: 15]
            set { _messageType = value; OnPropertyChanged(); } //[cite: 15]
        }

        public string EncryptedBase64Media
        {
            get => _encryptedBase64Media; //[cite: 15]
            set { _encryptedBase64Media = value; OnPropertyChanged(); } //[cite: 15]
        }

        public string LocalMediaPath
        {
            get => _localMediaPath; //[cite: 15]
            set { _localMediaPath = value; OnPropertyChanged(); } //[cite: 15]
        }

        public DateTime Timestamp
        {
            get => _timestamp; //[cite: 15]
            set { _timestamp = value; OnPropertyChanged(); } //[cite: 15]
        }

        public bool IsMe
        {
            get => _isMe; //[cite: 15]
            set { _isMe = value; OnPropertyChanged(); } //[cite: 15]
        }

        public bool IsImage => MessageType == "Image"; //[cite: 15]
        public bool IsVideo => MessageType == "Video"; //[cite: 15]
        public bool IsText => MessageType == "Text"; //[cite: 15]

        // YENİ: gönderim durumu (yalnızca IsMe == true olan mesajlarda anlamlı)
        private MessageStatus _status = MessageStatus.Sent;
        public MessageStatus Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusIcon)); }
        }

        // YENİ: XAML'de saat yanında gösterilecek küçük durum ikonu
        public string StatusIcon => Status switch
        {
            MessageStatus.Sending => "🕓",
            MessageStatus.Sent => "✓",
            MessageStatus.Failed => "⚠",
            _ => string.Empty
        };

        // YENİ: bu mesajın üstünde gün ayırıcı gösterilsin mi
        // (ChatPage.AppendWithDateSeparator tarafından, listeye eklenirken set edilir)
        private bool _showDateSeparator;
        public bool ShowDateSeparator
        {
            get => _showDateSeparator;
            set { _showDateSeparator = value; OnPropertyChanged(); }
        }

        public string DateSeparatorText => Timestamp.Date == DateTime.Today
            ? "Bugün"
            : Timestamp.Date == DateTime.Today.AddDays(-1)
                ? "Dün"
                : Timestamp.ToString("dd MMMM yyyy");

        public event PropertyChangedEventHandler? PropertyChanged; //[cite: 15]
        protected void OnPropertyChanged([CallerMemberName] string propertyName = "") //[cite: 15]
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName)); //[cite: 15]
        }
    }
}