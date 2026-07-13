using System;
using System.Globalization;
using Microsoft.Maui.Controls;

namespace P2PFil.Helpers
{
    public class AlignmentConverter : IValueConverter
    {
        // Parametrelerdeki object türlerinin yanına '?' ekledik
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool isMe && isMe)
            {
                return LayoutOptions.End;
            }
            return LayoutOptions.Start;
        }

        // Burada da aynı şekilde '?' ekledik
        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}