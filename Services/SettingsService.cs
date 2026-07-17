using Microsoft.Maui.Storage;
using System;

namespace P2PFil.Services
{
    public static class SettingsService
    {
        // İsim değiştiğinde diğer sayfaları bilgilendirmek için event
        public static event Action? UsernameChanged;

        public static string Username
        {
            get => Preferences.Get(nameof(Username), "Anonim Kullanıcı");
            set
            {
                Preferences.Set(nameof(Username), value);
                UsernameChanged?.Invoke(); // Değişimi tetikle
            }
        }
        // Diğer sayfaların anlık güncellenmesi için Event
        public static event Action? ProfileImageChanged;

        public static string ProfileImagePath
        {
            get => Preferences.Get(nameof(ProfileImagePath), "default_profile.png");
            set
            {
                Preferences.Set(nameof(ProfileImagePath), value);
                ProfileImageChanged?.Invoke(); // Resim değiştiğinde tetikle
            }
        }

        public static string DownloadFolder
        {
            get => Preferences.Get(nameof(DownloadFolder), FileSystem.AppDataDirectory);
            set => Preferences.Set(nameof(DownloadFolder), value);
        }

        // YENİ: Yeni mesaj geldiğinde ekranda çıkan bildirim penceresini
        // (DisplayAlert) açıp kapatmak için ayar. Varsayılan: açık (true).
        public static bool MessageNotificationsEnabled
        {
            get => Preferences.Get(nameof(MessageNotificationsEnabled), true);
            set => Preferences.Set(nameof(MessageNotificationsEnabled), value);
        }

        public static async Task<string> GetDeviceIdAsync()
        {
            var deviceId = await SecureStorage.GetAsync("DeviceId");
            if (string.IsNullOrEmpty(deviceId))
            {
                deviceId = Guid.NewGuid().ToString();
                await SecureStorage.SetAsync("DeviceId", deviceId);
            }
            return deviceId;
        }
    }
}