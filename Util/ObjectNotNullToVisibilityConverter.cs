using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace BoomBx.Views
{
    public class ObjectNotNullToVisibilityConverter : IValueConverter
    {
        public static readonly ObjectNotNullToVisibilityConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value != null;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}