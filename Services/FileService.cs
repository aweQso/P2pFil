using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using P2PFil.Models;

namespace P2PFil.Services
{
    public static class FileService
    {
        private static readonly string MetadataPath = Path.Combine(Microsoft.Maui.Storage.FileSystem.AppDataDirectory, "shared_files.json");

        public static List<SharedFile> GetSavedFiles()
        {
            if (!File.Exists(MetadataPath)) return new List<SharedFile>();
            try
            {
                var json = File.ReadAllText(MetadataPath);
                var files = JsonSerializer.Deserialize<List<SharedFile>>(json) ?? new();

                // Cihazdaki orijinal dosya manuel silinmişse listeden temizle
                var validFiles = files.Where(f => File.Exists(f.OriginalPath)).ToList();
                if (validFiles.Count != files.Count) SaveSharedFiles(validFiles);
                return validFiles;
            }
            catch { return new List<SharedFile>(); }
        }

        private static void SaveSharedFiles(List<SharedFile> files)
        {
            File.WriteAllText(MetadataPath, JsonSerializer.Serialize(files));
        }

        public static void SaveFile(string sourcePath, string fileName)
        {
            var files = GetSavedFiles();
            if (files.Any(f => f.OriginalPath == sourcePath))
                throw new Exception("Bu dosya zaten paylaşım listenizde mevcut!");

            var fileInfo = new FileInfo(sourcePath);
            files.Add(new SharedFile
            {
                FileName = fileName ?? fileInfo.Name,
                OriginalPath = sourcePath,
                FileSize = $"{fileInfo.Length / 1024 / 1024.0:F2} MB",
                UploadDate = DateTime.Now
            });

            SaveSharedFiles(files);
        }

        public static void DeleteFile(string originalPath)
        {
            var files = GetSavedFiles();
            var fileToRemove = files.FirstOrDefault(f => f.OriginalPath == originalPath);
            if (fileToRemove != null)
            {
                files.Remove(fileToRemove);
                SaveSharedFiles(files);
                // DİKKAT: File.Delete() KESİNLİKLE KULLANILMIYOR. Orijinal dosya korunuyor.
            }
        }

        public static string GetFileHash(string filePath)
        {
            if (!File.Exists(filePath)) return string.Empty;
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            var hashBytes = sha256.ComputeHash(stream);
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }
    }
}