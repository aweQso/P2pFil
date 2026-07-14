using System;
using System.IO;
using System.Security.Cryptography;
using Microsoft.Maui.Storage;

namespace P2PFil.Services
{
    public static class IdentityKeyStore
    {
        private static readonly string KeyPath = Path.Combine(FileSystem.AppDataDirectory, "identity_private.p8");
        private static readonly byte[] CachedPrivateKey;
        private static readonly object LockObj = new();

        static IdentityKeyStore()
        {
            lock (LockObj)
            {
                if (File.Exists(KeyPath))
                {
                    try
                    {
                        CachedPrivateKey = File.ReadAllBytes(KeyPath);
                    }
                    catch
                    {
                        CachedPrivateKey = GenerateAndSaveKey();
                    }
                }
                else
                {
                    CachedPrivateKey = GenerateAndSaveKey();
                }
            }
        }

        private static byte[] GenerateAndSaveKey()
        {
            using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
            byte[] keyBytes = ecdh.ExportPkcs8PrivateKey();
            string temp = KeyPath + ".tmp";
            File.WriteAllBytes(temp, keyBytes);

            if (File.Exists(KeyPath))
                File.Delete(KeyPath);

            File.Move(temp, KeyPath);
            TryRestrictPermissions(KeyPath);
            return keyBytes;
        }

        // SERTLEŞTİRME: Kimlik anahtarı dosyası, mümkün olan platformlarda
        // sadece sahibi tarafından okunabilir/yazılabilir hale getirilir
        // (chmod 600 benzeri). Bu, aynı cihazdaki diğer kullanıcı/uygulamaların
        // dosyayı okumasını zorlaştırır.
        //
        // NOT: Bu, tam bir çözüm DEĞİLDİR. Uzun vadede bu anahtarın
        // Microsoft.Maui.Storage.SecureStorage (Android Keystore / iOS Keychain /
        // Windows DPAPI) üzerine taşınması önerilir. SecureStorage API'si async
        // olduğu için bu, IdentityKeyStore ve onu senkron çağıran tüm noktaların
        // (KeyExchangeService constructor'ı dahil) async'e taşınmasını gerektiren
        // ayrı bir refactor konusudur; kapsam dışı bırakıldı.
        private static void TryRestrictPermissions(string path)
        {
            try
            {
                if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() || OperatingSystem.IsAndroid())
                {
                    File.SetUnixFileMode(path,
                        UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
            }
            catch
            {
                // Platform desteklemiyorsa veya izin verilmiyorsa sessizce geç;
                // bu sertleştirme "best effort" bir katmandır, kritik yol değildir.
            }
        }

        public static ECDiffieHellman GetIdentityKey()
        {
            var ecdh = ECDiffieHellman.Create();
            ecdh.ImportPkcs8PrivateKey(CachedPrivateKey, out _);
            return ecdh;
        }

        // YENİ: Aynı kalıcı kimlik anahtarını (P-256, PKCS8) ECDSA imzalama
        // amacıyla döndürür. KeyExchangeService, ephemeral ECDH public key'ini
        // bu anahtarla imzalayarak "ben gerçekten bu identity fingerprint'e
        // sahip cihazım" iddiasını doğrulanabilir kılar. EC private key'in
        // PKCS8 kodlaması ECDH/ECDSA arasında ayrım yapmadığından aynı ham
        // anahtar materyali güvenle yeniden kullanılabilir.
        public static ECDsa GetSigningKey()
        {
            var ecdsa = ECDsa.Create();
            ecdsa.ImportPkcs8PrivateKey(CachedPrivateKey, out _);
            return ecdsa;
        }
    }
}
