using System.IO;
using System.Windows;
using RShiftTools.Services;

namespace RShiftTools;

public partial class App : Application
{
    public static string FfmpegPath { get; private set; } = "";
    public static string FfprobePath { get; private set; } = "";
    public static FfmpegService Ffmpeg { get; private set; } = null!;
    public static FfprobeService Ffprobe { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var exeDir = AppContext.BaseDirectory;
        FfmpegPath = Path.Combine(exeDir, "ffmpeg.exe");
        FfprobePath = Path.Combine(exeDir, "ffprobe.exe");

        Ffmpeg = new FfmpegService(FfmpegPath);
        Ffprobe = new FfprobeService(FfprobePath);

        var args = e.Args;
        var mode = GetArg(args, "--mode");
        var files = GetFiles(args);

        if (args.Contains("--install"))
        {
            try
            {
                RegistryService.Register(AppContext.BaseDirectory);
                MessageBox.Show("右クリックメニューへの登録が完了しました。",
                    "RShiftTools", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"登録に失敗しました。\n{ex.Message}",
                    "RShiftTools", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            Shutdown(0);
            return;
        }

        if (args.Contains("--uninstall"))
        {
            try
            {
                RegistryService.Unregister();
            }
            catch { }
            Shutdown(0);
            return;
        }

        if (!File.Exists(FfmpegPath) || !File.Exists(FfprobePath))
        {
            MessageBox.Show(
                "ffmpeg.exe / ffprobe.exe が見つかりません。\nexe と同じフォルダに配置してください。",
                "RShiftTools", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        if (files.Count == 0 && mode != null)
        {
            MessageBox.Show("ファイルが指定されていません。", "RShiftTools",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown(1);
            return;
        }

        Window? window = mode switch
        {
            "convert" => new Views.ConvertDialog(files),
            "resize" => new Views.ResizeDialog(files),
            "cut" => null, // new Views.CutDialog(files[0]),
            "compress" => new Views.CompressDialog(files),
            _ => null, // 設定画面（未実装）
        };

        if (window == null)
        {
            MessageBox.Show($"モード '{mode}' は未実装です。", "RShiftTools",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown(0);
            return;
        }

        window.Show();
    }

    private static string? GetArg(string[] args, string key)
    {
        var idx = Array.IndexOf(args, key);
        return idx >= 0 && idx + 1 < args.Length ? args[idx + 1] : null;
    }

    private static List<string> GetFiles(string[] args)
    {
        var files = new List<string>();
        var idx = Array.IndexOf(args, "--files");
        if (idx < 0) return files;

        for (var i = idx + 1; i < args.Length; i++)
        {
            if (args[i].StartsWith("--")) break;
            if (File.Exists(args[i])) files.Add(args[i]);
        }
        return files;
    }
}