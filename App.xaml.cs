using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
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
        FfmpegPath = Path.Combine(exeDir, AppStrings.FfmpegExe);
        FfprobePath = Path.Combine(exeDir, AppStrings.FfprobeExe);

        Ffmpeg = new FfmpegService(FfmpegPath);
        Ffprobe = new FfprobeService(FfprobePath);

        var args = e.Args;
        var mode = GetArg(args, "--mode");
        var files = GetFiles(args);

        if (args.Contains("--install"))
        {
            var allUsers = args.Contains("--allusers");
            try
            {
                RegistryService.Register(AppContext.BaseDirectory, allUsers: allUsers);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"レジストリへの登録に失敗しました。\n{ex.Message}",
                    AppStrings.AppName,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
                Shutdown(1);
                return;
            }
            Shutdown(0);
            return;
        }

        if (args.Contains("--uninstall"))
        {
            var allUsers = args.Contains("--allusers");
            try
            {
                RegistryService.Unregister(allUsers: allUsers);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"レジストリからの削除に失敗しました。\n{ex.Message}",
                    AppStrings.AppName,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
                Shutdown(1);
                return;
            }
            Shutdown(0);
            return;
        }

        if (mode != null && files.Count > 0)
        {
            // var collected = CollectFromSiblingInstances(mode, files);
            // if (collected == null)
            //     return;
            // files = collected;
        }

        if (!File.Exists(FfmpegPath) || !File.Exists(FfprobePath))
        {
            MessageBox.Show(
                AppStrings.Error_FfmpegMissing,
                AppStrings.AppName,
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
            Shutdown(1);
            return;
        }

        if (!UserSettings.Initialized)
        {
            UserSettings.HwEncoder = DetectHardwareEncoder(FfmpegPath);
            UserSettings.Initialized = true;
        }

        if (files.Count == 0 && mode != null)
        {
            MessageBox.Show(
                AppStrings.Error_FileNotSpecified,
                AppStrings.AppName,
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );
            Shutdown(1);
            return;
        }

        Window window = mode switch
        {
            "convert" => new Views.ConvertDialog(files),
            "resize" => new Views.ResizeDialog(files),
            "cut" => new Views.CutDialog(files),
            "compress" => new Views.CompressDialog(files),
            _ => new Views.MainWindow(),
        };

        try
        {
            window.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                string.Format(AppStrings.Error_WindowStartupFailed, ex.Message, ex.StackTrace),
                AppStrings.AppName
            );
            Shutdown(1);
        }
    }

    private List<string>? CollectFromSiblingInstances(string mode, List<string> files)
    {
        var pipeName = $"RShiftTools_{mode}";
        var sessionId = Environment.ProcessId;
        var sessionKey = (DateTime.UtcNow.Ticks / 100000).ToString();
        var sessionDir = Path.Combine(Path.GetTempPath(), "RShiftTools", pipeName + "_" + sessionKey);

        Directory.CreateDirectory(sessionDir);
        var myFile = Path.Combine(sessionDir, $"{sessionId}.txt");
        File.WriteAllLines(myFile, files.Where(File.Exists));

        try
        {
            using var pipe = new System.IO.Pipes.NamedPipeServerStream(
                pipeName,
                System.IO.Pipes.PipeDirection.In,
                1
            );

            Thread.Sleep(200);

            var allFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in Directory.GetFiles(sessionDir, "*.txt"))
            {
                try
                {
                    foreach (var p in File.ReadAllLines(f))
                    {
                        if (File.Exists(p))
                            allFiles.Add(p);
                    }
                }
                catch { }
            }

            try { Directory.Delete(sessionDir, true); } catch { }

            if (allFiles.Count == 0)
            {
                Shutdown(1);
                return null;
            }

            return allFiles.ToList();
        }
        catch (System.IO.IOException)
        {
            Shutdown(0);
            return null;
        }
    }

    private static string? GetArg(string[] args, string key)
    {
        var idx = Array.IndexOf(args, key);
        return idx >= 0 && idx + 1 < args.Length ? args[idx + 1] : null;
    }

    private static List<string> GetFiles(string[] args)
    {
        var fileListPath = GetArg(args, "--filelist");
        if (fileListPath != null)
        {
            var files = new List<string>();
            try
            {
                if (File.Exists(fileListPath))
                {
                    foreach (var line in File.ReadAllLines(fileListPath))
                    {
                        var path = line.Trim();
                        if (path.Length > 0 && File.Exists(path))
                            files.Add(path);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to read filelist: {ex.Message}");
            }
            return files;
        }

        var files2 = new List<string>();
        var idx = Array.IndexOf(args, "--files");
        if (idx < 0)
            return files2;

        var start = idx + 1;
        for (var i = start; i < args.Length; i++)
        {
            if (args[i].StartsWith("--"))
                break;
            if (File.Exists(args[i]))
                files2.Add(args[i]);
        }

        if (files2.Count > 0)
            return files2;

        var commandLine = Environment.CommandLine;
        var marker = " --files ";
        var markerIdx = commandLine.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIdx < 0)
            return files2;

        var rawFilesPart = commandLine[(markerIdx + marker.Length)..];
        var argsMarkerInRaw = rawFilesPart.IndexOf(" --", StringComparison.Ordinal);
        if (argsMarkerInRaw >= 0)
            rawFilesPart = rawFilesPart[..argsMarkerInRaw];

        rawFilesPart = rawFilesPart.Trim();
        if (string.IsNullOrWhiteSpace(rawFilesPart))
            return files2;

        var expanded = ProcessHelper.SplitCommandLinePublic(rawFilesPart);
        foreach (var token in expanded)
        {
            if (File.Exists(token))
                files2.Add(token);
        }

        return files2;
    }

    private static string DetectHardwareEncoder(string ffmpegPath)
    {
        try
        {
            using var process = ProcessHelper.StartProcess(
                ffmpegPath,
                new[] { "-hide_banner", "-encoders" },
                redirectStdOut: true,
                redirectStdErr: false
            );
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            if (output.Contains("h264_nvenc"))
                return "NVIDIA (nvenc)";
            if (output.Contains("h264_amf"))
                return "AMD (amf)";
            if (output.Contains("h264_qsv"))
                return "Intel (qsv)";
        }
        catch (Exception ex)
        {
            Log.Error($"HW encoder detection failed: {ex.Message}");
        }
        return "自動 (CPU)";
    }
}
