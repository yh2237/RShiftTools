using Microsoft.Win32;
using System.IO;

namespace RShiftTools.Services;

public static class RegistryService
{
    private const string AppName = "RShiftTools";
    private const string ExeName = "rshiftt.exe";

    public static void Register(string installDir)
    {
        var exePath = Path.Combine(installDir, ExeName);

        using var rootKey = Registry.ClassesRoot.CreateSubKey($@"*\shell\{AppName}");
        rootKey.SetValue("MUIVerb", AppName);
        rootKey.SetValue("SubCommands", "");

        using var shellKey = rootKey.CreateSubKey("shell");

        var modes = new (string mode, string label)[]
        {
        ("convert",  "変換..."),
        ("resize",   "リサイズ..."),
        ("cut",      "カット..."),
        ("compress", "サイズ圧縮..."),
        };

        foreach (var (mode, label) in modes)
        {
            using var modeKey = shellKey.CreateSubKey(mode);
            modeKey.SetValue("", label);
            using var cmdKey = modeKey.CreateSubKey("command");
            cmdKey.SetValue("", $"\"{exePath}\" --mode {mode} --files \"%1\"");
        }
    }

    public static void Unregister()
    {
        try
        {
            Registry.ClassesRoot.DeleteSubKeyTree($@"*\shell\{AppName}", throwOnMissingSubKey: false);
        }
        catch { }
    }
}