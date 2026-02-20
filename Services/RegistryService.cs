using System.IO;
using Microsoft.Win32;

namespace RShiftTools.Services;

public static class RegistryService
{
    public static void Register(string installDir, bool allUsers = false)
    {
        var exePath = Path.Combine(installDir, AppStrings.ExeName);

        var baseRoot = allUsers ? Registry.ClassesRoot : Registry.CurrentUser;
        var basePath = allUsers
            ? $@"*\shell\{AppStrings.AppName}"
            : $@"Software\Classes\*\shell\{AppStrings.AppName}";

        using var rootKey = baseRoot.CreateSubKey(basePath);
        rootKey.SetValue("MUIVerb", AppStrings.MUIVerb);
        rootKey.SetValue("SubCommands", "");

        using var shellKey = rootKey.CreateSubKey("shell");

        var modes = new (string mode, string label)[]
        {
            ("convert", "変換..."),
            ("resize", "リサイズ..."),
            ("cut", "カット..."),
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

    public static void Unregister(bool allUsers = false)
    {
        try
        {
            if (allUsers)
            {
                Registry.ClassesRoot.DeleteSubKeyTree(
                    $@"*\shell\{AppStrings.AppName}",
                    throwOnMissingSubKey: false
                );
            }
            else
            {
                Registry.CurrentUser.DeleteSubKeyTree(
                    $@"Software\Classes\*\shell\{AppStrings.AppName}",
                    throwOnMissingSubKey: false
                );
            }
        }
        catch { }
    }
}
