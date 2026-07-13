using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace P2PFil.ChatModule
{
    public enum TrustResult
    {
        NewPeerTrusted, // İlk bağlantı, fingerprint kaydedildi (TOFU)
        Matches,        // Daha önce kaydedilen fingerprint ile eşleşiyor
        Mismatch        // UYARI: Kayıtlı fingerprint ile eşleşmiyor - olası MITM
    }

    // GÜVENLİK DÜZELTMESİ (MITM Koruması): Trust-On-First-Use (TOFU) tabanlı basit
    // bir eş kimlik doğrulama katmanı. SSH'ın known_hosts dosyasına benzer şekilde
    // çalışır: bir eşle ilk kez konuşulduğunda ECDH public key fingerprint'i kalıcı
    // olarak kaydedilir; sonraki bağlantılarda fingerprint değişmişse (ör. aktif bir
    // MITM saldırganı araya girdiyse) bağlantı reddedilir.
    //
    // ÖNEMLİ SINIRLAMA: TOFU, bir eşle yapılan İLK bağlantının kendisini MITM'e karşı
    // koruyamaz (klasik TOFU sınırlaması - tıpkı SSH'de olduğu gibi). Tam koruma için
    // fingerprint'in ilk bağlantıda kullanıcılar tarafından ayrı bir kanaldan (yüz
    // yüze, telefonla vb.) karşılaştırılması gerekir. Bu sınıf en azından TEKRAR
    // EDEN/AKTİF MITM'i ve oturum ele geçirmeyi tespit edip engeller; sıfırdan tam
    // koruma sağlamaz.
    public sealed class PeerTrustStore
    {
        private static readonly Lazy<PeerTrustStore> _instance = new(() => new PeerTrustStore());
        public static PeerTrustStore Instance => _instance.Value;

        private readonly ConcurrentDictionary<string, string> _knownFingerprints = new();
        private readonly string _storePath;
        private readonly object _fileLock = new();

        private PeerTrustStore()
        {
            try
            {
                _storePath = Path.Combine(FileSystem.AppDataDirectory, "trusted_peers.json");
                Load();
            }
            catch
            {
                _storePath = string.Empty; // Kalıcı depolama kullanılamıyorsa bellek-içi devam et
            }
        }

        private void Load()
        {
            if (string.IsNullOrEmpty(_storePath) || !File.Exists(_storePath)) return;
            try
            {
                string json = File.ReadAllText(_storePath);
                var data = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (data != null)
                {
                    foreach (var kvp in data)
                        _knownFingerprints[kvp.Key] = kvp.Value;
                }
            }
            catch
            {
                // Bozuk/okunamayan dosya - sıfırdan başla
            }
        }

        private void Save()
        {
            if (string.IsNullOrEmpty(_storePath)) return;
            try
            {
                lock (_fileLock)
                {
                    var snapshot = new Dictionary<string, string>(_knownFingerprints);
                    File.WriteAllText(_storePath, JsonSerializer.Serialize(snapshot));
                }
            }
            catch
            {
                // Kalıcı kaydetme başarısız olsa da uygulama çalışmaya devam etmeli
            }
        }

        // peerKey: genellikle uzak IP adresi. fingerprint: KeyExchangeService'ten dönen SHA256 fingerprint.
        public TrustResult VerifyOrTrust(string peerKey, string fingerprint)
        {
            if (string.IsNullOrEmpty(peerKey) || string.IsNullOrEmpty(fingerprint))
                return TrustResult.Mismatch;

            if (_knownFingerprints.TryGetValue(peerKey, out var known))
            {
                return known == fingerprint ? TrustResult.Matches : TrustResult.Mismatch;
            }

            _knownFingerprints[peerKey] = fingerprint;
            Save();
            return TrustResult.NewPeerTrusted;
        }

        // Bir eşin kaydını sil (ör. kullanıcı "bu cihazı unut" derse ya da IP
        // yeniden atandığında kayıt elle temizlenmek istenirse).
        public void Forget(string peerKey)
        {
            if (_knownFingerprints.TryRemove(peerKey, out _))
                Save();
        }
    }
}
