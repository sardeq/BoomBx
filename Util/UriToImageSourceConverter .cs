using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System;
using System.Globalization;
using System.IO;

namespace BoomBx.Converters
{
    public class UriToImageSourceConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not string path) return null;

            try
            {
                if (path.StartsWith("avares://"))
                {
                    var uri = new Uri(path);
                    using var stream = AssetLoader.Open(uri);
                    return new Bitmap(stream);
                }

                if (Path.IsPathRooted(path))
                {
                    return new Bitmap(path);
                }

                var appDataPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "BoomBx",
                    "icons",
                    path);

                return File.Exists(appDataPath) ? new Bitmap(appDataPath) : GetDefaultIcon();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading image: {ex}");
                return GetDefaultIcon();
            }
        }

        private static Bitmap GetDefaultIcon()
        {
            var uri = new Uri("avares://BoomBx/Assets/bocchi.jpg");
            using var stream = AssetLoader.Open(uri);
            return new Bitmap(stream);
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) 
            => throw new NotSupportedException();
    }
}