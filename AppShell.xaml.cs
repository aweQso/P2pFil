using P2PFil; // Profil sayfanın bulunduğu klasörün namespace'i buraya gelmeli.

namespace P2PFil;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        Routing.RegisterRoute(nameof(MainPage), typeof(MainPage));
        Routing.RegisterRoute(nameof(FilesPage), typeof(FilesPage));
        Routing.RegisterRoute(nameof(ProfilePage), typeof(ProfilePage)); // using eklendiği için artık tanır.
    }
}