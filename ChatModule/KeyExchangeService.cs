using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using P2PFil.Services;

namespace P2PFil.ChatModule
{
    public class KeyExchangeService : IDisposable
    {
        private static readonly ECCurve ExpectedCurve = ECCurve.NamedCurves.nistP256;

        // Kimlik (identity) anahtarı: SADECE fingerprint/TOFU ve ephemeral anahtarı
        // imzalamak (kimlik doğrulama) için kullanılır. Paylaşılan sırrın
        // türetilmesinde ARTIK kullanılmaz.
        private readonly ECDiffieHellman _identityEcdh;

        public KeyExchangeService()
        {
            _identityEcdh = IdentityKeyStore.GetIdentityKey();
        }

        // DÜZELTME (Forward Secrecy): Önceki sürümde paylaşılan sır, cihazın
        // KALICI kimlik anahtarından (static-static ECDH) türetiliyordu. Bu,
        // iki cihaz arasındaki her oturumda AYNI anahtarın üretilmesine ve
        // identity_private.p8 dosyası ele geçirilirse geçmiş/gelecek TÜM
        // trafiğin çözülebilmesine yol açıyordu.
        //
        // Şimdi: her el sıkışmada YENİ (ephemeral) bir ECDH anahtar çifti
        // üretilir, paylaşılan sır SADECE bu ephemeral anahtarlardan türetilir
        // ve ephemeral private key oturum sonunda `using` ile bellekten silinir.
        // Kimlik doğrulama, ephemeral public key'in kalıcı kimlik anahtarıyla
        // (ECDSA imzası) imzalanmasıyla sağlanır -> MITM hâlâ tespit edilebilir,
        // ama artık forward secrecy de var.
        public async Task<(byte[] Key, string Fingerprint, string DeviceId)> PerformKeyExchangeAsync(NetworkStream stream, CancellationToken ct)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));

            byte[] myIdentityPublicKey = _identityEcdh.ExportSubjectPublicKeyInfo();
            byte[] myDeviceId = Encoding.UTF8.GetBytes(App.DeviceId);

            using var ephemeral = ECDiffieHellman.Create(ExpectedCurve);
            byte[] myEphemeralPublicKey = ephemeral.ExportSubjectPublicKeyInfo();

            byte[] signature;
            using (var signer = IdentityKeyStore.GetSigningKey())
            {
                signature = signer.SignData(myEphemeralPublicKey, HashAlgorithmName.SHA256);
            }

            await FrameIO.WriteFrameAsync(stream, myIdentityPublicKey, ct);
            await FrameIO.WriteFrameAsync(stream, myDeviceId, ct);
            await FrameIO.WriteFrameAsync(stream, myEphemeralPublicKey, ct);
            await FrameIO.WriteFrameAsync(stream, signature, ct);

            byte[] remoteIdentityPublicKey = await FrameIO.ReadFrameAsync(stream, 4096, ct, "Geçersiz kimlik public key");
            byte[] remoteDeviceBytes = await FrameIO.ReadFrameAsync(stream, 256, ct, "Geçersiz DeviceId");
            byte[] remoteEphemeralPublicKey = await FrameIO.ReadFrameAsync(stream, 4096, ct, "Geçersiz ephemeral public key");
            byte[] remoteSignature = await FrameIO.ReadFrameAsync(stream, 256, ct, "Geçersiz imza");

            if (remoteDeviceBytes.Length == 0)
                throw new CryptographicException("Geçersiz DeviceId.");

            string remoteDeviceId = Encoding.UTF8.GetString(remoteDeviceBytes);

            // TOFU fingerprint: kimlik (identity) anahtarı üzerinden hesaplanır,
            // bu değer oturumdan oturuma DEĞİŞMEZ -> PeerTrustStore karşılaştırması
            // sağlıklı çalışmaya devam eder.
            string fingerprint = Convert.ToBase64String(SHA256.HashData(remoteIdentityPublicKey));

            // Karşı tarafın "ben buyum" iddiasını (kimlik public key), ephemeral
            // anahtarını imzalamış olmasından doğrula. İmza tutmuyorsa ya
            // ephemeral anahtar yolda değiştirilmiş (aktif MITM) ya da
            // kimlik anahtarı sahte demektir.
            using (var verifier = ECDsa.Create())
            {
                verifier.ImportSubjectPublicKeyInfo(remoteIdentityPublicKey, out _);
                if (!verifier.VerifyData(remoteEphemeralPublicKey, remoteSignature, HashAlgorithmName.SHA256))
                    throw new CryptographicException("GÜVENLİK UYARISI: Ephemeral anahtar imzası doğrulanamadı. Olası Man-in-the-Middle saldırısı.");
            }

            using var remoteEphemeralEcdh = ECDiffieHellman.Create();
            remoteEphemeralEcdh.ImportSubjectPublicKeyInfo(remoteEphemeralPublicKey, out _);

            var remoteCurveOid = remoteEphemeralEcdh.ExportParameters(false).Curve.Oid?.Value;
            if (string.IsNullOrEmpty(remoteCurveOid) || remoteCurveOid != ExpectedCurve.Oid.Value)
                throw new CryptographicException("Desteklenmeyen eliptik eğri.");

            // Paylaşılan sır SADECE ephemeral anahtarlardan türetiliyor.
            byte[] derivedKey = ephemeral.DeriveKeyFromHash(remoteEphemeralEcdh.PublicKey, HashAlgorithmName.SHA256);
            return (derivedKey, fingerprint, remoteDeviceId);
        }

        public void Dispose()
        {
            _identityEcdh.Dispose();
            GC.SuppressFinalize(this);
        }

        // --- MERKEZİLEŞTİRİLMİŞ OTURUM YÖNETİMİ ---
        public static async Task<byte[]> NegotiateSessionAsync(NetworkStream stream, IPAddress remoteIp, bool isClient, CancellationToken ct)
        {
            if (isClient)
            {
                // DÜZELTME (Protokol Bug'ı): Önceki sürümde, client cache'li bir
                // anahtara sahipken server'da session bulunmuyorsa, client
                // fallback yolunda "0x00" flag'ini İKİNCİ KEZ stream'e yazıyordu.
                // Bu fazladan byte, akıştaki tüm sonraki okumaları 1 byte
                // kaydırıyor ve el sıkışma "Geçersiz public key" hatasıyla
                // çöküyordu. Artık flag TAM OLARAK BİR KEZ yazılıyor.
                bool hasCached = SessionManager.Instance.TryGetSessionKey(remoteIp, out byte[] existingKey);

                await FrameIO.WriteByteAsync(stream, (byte)(hasCached ? 0x01 : 0x00), ct);
                await stream.FlushAsync(ct);

                if (hasCached)
                {
                    int response = await FrameIO.ReadByteOrEofAsync(stream, ct);
                    if (response == 0x01) return existingKey;
                    // Server session'ı düşürmüş -> tam el sıkışmaya devam et.
                    // (Burada İKİNCİ bir flag byte'ı YAZILMAZ.)
                }

                using var keyExchange = new KeyExchangeService();
                var (key, fingerprint, deviceId) = await keyExchange.PerformKeyExchangeAsync(stream, ct);

                VerifyTrustOrThrow(deviceId, remoteIp, fingerprint);
                SessionManager.Instance.CreateOrUpdateSession(remoteIp, key, fingerprint);
                return key;
            }
            else
            {
                int flag = await FrameIO.ReadByteOrEofAsync(stream, ct);
                if (flag == 0x01)
                {
                    if (SessionManager.Instance.TryGetSessionKey(remoteIp, out byte[] existingKey))
                    {
                        await FrameIO.WriteByteAsync(stream, 0x01, ct);
                        await stream.FlushAsync(ct);
                        return existingKey;
                    }
                    else
                    {
                        await FrameIO.WriteByteAsync(stream, 0x00, ct);
                        await stream.FlushAsync(ct);
                    }
                }
                using var keyExchange = new KeyExchangeService();
                var (key, fingerprint, deviceId) = await keyExchange.PerformKeyExchangeAsync(stream, ct);

                VerifyTrustOrThrow(deviceId, remoteIp, fingerprint);
                SessionManager.Instance.CreateOrUpdateSession(remoteIp, key, fingerprint);
                return key;
            }
        }

        private static void VerifyTrustOrThrow(string deviceId, IPAddress remoteIp, string fingerprint)
        {
            var result = PeerTrustStore.Instance.VerifyOrTrust(deviceId, fingerprint);
            if (result == TrustResult.Mismatch)
            {
                SessionManager.Instance.RemoveSession(remoteIp);
                throw new CryptographicException($"GÜVENLİK UYARISI: {deviceId} kimliğine ait fingerprint değişti. Olası Man-in-the-Middle saldırısı.");
            }
        }
    }
}
