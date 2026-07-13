using P2PFil.ChatModule;
using P2PFil.Services;

namespace P2PFil;

public partial class App : Application
{
    public static NetworkService NetworkService { get; } = new NetworkService();
    public static ChatService ChatService { get; } = new ChatService();
    public static string CurrentUsername { get; set; } = "Sen";

    public App()
    {
        AppDomain.CurrentDomain.UnhandledException += (s, e) => {
            System.Diagnostics.Debug.WriteLine("KRİTİK HATA: " + e.ExceptionObject.ToString());
        };

        TaskScheduler.UnobservedTaskException += (s, e) => {
            System.Diagnostics.Debug.WriteLine("ASENKRON HATA: " + e.Exception.InnerException?.Message);
            e.SetObserved();
        };

        InitializeComponent();

        NetworkService.StartDiscovery();
        ChatService.StartListening();

        ChatService.OnMessageReceived += (msg) =>
        {
            // DÜZELTME 1: Gelen mesajın IP'sini buluyoruz
            string senderIp = NetworkService.GetIpByName(msg.SenderName);
            var currentPage = Shell.Current?.CurrentPage as ChatPage;

            // DÜZELTME 2: İsim yerine IP kontrolü yapıyoruz! Zaten sohbetteysek bildirim gösterme.
            if (currentPage != null && currentPage.TargetIp == senderIp)
            {
                return;
            }

            // Eğer başka sayfadaysak veya sohbette değilsek bildirim göster
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                if (Shell.Current != null)
                {
                    bool git = await Shell.Current.DisplayAlert(
                        "Yeni Mesaj!",
                        $"{msg.SenderName}: {msg.Content}",
                        "Sohbete Git",
                        "Kapat"
                    );

                    if (git)
                    {
                        if (!string.IsNullOrEmpty(senderIp))
                        {
                            await Shell.Current.Navigation.PopToRootAsync();
                            await Task.Delay(100);
                            await Shell.Current.Navigation.PushAsync(new ChatPage(senderIp, msg.SenderName));
                        }
                    }
                }
            });
        };

        MainPage = new AppShell();
    }
}