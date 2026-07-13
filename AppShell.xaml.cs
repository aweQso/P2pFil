namespace P2PFil;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        // FilesPage'in rotasını sisteme tanıtıyoruz ki geçişte "Route not found" hatası vermesin
        Routing.RegisterRoute(nameof(FilesPage), typeof(FilesPage));
    }
}