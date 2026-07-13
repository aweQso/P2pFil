using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace P2PFil.ChatModule
{
    public static class EncryptionHelper
    {
        private const int NonceSize = 12;
        private const int TagSize = 16;

        public static string Encrypt(string plainText, byte[] key)
        {
            if (plainText == null) throw new ArgumentNullException(nameof(plainText));
            return Convert.ToBase64String(EncryptBytes(Encoding.UTF8.GetBytes(plainText), key));
        }

        public static string Decrypt(string cipherText, byte[] key)
        {
            if (cipherText == null) throw new ArgumentNullException(nameof(cipherText));
            return Encoding.UTF8.GetString(DecryptBytes(Convert.FromBase64String(cipherText), key));
        }

        // C# 12 Uyumluluğu: byte[] için varsayılan şifreleme metodu
        public static byte[] EncryptBytes(byte[] plainBytes, byte[] key)
        {
            return EncryptBytes(plainBytes, plainBytes.Length, key);
        }

        // C# 12 Uyumluluğu: Span yerine Array+Length alarak async metotlardan güvenli çağrı sağlar
        public static byte[] EncryptBytes(byte[] plainBytes, int length, byte[] key)
        {
            if (key == null || key.Length != 32) throw new ArgumentException("Key 32 byte olmalı.");

            byte[] nonce = new byte[NonceSize];
            RandomNumberGenerator.Fill(nonce);

            byte[] tag = new byte[TagSize];
            byte[] cipherText = new byte[length];

            using (var aesGcm = new AesGcm(key, TagSize))
            {
                // Senkron bir metot içinde AsSpan kullanmak C# 12'de tamamen güvenlidir
                aesGcm.Encrypt(nonce, plainBytes.AsSpan(0, length), cipherText, tag);
            }

            byte[] result = new byte[NonceSize + TagSize + cipherText.Length];
            Buffer.BlockCopy(nonce, 0, result, 0, NonceSize);
            Buffer.BlockCopy(tag, 0, result, NonceSize, TagSize);
            Buffer.BlockCopy(cipherText, 0, result, NonceSize + TagSize, cipherText.Length);
            return result;
        }

        public static byte[] DecryptBytes(byte[] cipherBytes, byte[] key)
        {
            if (key == null || key.Length != 32) throw new ArgumentException("Key 32 byte olmalı.");
            if (cipherBytes.Length < NonceSize + TagSize) throw new CryptographicException("Veri bozuk.");

            byte[] nonce = new byte[NonceSize];
            Buffer.BlockCopy(cipherBytes, 0, nonce, 0, NonceSize);

            byte[] tag = new byte[TagSize];
            Buffer.BlockCopy(cipherBytes, NonceSize, tag, 0, TagSize);

            int cipherLength = cipherBytes.Length - NonceSize - TagSize;
            byte[] cipherText = new byte[cipherLength];
            Buffer.BlockCopy(cipherBytes, NonceSize + TagSize, cipherText, 0, cipherLength);

            byte[] plainBytes = new byte[cipherLength];

            using (var aesGcm = new AesGcm(key, TagSize))
            {
                aesGcm.Decrypt(nonce, cipherText, tag, plainBytes);
            }
            return plainBytes;
        }
    }
}