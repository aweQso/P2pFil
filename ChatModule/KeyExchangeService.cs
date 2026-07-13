using System;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace P2PFil.ChatModule
{
    public class KeyExchangeService : IDisposable
    {
        private readonly ECDiffieHellman _ecdh;

        public KeyExchangeService()
        {
            _ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        }

        public async Task<(byte[] Key, string Fingerprint)> PerformKeyExchangeAsync(NetworkStream stream, CancellationToken ct)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));

            byte[] myPublicKey = _ecdh.ExportSubjectPublicKeyInfo();
            byte[] myLenBytes = BitConverter.GetBytes(myPublicKey.Length);

            await stream.WriteAsync(myLenBytes, 0, myLenBytes.Length, ct);
            await stream.WriteAsync(myPublicKey, 0, myPublicKey.Length, ct);
            await stream.FlushAsync(ct);

            byte[] remoteLenBytes = new byte[4];
            int bytesRead = 0;
            while (bytesRead < 4)
            {
                int read = await stream.ReadAsync(remoteLenBytes, bytesRead, 4 - bytesRead, ct);
                if (read == 0) throw new CryptographicException("Anahtar değişimi başarısız: Uzak uç bağlantıyı kapattı.");
                bytesRead += read;
            }
            int remoteLen = BitConverter.ToInt32(remoteLenBytes, 0);

            if (remoteLen <= 0 || remoteLen > 4096)
                throw new CryptographicException("Geçersiz uzak ortak anahtar boyutu tespit edildi.");

            byte[] remotePublicKey = new byte[remoteLen];
            int totalRead = 0;
            while (totalRead < remoteLen)
            {
                int read = await stream.ReadAsync(remotePublicKey, totalRead, remoteLen - totalRead, ct);
                if (read == 0) throw new CryptographicException("Anahtar değişimi başarısız: Uzak ortak anahtar verisi eksik.");
                totalRead += read;
            }

            string fingerprint = Convert.ToBase64String(SHA256.HashData(remotePublicKey));

            using var remoteEcdh = ECDiffieHellman.Create();
            remoteEcdh.ImportSubjectPublicKeyInfo(remotePublicKey, out _);

            byte[] derivedKey = _ecdh.DeriveKeyFromHash(remoteEcdh.PublicKey, HashAlgorithmName.SHA256);

            return (derivedKey, fingerprint);
        }

        public void Dispose()
        {
            _ecdh?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}