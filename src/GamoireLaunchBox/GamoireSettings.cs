using System;
using System.IO;
using System.Text.Json;

namespace GamoireLaunchBox
{
    public sealed class GamoireSettings
    {
        public string ServerUrl { get; set; } = UrlRules.DefaultServer;

        public bool SkipHidden { get; set; } = true;

        public static string DefaultFolder() =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Gamoire", "LaunchBox");

        public static GamoireSettings Load(string folder)
        {
            try
            {
                var path = Path.Combine(folder, "settings.json");
                if (!File.Exists(path)) return new GamoireSettings();
                var loaded = JsonSerializer.Deserialize<GamoireSettings>(File.ReadAllText(path)) ?? new GamoireSettings();
                if (string.IsNullOrWhiteSpace(loaded.ServerUrl)) loaded.ServerUrl = UrlRules.DefaultServer;
                return loaded;
            }
            catch (Exception ex) when (ex is IOException || ex is JsonException || ex is UnauthorizedAccessException)
            {
                return new GamoireSettings();
            }
        }

        public void Save(string folder)
        {
            try
            {
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "settings.json"), JsonSerializer.Serialize(this));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
        }
    }
}
