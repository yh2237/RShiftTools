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

    public static bool Initialized
    {
        get => _data.Initialized;
        set
        {
            _data.Initialized = value;
            Save();
        }
    }

    public static void SaveNow() => Save();

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
        var json = JsonSerializer.Serialize(
            _data,
            new JsonSerializerOptions { WriteIndented = true }
        );
        var path = _path;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to save settings: {ex.Message}");
        }
    }

    private sealed class SettingsData
    {
        public string HwEncoder { get; set; } = "自動 (CPU)";
        public bool Initialized { get; set; }
    }
}
