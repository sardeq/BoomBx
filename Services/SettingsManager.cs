using System;
using System.IO;
using System.Text.Json;
using BoomBx.Models;

namespace BoomBx.Services
{
    public static class SettingsManager
    {
        private static readonly string AppDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BoomBx");

        public static AppSettings LoadSettings()
        {
            Directory.CreateDirectory(AppDataDir);
            var path = Path.Combine(AppDataDir, "settings.json");
            
            return File.Exists(path) 
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings()
                : new AppSettings();
        }

        public static void SaveSettings(AppSettings settings)
        {
            Directory.CreateDirectory(AppDataDir);
            var path = Path.Combine(AppDataDir, "settings.json");
            File.WriteAllText(path, JsonSerializer.Serialize(settings));
        }
    }
}