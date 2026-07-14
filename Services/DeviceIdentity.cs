using System;
using System.IO;

namespace P2PFil.Services
{
    public static class DeviceIdentity
    {
        private static readonly string DeviceIdPath =
            Path.Combine(FileSystem.AppDataDirectory, "device.id");

        public static string GetDeviceId()
        {
            try
            {
                if (File.Exists(DeviceIdPath))
                {
                    string id = File.ReadAllText(DeviceIdPath).Trim();

                    if (!string.IsNullOrWhiteSpace(id))
                        return id;
                }

                string newId = Guid.NewGuid().ToString("N");

                File.WriteAllText(DeviceIdPath, newId);

                return newId;
            }
            catch (Exception ex)
            {
                // DÜZELTME: Önceki sürümde bu fallback tamamen sessizdi.
                // Dosya yazımı başarısız olduğunda (izin sorunu, salt-okunur
                // depolama vb.) DeviceId hiçbir yere kalıcılaştırılmadan
                // döndürülüyor; bu da uygulama her açıldığında cihazın YENİ
                // bir kimlikle görünmesi (ve dolayısıyla PeerTrustStore'daki
                // TOFU güven zincirinin sessizce kırılması) anlamına geliyordu.
                // Artık en azından debug log'a düşüyor, böylece "neden sürekli
                // yeniden güven onayı isteniyor" sorusu teşhis edilebilir.
                System.Diagnostics.Debug.WriteLine(
                    $"KRİTİK: DeviceId kalıcılaştırılamadı, geçici (kalıcı olmayan) bir kimlik kullanılıyor: {ex.Message}");
                return Guid.NewGuid().ToString("N");
            }
        }
    }
}
