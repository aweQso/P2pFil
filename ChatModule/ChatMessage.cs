using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace P2PFil.ChatModule
{
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

        public event PropertyChangedEventHandler? PropertyChanged; //[cite: 15]
        protected void OnPropertyChanged([CallerMemberName] string propertyName = "") //[cite: 15]
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName)); //[cite: 15]
        }
    }
}