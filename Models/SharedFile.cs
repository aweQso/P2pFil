using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace P2PFil.Models
{
    public class SharedFile : INotifyPropertyChanged
    {
        public string FileName { get; set; } = string.Empty;
        public string OwnerName { get; set; } = string.Empty;
        public string FileSize { get; set; } = string.Empty;
        public string FileType { get; set; } = string.Empty;
        public string SenderId { get; set; } = string.Empty;
        public DateTime UploadDate { get; set; } = DateTime.Now;

        // İndirme özellikleri
        private double _progress;
        public double Progress { get => _progress; set { _progress = value; OnPropertyChanged(); } }

        private bool _isDownloading;
        public bool IsDownloading { get => _isDownloading; set { _isDownloading = value; OnPropertyChanged(); } }

        private string _statusMessage = "";
        public string StatusMessage { get => _statusMessage; set { _statusMessage = value; OnPropertyChanged(); } }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}