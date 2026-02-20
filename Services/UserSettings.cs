using System.IO;
using System.Text.Json;

namespace RShiftTools.Services;

public static class UserSettings
{
    private static readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RShiftTools",
        "settings.json"
    );

    private static SettingsData _data = Load();

    public static string HwEncoder
    {
        get => _data.HwEncoder;
        set
        {
            _data.HwEncoder = value;
            Save();
        }
    }

    private static SettingsData Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                return JsonSerializer.Deserialize<SettingsData>(json) ?? new SettingsData();
            }
        }
        catch { }
        return new SettingsData();
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var json = JsonSerializer.Serialize(
                _data,
                new JsonSerializerOptions { WriteIndented = true }
            );
            File.WriteAllText(_path, json);
        }
        catch { }
    }

    private sealed class SettingsData
    {
        public string HwEncoder { get; set; } = "自動 (CPU)";
    }
}
