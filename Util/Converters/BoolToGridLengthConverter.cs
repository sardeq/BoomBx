using Avalonia.Data.Converters;
using Avalonia;
using System;
using System.Globalization;
using Avalonia.Controls;

namespace BoomBx.Converters
{
    public class BoolToGridLengthConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool boolValue && boolValue)
            {
                return new GridLength(200); 
            }
            return new GridLength(0); 
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}