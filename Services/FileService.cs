using System.IO;
using System.Security.Cryptography;

namespace P2PFil.Services;

public static class FileService
{
    // 1. DÜZELTME: '.AbsolutePath' öncesi '?' konuldu ve '??' ile null olma durumuna karşı koruma eklendi.
#if ANDROID
    public static readonly string SharedPath = Path.Combine(Android.OS.Environment.GetExternalStoragePublicDirectory(Android.OS.Environment.DirectoryDownloads)?.AbsolutePath ?? string.Empty, "P2P_Shared");
    public static readonly string DownloadPath = Path.Combine(Android.OS.Environment.GetExternalStoragePublicDirectory(Android.OS.Environment.DirectoryDownloads)?.AbsolutePath ?? string.Empty, "P2P_Downloads");
#else
    public static readonly string SharedPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "P2P_Shared");
    public static readonly string DownloadPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "P2P_Downloads");
#endif

    static FileService()
    {
        EnsureDirectoriesExist();
    }

    private static void EnsureDirectoriesExist()
    {
        // Klasör yolları null değilse oluştur
        if (!string.IsNullOrEmpty(SharedPath) && !Directory.Exists(SharedPath))
            Directory.CreateDirectory(SharedPath);

        if (!string.IsNullOrEmpty(DownloadPath) && !Directory.Exists(DownloadPath))
            Directory.CreateDirectory(DownloadPath);
    }

    public static string SaveFile(string sourcePath, string fileName)
    {
        EnsureDirectoriesExist();

        // 2. DÜZELTME: GetFileName null dönerse diye "isimsiz_dosya" varsayılanı atandı
        string safeFileName = Path.GetFileName(fileName) ?? "isimsiz_dosya";
        string destinationPath = Path.Combine(SharedPath, safeFileName);

        if (File.Exists(destinationPath))
        {
            throw new Exception("Bu isimde bir dosya zaten listenizde mevcut! Lütfen dosya adını değiştirip tekrar deneyin.");
        }

        File.Copy(sourcePath, destinationPath, true);
        return destinationPath;
    }

    public static void DeleteFile(string fileName)
    {
        string safeFileName = Path.GetFileName(fileName) ?? string.Empty;
        if (string.IsNullOrEmpty(safeFileName)) return;

        string filePath = Path.Combine(SharedPath, safeFileName);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }

    public static List<FileInfo> GetSavedFiles()
    {
        EnsureDirectoriesExist();
        if (string.IsNullOrEmpty(SharedPath)) return new List<FileInfo>();

        DirectoryInfo d = new DirectoryInfo(SharedPath);
        return d.GetFiles().ToList();
    }

    public static string GetFileHash(string filePath)
    {
        if (!File.Exists(filePath)) return string.Empty;

        // 3. DÜZELTME: SHA256.Create() null dönerse diye güvenlik şartı eklendi
        using var sha256 = SHA256.Create();
        if (sha256 == null) return string.Empty;

        using var stream = File.OpenRead(filePath);
        var hashBytes = sha256.ComputeHash(stream);
        return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
    }
}