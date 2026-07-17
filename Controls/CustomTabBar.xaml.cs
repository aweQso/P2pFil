using System;
using Microsoft.Maui.Controls;

namespace P2PFil.Controls;

public partial class CustomTabBar : ContentView
{
    public CustomTabBar()
    {
        InitializeComponent();
    }

    public void SetActiveIndex(int index)
    {
        // 300px toplam geniþlik / 3 buton = 100px hareket payý
        double targetX = index * 100;

        // Geçiþ süresini 250ms'ye indirdim ki sayfa kaymasýyla senkron olsun
        SelectionIndicator.TranslateTo(targetX, 0, 750, Easing.CubicOut);
    }

    // animate: false ile Shell'in kendi animasyonunu susturuyoruz.
    // Böylece sayfalar arasý geçiþte sadece bizim yazdýðýmýz yumuþak kaydýrma animasyonu çalýþacak.
    private async void OnGoToNetworkClicked(object sender, EventArgs e)
        => await Shell.Current.GoToAsync("///MainPage", animate: false);

    private async void OnGoToFilesClicked(object sender, EventArgs e)
        => await Shell.Current.GoToAsync("///FilesPage", animate: false);

    private async void OnGoToProfileClicked(object sender, EventArgs e)
        => await Shell.Current.GoToAsync("///ProfilePage", animate: false);
}