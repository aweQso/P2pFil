using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Maui.Storage;

namespace P2PFil.ChatModule
{
    public enum TrustResult
    {
        NewPeerTrusted,
        Matches,
        Mismatch
    }

    public class PeerRecord
    {
        public string Fingerprint { get; set; } = string.Empty;
        public string LastKnownName { get; set; } = string.Empty;
    }

    public sealed class PeerTrustStore
    {
        private static readonly Lazy<PeerTrustStore> _instance = new(() => new PeerTrustStore());
        public static PeerTrustStore Instance => _instance.Value;

        private readonly ConcurrentDictionary<string, PeerRecord> _peers = new();
        private readonly string _storePath;
        private readonly object _fileLock = new();

        private PeerTrustStore()
        {
            _storePath = Path.Combine(FileSystem.AppDataDirectory, "trusted_peers.json");
            Load();
        }

        private void Load()
        {
            if (File.Exists(_storePath))
            {
                try
                {
                    string json = File.ReadAllText(_storePath);
                    var data = JsonSerializer.Deserialize<Dictionary<string, PeerRecord>>(json);
                    if (data != null)
                    {
                        foreach (var kvp in data)
                            _peers[kvp.Key] = kvp.Value;
                    }
                }
                catch (Exception ex)
                {
                    // DÜZELTME: Önceki sürümde bu catch tamamen sessizdi. Dosya
                    // bozuksa (ör. yarım kalmış bir yazma sonrası) TÜM güven
                    // deposu sessizce sıfırlanıyor ve her peer için TOFU penceresi
                    // yeniden açılıyordu. En azından bunu loglayarak sorunun
                    // fark edilmesini sağlıyoruz.
                    System.Diagnostics.Debug.WriteLine($"PeerTrustStore yüklenemedi (dosya bozuk olabilir): {ex.Message}");
                }
            }
        }

        private void Save()
        {
            lock (_fileLock)
            {
                try
                {
                    var snapshot = new Dictionary<string, PeerRecord>(_peers);
                    string json = JsonSerializer.Serialize(snapshot);

                    // DÜZELTME (Atomik Yazma): Önceki sürüm doğrudan
                    // File.WriteAllText(_storePath, ...) kullanıyordu. Yazma
                    // sırasında uygulama çökerse/enerji kesilirse dosya yarım
                    // kalabiliyor ve bir sonraki açılışta Load() bunu sessizce
                    // yutup TÜM güven deposunu sıfırlıyordu (yeniden MITM
                    // penceresi açılması anlamına gelir). Artık temp dosyaya
                    // yazılıp ardından atomik olarak yerine taşınıyor.
                    string tempPath = _storePath + ".tmp";
                    File.WriteAllText(tempPath, json);

                    if (File.Exists(_storePath))
                        File.Replace(tempPath, _storePath, null);
                    else
                        File.Move(tempPath, _storePath);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"PeerTrustStore kaydedilemedi: {ex.Message}");
                }
            }
        }

        public TrustResult VerifyOrTrust(string deviceId, string fingerprint)
        {
            if (string.IsNullOrWhiteSpace(deviceId) || string.IsNullOrWhiteSpace(fingerprint))
                return TrustResult.Mismatch;

            if (_peers.TryGetValue(deviceId, out var record))
            {
                if (string.IsNullOrEmpty(record.Fingerprint))
                {
                    record.Fingerprint = fingerprint;
                    Save();
                    return TrustResult.NewPeerTrusted;
                }
                return record.Fingerprint == fingerprint ? TrustResult.Matches : TrustResult.Mismatch;
            }

            _peers[deviceId] = new PeerRecord { Fingerprint = fingerprint };
            Save();
            return TrustResult.NewPeerTrusted;
        }

        public bool ValidateOrBindName(string username, string deviceId)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(deviceId))
                return false;

            foreach (var kvp in _peers)
            {
                if (kvp.Key != deviceId && kvp.Value.LastKnownName == username)
                    return false;
            }

            if (_peers.TryGetValue(deviceId, out var record))
            {
                if (record.LastKnownName != username)
                {
                    record.LastKnownName = username;
                    Save();
                }
            }
            else
            {
                _peers[deviceId] = new PeerRecord { LastKnownName = username };
                Save();
            }

            return true;
        }

        public void Forget(string deviceId)
        {
            if (_peers.TryRemove(deviceId, out _))
            {
                Save();
            }
        }
    }
}
