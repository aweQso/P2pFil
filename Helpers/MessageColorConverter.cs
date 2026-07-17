using System.Globalization;

namespace P2PFil.Helpers;

public class MessageColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // IsMe true ise mor, false ise gri arka plan
        return (value is bool isMe && isMe) ? Color.FromArgb("#6366F1") : Color.FromArgb("#30FFFFFF");
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}