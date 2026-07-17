using P2PFil.ChatModule;
using P2PFil.Services;
using System;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;

namespace P2PFil
{
    public partial class App : Application
    {
        public static NetworkService NetworkService { get; } = new NetworkService();
        public static ChatService ChatService { get; } = new ChatService();
        public static int CurrentTabIndex { get; set; } = 0; // Başlangıç sayfasını 0 (MainPage) kabul ediyoruz

        // Kullanıcı adını artık SettingsService üzerinden yönetiyoruz (Profil sayfasındaki değişiklikler anında yansısın diye)
        public static string CurrentUsername
        {
            get => SettingsService.Username;
            set => SettingsService.Username = value;
        }

        public static string DeviceId { get; private set; } = "";

        public App()
        {
            // Global hata yakalama (Uygulamanın beklenmedik şekilde çökmesini engeller)
            AppDomain.CurrentDomain.UnhandledException += (s, e) => {
                System.Diagnostics.Debug.WriteLine("KRİTİK HATA: " + e.ExceptionObject.ToString());
            };

            TaskScheduler.UnobservedTaskException += (s, e) => {
                System.Diagnostics.Debug.WriteLine("ASENKRON HATA: " + e.Exception.InnerException?.Message);
                e.SetObserved();
            };

            InitializeComponent();
            MainPage = new AppShell();

            // Kurucu metot asenkron olamayacağı için asenkron işlemleri ayrı bir metoda devrediyoruz
            InitializeAppAsync();
        }

        private async void InitializeAppAsync()
        {
            // 1. ÖNCE cihaz kimliğini cihaz deposundan (SecureStorage) güvenle alıyoruz
            DeviceId = await SettingsService.GetDeviceIdAsync();
            System.Diagnostics.Debug.WriteLine($"DeviceId = {DeviceId}");

            // 2. Oturum yöneticisini başlat
            _ = SessionManager.Instance;

            // 3. Servisleri DeviceId belli OLDUKTAN SONRA başlatıyoruz (Önemli)
            NetworkService.StartDiscovery();
            ChatService.StartListening();

            // YENİ: Kayıtlı Performans Modu tercihi, kullanıcı ProfilePage'i hiç
            // açmasa bile uygulama açılışında TransferManager'a uygulanır.
            // (ProfilePage.xaml.cs'deki aynı anahtar/varsayılan ile birebir uyumlu.)
            bool performanceModeEnabled = Preferences.Get("PerformanceMode", false);
            TransferManager.Instance.SetMaxConcurrency(performanceModeEnabled ? 1 : 3);

            // 4. Farklı IP veya cihazlardan mesaj gelince tetiklenen güvenli UI mantığı
            ChatService.OnMessageReceived += (msg) =>
            {
                // UI Thread kilitlemesini engellemek ve arka plan thread çökmelerini önlemek için tamamen MainThread'e alıyoruz
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    try
                    {
                        string senderDeviceId = NetworkService.GetDeviceIdByName(msg.SenderName);
                        if (string.IsNullOrEmpty(senderDeviceId)) return;

                        var currentPage = Shell.Current?.CurrentPage as ChatPage;
                        bool isAlreadyInChat = (currentPage != null && currentPage.TargetDeviceId == senderDeviceId);

                        // YENİ: Kullanıcı Profil sekmesinden bildirimleri kapattıysa
                        // popup hiç gösterilmez.
                        if (!SettingsService.MessageNotificationsEnabled) return;

                        // Eğer kullanıcı zaten o kişiyle sohbet ekranında değilse bildirim penceresi göster
                        if (!isAlreadyInChat && Shell.Current != null)
                        {
                            bool git = await Shell.Current.DisplayAlert(
                                "Yeni Mesaj!",
                                $"{msg.SenderName}: {msg.Content}",
                                "Sohbete Git",
                                "Kapat"
                            );

                            if (git)
                            {
                                await Shell.Current.Navigation.PopToRootAsync();
                                await Task.Delay(100);
                                await Shell.Current.Navigation.PushAsync(new ChatPage(senderDeviceId, msg.SenderName));
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Mesaj Alımında UI Güncelleme Hatası: {ex.Message}");
                    }
                });
            };
        }
    }
}