using System;
using Microsoft.Maui.ApplicationModel;

namespace P2PFil.Services
{
    // YENİ: Uygulama genelinde merkezi, hafif bir "Messenger" (pub/sub) yapısı.
    //
    // SORUN: Önceden MainPage/FilesPage/ProfilePage kendi profil resmi
    // güncellemelerini SADECE OnAppearing() içinde (App.NetworkService.ProfileImageUpdated
    // ve SettingsService.ProfileImageChanged event'lerine) abone oluyor, OnDisappearing()
    // içinde abonelikten çıkıyordu. Yani sayfa o an ekranda değilse (arka planda /
    // uykuda ise) güncellemeyi kaçırıyordu; kullanıcı geri döndüğünde manuel bir
    // RefreshData() çağrısına güveniliyordu.
    //
    // ÇÖZÜM: Bu sınıf sayfa yaşam döngüsünden TAMAMEN BAĞIMSIZDIR. Sayfalar
    // constructor'larında (sadece bir kez, OnAppearing/OnDisappearing'de değil)
    // abone olabilir; bildirim her zaman MainThread üzerinde teslim edilir, bu
    // yüzden abone olan taraf UI'ı doğrudan güncelleyebilir.
    //
    // Var olan event'lere (NetworkService.ProfileImageUpdated, SettingsService.
    // ProfileImageChanged) DOKUNULMADI; bu sınıf onların ÜZERİNE, sayfa
    // yaşam döngüsünden bağımsız ek bir dağıtım katmanı olarak eklenmiştir.
    public static class ProfileMessenger
    {
        // Uzak bir peer'ın profili (isim/resim) güncellendiğinde: deviceId taşır.
        public static event Action<string>? PeerProfileChanged;

        // Bu cihazın KENDİ profili (isim veya resim) güncellendiğinde.
        public static event Action? LocalProfileChanged;

        public static void PublishPeerProfileChanged(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId)) return;
            MainThread.BeginInvokeOnMainThread(() => PeerProfileChanged?.Invoke(deviceId));
        }

        public static void PublishLocalProfileChanged()
        {
            MainThread.BeginInvokeOnMainThread(() => LocalProfileChanged?.Invoke());
        }
    }
}
