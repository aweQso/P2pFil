using System.Globalization;

namespace P2PFil.Helpers;

public class InvertedBoolConverter : IValueConverter
{
    // object? kullanımı, null değer gelebileceğini belirtir ve hatayı çözer
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b) return !b;
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b) return !b;
        return true;
    }
}