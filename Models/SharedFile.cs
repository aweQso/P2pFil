using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace P2PFil.Models
{
    public class SharedFile : INotifyPropertyChanged
    {
        private string _fileName = string.Empty; //[cite: 17]
        private string _ownerName = string.Empty; //[cite: 17]
        private string _senderId = string.Empty; // FilesPage IP takibi için eklendi[cite: 17]
        private string _fileSize = "0 MB"; //[cite: 17]
        private DateTime _uploadDate; //[cite: 17]
        private double _progress = 0; //[cite: 17]
        private bool _isDownloading = false; //[cite: 17]
        private string _statusMessage = "İndir"; //[cite: 17]

        public string FileName
        {
            get => _fileName; //[cite: 17]
            set { _fileName = value; OnPropertyChanged(); } //[cite: 17]
        }

        public string OwnerName
        {
            get => _ownerName; //[cite: 17]
            set { _ownerName = value; OnPropertyChanged(); } //[cite: 17]
        }

        public string SenderId
        {
            get => _senderId; //[cite: 17]
            set { _senderId = value; OnPropertyChanged(); } //[cite: 17]
        }

        public string FileSize
        {
            get => _fileSize; //[cite: 17]
            set { _fileSize = value; OnPropertyChanged(); } //[cite: 17]
        }

        public DateTime UploadDate
        {
            get => _uploadDate; //[cite: 17]
            set { _uploadDate = value; OnPropertyChanged(); } //[cite: 17]
        }

        public double Progress
        {
            get => _progress; //[cite: 17]
            set { _progress = value; OnPropertyChanged(); } //[cite: 17]
        }

        public bool IsDownloading
        {
            get => _isDownloading; //[cite: 17]
            set { _isDownloading = value; OnPropertyChanged(); } //[cite: 17]
        }

        public string StatusMessage
        {
            get => _statusMessage; //[cite: 17]
            set { _statusMessage = value; OnPropertyChanged(); } //[cite: 17]
        }

        public event PropertyChangedEventHandler? PropertyChanged; //[cite: 17]
        protected void OnPropertyChanged([CallerMemberName] string propertyName = "") //[cite: 17]
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName)); //[cite: 17]
        }
    }
}