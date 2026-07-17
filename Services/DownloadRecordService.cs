using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace P2PFil.Services
{
    // YENİ: Yarım kalan (ağ kopması, uygulama kapatılması, vs. yüzünden
    // tamamlanmamış) indirmelerin KALICI kaydını tutar. FileService'teki
    // "shared_files.json" deseniyle aynı yaklaşım -- basit bir JSON dosyası,
    // AppDataDirectory içinde.
    //
    // Neden gerekli: Önceden SharedFile.State / Progress / DownloadCts gibi her
    // şey sadece RAM'de tutuluyordu. Uygulama kapatılıp yeniden açıldığında (ya
    // da FilesPage'den çıkılıp geri gelindiğinde) NetworkService_FilesReceived
    // ağdan gelen dosyaları SIFIRDAN "Bekliyor" state'inde SharedFile nesnesi
    // olarak oluşturuyordu -- diskte %60 inmiş bir .indirilen dosya olsa bile
    // bunu bilen kimse yoktu ve "İndir" butonu her zaman baştan indirmeyi
    // tetikliyordu.
    //
    // Bu servis, bir indirme %'lik ilerleme kaydettikçe (NetworkService
    // içinde) "hangi dosya, kimden, ne kadarı diskte var" bilgisini diske
    // yazar. FilesPage bu kaydı, ağdan gelen HER dosya için TEK TEK (rastgele
    // değil, ilgili SharedFile'ın FileName+SenderDeviceId'sine göre) kontrol
    // eder ve eşleşme varsa o dosyayı otomatik olarak Duraklatildi durumuna
    // getirip Progress'ini doldurur -- böylece buton otomatik "Devam Et" yazar
    // ve kullanıcı bastığında NetworkService zaten var olan
    // "REQ_FILE:{name}:{existingLength}" resume mekanizmasıyla kaldığı yerden
    // devam eder.
    public static class DownloadRecordService
    {
        private static readonly string RecordsPath = Path.Combine(
            Microsoft.Maui.Storage.FileSystem.AppDataDirectory, "download_progress.json");

        private static readonly object _lock = new();

        public class DownloadRecord
        {
            // Dosyayı gönderen cihazın KALICI DeviceId'si (IP değil -- IP'ler
            // ağ yeniden bağlanmalarında değişebilir, DeviceId sabittir).
            public string SenderDeviceId { get; set; } = "";
            public string FileName { get; set; } = "";
            public long DownloadedBytes { get; set; }
            public long TotalBytes { get; set; }
            public string ExpectedHash { get; set; } = "";
            public DateTime LastUpdated { get; set; }
        }

        private static string BuildKey(string senderDeviceId, string fileName) => $"{senderDeviceId}::{fileName}";

        private static List<DownloadRecord> LoadAll()
        {
            if (!File.Exists(RecordsPath)) return new List<DownloadRecord>();
            try
            {
                var json = File.ReadAllText(RecordsPath);
                return JsonSerializer.Deserialize<List<DownloadRecord>>(json) ?? new();
            }
            catch { return new List<DownloadRecord>(); }
        }

        private static void SaveAll(List<DownloadRecord> records)
        {
            try
            {
                File.WriteAllText(RecordsPath, JsonSerializer.Serialize(records));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DownloadRecordService] Kayıt yazılamadı: {ex.Message}");
            }
        }

        // İndirme ilerledikçe (ör. her UI güncellemesinde, ~250ms'de bir)
        // çağrılır. Aşırı sık disk yazımını önlemek isteyen çağıran taraf
        // kendi throttling'ini uygulayabilir; burada ekstra bir sınırlama
        // YOKTUR -- çağıran taraf ne sıklıkta çağırırsa o kadar sık yazılır.
        public static void UpdateProgress(string senderDeviceId, string fileName, long downloadedBytes, long totalBytes, string expectedHash)
        {
            if (string.IsNullOrEmpty(senderDeviceId) || string.IsNullOrEmpty(fileName)) return;

            lock (_lock)
            {
                var records = LoadAll();
                string key = BuildKey(senderDeviceId, fileName);
                var existing = records.FirstOrDefault(r => BuildKey(r.SenderDeviceId, r.FileName) == key);

                if (existing != null)
                {
                    existing.DownloadedBytes = downloadedBytes;
                    existing.TotalBytes = totalBytes;
                    existing.ExpectedHash = expectedHash;
                    existing.LastUpdated = DateTime.Now;
                }
                else
                {
                    records.Add(new DownloadRecord
                    {
                        SenderDeviceId = senderDeviceId,
                        FileName = fileName,
                        DownloadedBytes = downloadedBytes,
                        TotalBytes = totalBytes,
                        ExpectedHash = expectedHash,
                        LastUpdated = DateTime.Now
                    });
                }

                SaveAll(records);
            }
        }

        // İndirme tamamlandığında ya da kullanıcı dosyayı iptal edip diskten
        // sildiğinde çağrılır -- kayıt artık geçersizdir.
        public static void ClearProgress(string senderDeviceId, string fileName)
        {
            if (string.IsNullOrEmpty(senderDeviceId) || string.IsNullOrEmpty(fileName)) return;

            lock (_lock)
            {
                var records = LoadAll();
                string key = BuildKey(senderDeviceId, fileName);
                int removed = records.RemoveAll(r => BuildKey(r.SenderDeviceId, r.FileName) == key);
                if (removed > 0) SaveAll(records);
            }
        }

        // BELİRLİ (rastgele değil) bir dosya + gönderen için kayıtlı ilerleme
        // var mı diye bakar. FilesPage, ağdan gelen HER SharedFile için bunu
        // tek tek çağırır -- böylece sadece o dosyaya ait kayıt eşleşir, başka
        // dosyaların durumu asla karışmaz.
        public static DownloadRecord? FindRecord(string senderDeviceId, string fileName)
        {
            if (string.IsNullOrEmpty(senderDeviceId) || string.IsNullOrEmpty(fileName)) return null;

            lock (_lock)
            {
                var records = LoadAll();
                string key = BuildKey(senderDeviceId, fileName);
                return records.FirstOrDefault(r => BuildKey(r.SenderDeviceId, r.FileName) == key);
            }
        }
    }
}
