using P2PFil.ChatModule;
using P2PFil.Services;
using System;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;

namespace P2PFil;

public partial class App : Application
{
    public static NetworkService NetworkService { get; } = new NetworkService();
    public static ChatService ChatService { get; } = new ChatService();
    public static string CurrentUsername { get; set; } = "Sen";
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

        DeviceId = DeviceIdentity.GetDeviceId();
        System.Diagnostics.Debug.WriteLine($"DeviceId = {DeviceId}");

        InitializeComponent();

        // Oturum yöneticisini başlat
        _ = SessionManager.Instance;

        NetworkService.StartDiscovery();
        ChatService.StartListening();

        // Farklı IP veya cihazlardan mesaj gelince tetiklenen güvenli UI mantığı
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

        MainPage = new AppShell();
    }
}