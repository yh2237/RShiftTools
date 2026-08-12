using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
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
            var collected = CollectFromSiblingInstances(mode, files);
            if (collected == null)
                return;
            files = collected;
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

        if (
            !UserSettings.Initialized
            || !IsConfiguredHardwareEncoderAvailable(FfmpegPath, UserSettings.HwEncoder)
        )
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

        if (mode == "cut" && files.Count != 1)
        {
            MessageBox.Show(
                AppStrings.Error_CutSingleFile,
                AppStrings.AppName,
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );
            Shutdown(1);
            return;
        }

        if (
            mode == "audio-edit"
            && files.Any(path => MediaFormats.GetKind(path) != MediaFormats.MediaKind.Audio)
        )
        {
            MessageBox.Show(
                AppStrings.Error_AudioFilesOnly,
                AppStrings.AppName,
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );
            Shutdown(1);
            return;
        }

        try
        {
            Window window = mode switch
            {
                "convert" => new Views.ConvertDialog(files),
                "resize" => new Views.ResizeDialog(files),
                "cut" => new Views.CutDialog(files),
                "compress" => new Views.CompressDialog(files),
                "audio-edit" => new Views.AudioEditDialog(files),
                _ => new Views.MainWindow(),
            };
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
        var windowsSessionId = Process.GetCurrentProcess().SessionId;
        var pipeName = $"RShiftTools_{windowsSessionId}_{mode}";
        System.IO.Pipes.NamedPipeServerStream server;
        try
        {
            server = new System.IO.Pipes.NamedPipeServerStream(
                pipeName,
                System.IO.Pipes.PipeDirection.In,
                1,
                System.IO.Pipes.PipeTransmissionMode.Byte,
                System.IO.Pipes.PipeOptions.Asynchronous
            );
        }
        catch (System.IO.IOException)
        {
            if (SendFilesToCollector(pipeName, files))
            {
                Shutdown(0);
                return null;
            }

            return files.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        using (server)
        {
            var allFiles = new HashSet<string>(
                files.Where(File.Exists),
                StringComparer.OrdinalIgnoreCase
            );
            var deadline = DateTime.UtcNow.AddSeconds(4);

            while (DateTime.UtcNow < deadline)
            {
                using var idleTimeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(700));
                try
                {
                    server.WaitForConnectionAsync(idleTimeout.Token).GetAwaiter().GetResult();
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                try
                {
                    using var reader = new StreamReader(
                        server,
                        Encoding.UTF8,
                        detectEncodingFromByteOrderMarks: false,
                        leaveOpen: true
                    );
                    var payload = reader.ReadLineAsync()
                        .WaitAsync(TimeSpan.FromSeconds(2))
                        .GetAwaiter()
                        .GetResult();
                    var received = payload == null
                        ? null
                        : JsonSerializer.Deserialize<List<string>>(payload);
                    if (received != null)
                    {
                        foreach (var path in received.Where(File.Exists))
                            allFiles.Add(path);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"Failed to receive selected files: {ex.Message}");
                }
                finally
                {
                    if (server.IsConnected)
                        server.Disconnect();
                }
            }

            return allFiles.ToList();
        }
    }

    private static bool SendFilesToCollector(string pipeName, IEnumerable<string> files)
    {
        try
        {
            using var client = new System.IO.Pipes.NamedPipeClientStream(
                ".",
                pipeName,
                System.IO.Pipes.PipeDirection.Out
            );
            client.Connect(1500);
            using var writer = new StreamWriter(
                client,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                leaveOpen: true
            ) { AutoFlush = true };
            writer.WriteLine(JsonSerializer.Serialize(files.Where(File.Exists)));
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to send selected files to collector: {ex.Message}");
            return false;
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
        if (CanInitializeEncoder(ffmpegPath, "h264_nvenc")) return "NVIDIA (nvenc)";
        if (CanInitializeEncoder(ffmpegPath, "h264_amf")) return "AMD (amf)";
        if (CanInitializeEncoder(ffmpegPath, "h264_qsv")) return "Intel (qsv)";
        return "自動 (CPU)";
    }

    private static bool IsConfiguredHardwareEncoderAvailable(string ffmpegPath, string setting) =>
        setting switch
        {
            "NVIDIA (nvenc)" => CanInitializeEncoder(ffmpegPath, "h264_nvenc"),
            "AMD (amf)" => CanInitializeEncoder(ffmpegPath, "h264_amf"),
            "Intel (qsv)" => CanInitializeEncoder(ffmpegPath, "h264_qsv"),
            _ => true,
        };

    private static bool CanInitializeEncoder(string ffmpegPath, string encoder)
    {
        try
        {
            using var process = ProcessHelper.StartProcess(
                ffmpegPath,
                new[]
                {
                    "-hide_banner", "-loglevel", "error",
                    "-f", "lavfi", "-i", "color=size=64x64:rate=1",
                    "-frames:v", "1", "-an", "-c:v", encoder,
                    "-f", "null", "-",
                },
                redirectStdOut: false,
                redirectStdErr: true
            );
            var errorTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(5000))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
                Log.Error($"Hardware encoder probe timed out: {encoder}");
                return false;
            }
            var error = errorTask.GetAwaiter().GetResult();
            if (process.ExitCode == 0)
                return true;
            Log.Debug($"Hardware encoder unavailable: {encoder}: {error}");
        }
        catch (Exception ex)
        {
            Log.Error($"Hardware encoder detection failed ({encoder}): {ex.Message}");
        }
        return false;
    }
}
