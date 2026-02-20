using System.IO;
using Microsoft.Win32;

namespace RShiftTools.Services;

public class DialogService : IDialogService
{
    public string? AskOutputPath(string inputPath, string outputExt)
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
