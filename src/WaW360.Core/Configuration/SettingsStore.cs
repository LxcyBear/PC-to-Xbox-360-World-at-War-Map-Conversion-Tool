using System.Text.Json;

namespace WaW360.Core.Configuration;

public static class SettingsStore
{
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WaW360Converter",
        "settings.json");

    public static ConverterSettings Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (!File.Exists(path)) return new ConverterSettings();
            var settings = JsonSerializer.Deserialize<ConverterSettings>(File.ReadAllText(path)) ?? new ConverterSettings();
            if (string.IsNullOrWhiteSpace(settings.OutputFolder) ||
                Path.GetFullPath(settings.OutputFolder).Equals(Path.GetFullPath(ConverterSettings.LegacyOutputFolder), StringComparison.OrdinalIgnoreCase))
                settings.OutputFolder = ConverterSettings.DefaultOutputFolder;
            settings.MaxParallelMaps = Math.Clamp(settings.MaxParallelMaps, 1, 2);
            settings.BatchMode = string.Equals(settings.BatchMode, "Safe Mode (Full Validation)", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(settings.BatchMode, "Full Regression", StringComparison.OrdinalIgnoreCase)
                ? "Safe Mode (Full Validation)"
                : "Fast Mode";
            return settings;
        }
        catch
        {
            return new ConverterSettings();
        }
    }

    public static void Save(ConverterSettings settings, string? path = null)
    {
        path ??= DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }
}
