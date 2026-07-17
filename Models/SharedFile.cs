using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;

namespace P2PFil.Models
{
    // YENİ: BaglantiBekleniyor -- karşı taraftan kaynaklı bir kopma (timeout,
    // socket hatası vb.) sonrası otomatik yeniden bağlanma denemesi sürerken
    // kullanılan ARA durum. Duraklatildi'den FARKLIDIR: Duraklatildi kullanıcının
    // (ya da uygulamanın kapanmasının) sebep olduğu, elle "Devam Et"e basılması
    // gereken durumu ifade eder. BaglantiBekleniyor'da hiçbir kullanıcı etkileşimi
    // gerekmez -- sistem arka planda kendi kendine tekrar dener, bağlantı
    // kurulduğu an indirmeye otomatik devam eder.
    public enum DownloadState { Bekliyor, Indiriliyor, Duraklatildi, Tamamlandi, IptalEdildi, Hata, BaglantiBekleniyor }

    public class SharedFile : INotifyPropertyChanged
    {
        private string _fileName = string.Empty;
        private string _ownerName = string.Empty;
        // DÜZELTME: SenderId artık KALICI DeviceId'yi tutuyor (IP değil) --
        // karşı taraf reconnect olup IP değiştirdiğinde dosya kartının
        // "aynı dosya" olarak tanınmaya devam etmesi için (bkz. FilesPage
        // NetworkService_FilesReceived). Gerçek bağlantı için kullanılacak,
        // her discovery paketinde tazelenen GÜNCEL IP ise CurrentIp'te tutulur.
        private string _senderId = string.Empty;
        private string _currentIp = string.Empty;
        private string _fileSize = "0 MB";
        private DateTime _uploadDate;

        // Yeni: Gerçek dosya yolu referansı
        private string _originalPath = string.Empty;

        // İndirme Metrikleri
        private DownloadState _state = DownloadState.Bekliyor;
        private double _progress = 0;
        private string _speedText = "0 MB/s";
        private string _etaText = "--:-- kaldı";
        private string _downloadedSizeText = "";
        private string _statusMessage = "İndir";

        // Kuyruk Kontrolü
        public CancellationTokenSource? DownloadCts { get; set; }

        public string FileName { get => _fileName; set { _fileName = value; OnPropertyChanged(); } }
        public string OwnerName { get => _ownerName; set { _ownerName = value; OnPropertyChanged(); } }
        public string SenderId { get => _senderId; set { _senderId = value; OnPropertyChanged(); } }
        public string CurrentIp { get => _currentIp; set { _currentIp = value; OnPropertyChanged(); } }
        public string FileSize { get => _fileSize; set { _fileSize = value; OnPropertyChanged(); } }
        public DateTime UploadDate { get => _uploadDate; set { _uploadDate = value; OnPropertyChanged(); } }
        public string OriginalPath { get => _originalPath; set { _originalPath = value; OnPropertyChanged(); } }

        public DownloadState State
        {
            get => _state;
            set
            {
                _state = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsDownloading));
                OnPropertyChanged(nameof(IsPaused));
                OnPropertyChanged(nameof(IsAutoRetrying));
                OnPropertyChanged(nameof(IsActiveOrRetrying));
                OnPropertyChanged(nameof(CanStartOrResume));
                OnPropertyChanged(nameof(ActionButtonText));
            }
        }

        public bool IsDownloading => State == DownloadState.Indiriliyor;
        public bool IsPaused => State == DownloadState.Duraklatildi;

        // YENİ: BaglantiBekleniyor durumundayken sistem arka planda otomatik
        // tekrar deniyor -- bu da "aktif çalışıyor" sayılır (İptal butonu ve
        // ilerleme paneli bu duruma göre görünür kalmalı, bkz. IsAutoRetrying).
        public bool IsAutoRetrying => State == DownloadState.BaglantiBekleniyor;

        // YENİ: XAML'deki "indirme kontrolleri" paneli (ilerleme çubuğu + İptal
        // butonu) hem gerçek indirme sırasında HEM DE otomatik yeniden bağlanma
        // denemesi sürerken (BaglantiBekleniyor) görünür kalmalı -- kart
        // "kapanmamalı", kullanıcı her zaman İptal edebilmeli. Bkz. FilePage.xaml
        // IsVisible="{Binding IsActiveOrRetrying}".
        public bool IsActiveOrRetrying => IsDownloading || IsAutoRetrying;

        // YENİ: "İndir/Devam Et" butonunun görünür + tıklanabilir olması gereken
        // TÜM durumlar. BaglantiBekleniyor KASITLI OLARAK burada YOK -- o
        // durumdayken kullanıcının hiçbir şeye basmasına gerek yok, sistem kendi
        // kendine bağlantıyı yakalayınca otomatik devam ediyor. Duraklatildi ve
        // Hata burada olmazsa, o duruma düşen bir dosya için buton XAML tarafında
        // gizli/pasif kalır ve kullanıcı resume'u tetikleyemez.
        public bool CanStartOrResume =>
            State is DownloadState.Bekliyor
                or DownloadState.Duraklatildi
                or DownloadState.Hata
                or DownloadState.IptalEdildi;

        // YENİ: Buton metnini state'e göre otomatik değiştirir; kullanıcı
        // "İndir" ile "Devam Et" arasındaki farkı görsel olarak ayırt edebilir.
        public string ActionButtonText => State switch
        {
            DownloadState.Duraklatildi => "Devam Et",
            DownloadState.Hata => "Tekrar Dene",
            DownloadState.IptalEdildi => "Tekrar Başlat",
            _ => "İndir"
        };

        public double Progress { get => _progress; set { _progress = value; OnPropertyChanged(); } }
        public string SpeedText { get => _speedText; set { _speedText = value; OnPropertyChanged(); } }
        public string EtaText { get => _etaText; set { _etaText = value; OnPropertyChanged(); } }
        public string DownloadedSizeText { get => _downloadedSizeText; set { _downloadedSizeText = value; OnPropertyChanged(); } }
        public string StatusMessage { get => _statusMessage; set { _statusMessage = value; OnPropertyChanged(); } }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        private string _ownerProfileImagePath = "default_profile.png";
        public string OwnerProfileImagePath
        {
            get => _ownerProfileImagePath;
            set
            {
                if (_ownerProfileImagePath != value)
                {
                    _ownerProfileImagePath = value;
                    OnPropertyChanged(nameof(OwnerProfileImagePath)); // INotifyPropertyChanged kullanıyorsan
                }
            }
        }
    }
}