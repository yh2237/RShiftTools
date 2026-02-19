using Microsoft.Win32;
using System.IO;

namespace RShiftTools.Services;

public static class DialogService
{
    public static string? AskOutputPath(string inputPath, string outputExt)
    {
        var dir = Path.GetDirectoryName(inputPath) ?? "";
        var nameWithoutExt = Path.GetFileNameWithoutExtension(inputPath);
        var ext = outputExt.StartsWith('.') ? outputExt : $".{outputExt}";

        var dialog = new SaveFileDialog
        {
            InitialDirectory = dir,
            FileName = nameWithoutExt + ext,
            DefaultExt = ext,
            Filter = $"出力ファイル (*{ext})|*{ext}|すべてのファイル (*.*)|*.*",
            OverwritePrompt = true,
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}