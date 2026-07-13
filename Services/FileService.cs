using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace P2PFil.Services
{
    public static class FileService
    {
        // Android ve Masaüstü platformları için güvenli indirme/paylaşım yolları[cite: 18]
#if ANDROID
        public static readonly string SharedPath = Path.Combine(Android.OS.Environment.GetExternalStoragePublicDirectory(Android.OS.Environment.DirectoryDownloads)?.AbsolutePath ?? string.Empty, "P2P_Shared"); //[cite: 18]
        public static readonly string DownloadPath = Path.Combine(Android.OS.Environment.GetExternalStoragePublicDirectory(Android.OS.Environment.DirectoryDownloads)?.AbsolutePath ?? string.Empty, "P2P_Downloads"); //[cite: 18]
#else
        public static readonly string SharedPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "P2P_Shared"); //[cite: 18]
        public static readonly string DownloadPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "P2P_Downloads"); //[cite: 18]
#endif

        static FileService()
        {
            EnsureDirectoriesExist(); //[cite: 18]
        }

        private static void EnsureDirectoriesExist()
        {
            // Klasör yolları boş veya null değilse güvenli şekilde oluşturulur[cite: 18]
            if (!string.IsNullOrEmpty(SharedPath) && !Directory.Exists(SharedPath)) //[cite: 18]
                Directory.CreateDirectory(SharedPath); //[cite: 18]

            if (!string.IsNullOrEmpty(DownloadPath) && !Directory.Exists(DownloadPath)) //[cite: 18]
                Directory.CreateDirectory(DownloadPath); //[cite: 18]
        }

        public static string SaveFile(string sourcePath, string fileName)
        {
            EnsureDirectoriesExist(); //[cite: 18]

            // Dosya adı doğrulanır, null ise varsayılan isim atanır[cite: 18]
            string safeFileName = Path.GetFileName(fileName) ?? "isimsiz_dosya"; //[cite: 18]
            string destinationPath = Path.Combine(SharedPath, safeFileName); //[cite: 18]

            if (File.Exists(destinationPath)) //[cite: 18]
            {
                throw new Exception("Bu isimde bir dosya zaten listenizde mevcut! Lütfen dosya adını değiştirip tekrar deneyin."); //[cite: 18]
            }

            File.Copy(sourcePath, destinationPath, true); //[cite: 18]
            return destinationPath; //[cite: 18]
        }

        public static void DeleteFile(string fileName)
        {
            string safeFileName = Path.GetFileName(fileName) ?? string.Empty; //[cite: 18]
            if (string.IsNullOrEmpty(safeFileName)) return; //[cite: 18]

            string filePath = Path.Combine(SharedPath, safeFileName); //[cite: 18]
            if (File.Exists(filePath)) //[cite: 18]
            {
                File.Delete(filePath); //[cite: 18]
            }
        }

        public static List<FileInfo> GetSavedFiles()
        {
            EnsureDirectoriesExist(); //[cite: 18]
            if (string.IsNullOrEmpty(SharedPath)) return new List<FileInfo>(); //[cite: 18]

            DirectoryInfo d = new DirectoryInfo(SharedPath); //[cite: 18]
            return d.GetFiles().ToList(); //[cite: 18]
        }

        public static string GetFileHash(string filePath)
        {
            if (!File.Exists(filePath)) return string.Empty; //[cite: 18]

            // C# 12 uyumlu ve null-safe SHA256 nesne yönetimi[cite: 18]
            using var sha256 = SHA256.Create(); //[cite: 18]
            if (sha256 == null) return string.Empty; //[cite: 18]

            using var stream = File.OpenRead(filePath); //[cite: 18]
            var hashBytes = sha256.ComputeHash(stream); //[cite: 18]
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant(); //[cite: 18]
        }
    }
}