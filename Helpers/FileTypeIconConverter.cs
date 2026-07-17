using System;
using System.Globalization;
using System.IO;
using Microsoft.Maui.Controls;

namespace P2PFil.Helpers
{
    // Dosya adına bakarak uzantısına uygun bir emoji ikon döner.
    // FilesPage.xaml'de "Benim Paylaşımlarım" listesindeki avatar alanında kullanılır.
    public class FileTypeIconConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            string fileName = value as string ?? string.Empty;
            string ext = Path.GetExtension(fileName).ToLowerInvariant();

            return ext switch
            {
                ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" or ".bmp" => "🖼",
                ".mp4" or ".mov" or ".avi" or ".mkv" or ".webm" => "🎬",
                ".pdf" => "📕",
                ".doc" or ".docx" => "📄",
                ".xls" or ".xlsx" => "📊",
                ".ppt" or ".pptx" => "📽",
                ".zip" or ".rar" or ".7z" => "🗜",
                ".mp3" or ".wav" or ".flac" or ".m4a" => "🎵",
                _ => "📦"
            };
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}