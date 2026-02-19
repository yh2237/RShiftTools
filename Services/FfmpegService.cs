using System.Diagnostics;
using System.Text.RegularExpressions;
using System.IO;

namespace RShiftTools.Services;

public class FfmpegProgress
{
    public double Percent { get; init; }
    public TimeSpan CurrentTime { get; init; }
    public string RawLine { get; init; } = "";
}

public class FfmpegService
{
    private readonly string _ffmpegPath;
    private static readonly Regex TimeRegex =
        new(@"time=(\d+):(\d+):(\d+)\.(\d+)", RegexOptions.Compiled);

    public FfmpegService(string ffmpegPath)
    {
        _ffmpegPath = ffmpegPath;
    }

    public async Task<(bool Success, string ErrorLog)> RunAsync(
    string arguments,
    double totalDurationSeconds,
    IProgress<FfmpegProgress>? onProgress = null,
    CancellationToken cancellationToken = default)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _ffmpegPath,
                Arguments = arguments,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(_ffmpegPath) ?? "",
            }
        };

        var ffmpegDir = Path.GetDirectoryName(_ffmpegPath) ?? "";
        process.StartInfo.Environment["PATH"] = ffmpegDir + ";" + Environment.GetEnvironmentVariable("PATH");

        var errorLines = new System.Text.StringBuilder();

        process.Start();

        var readTask = Task.Run(async () =>
        {
            while (!process.StandardError.EndOfStream)
            {
                var line = await process.StandardError.ReadLineAsync(cancellationToken);
                if (line == null) break;

                errorLines.AppendLine(line);

                var match = TimeRegex.Match(line);
                if (match.Success && totalDurationSeconds > 0)
                {
                    var h = int.Parse(match.Groups[1].Value);
                    var m = int.Parse(match.Groups[2].Value);
                    var s = int.Parse(match.Groups[3].Value);
                    var cs = int.Parse(match.Groups[4].Value);
                    var current = new TimeSpan(0, h, m, s, cs * 10);
                    var percent = Math.Min(current.TotalSeconds / totalDurationSeconds, 1.0);

                    onProgress?.Report(new FfmpegProgress
                    {
                        Percent = percent,
                        CurrentTime = current,
                        RawLine = line,
                    });
                }
            }
        }, cancellationToken);

        await using var reg = cancellationToken.Register(() =>
        {
            try { process.Kill(); } catch { }
        });

        await readTask;
        await process.WaitForExitAsync(cancellationToken);

        return (process.ExitCode == 0, errorLines.ToString());
    }

    public static string ResolveOutputPath(string inputPath, string newExtension, bool overwrite)
    {
        var dir = Path.GetDirectoryName(inputPath) ?? "";
        var nameWithoutExt = Path.GetFileNameWithoutExtension(inputPath);
        var ext = newExtension.StartsWith('.') ? newExtension : $".{newExtension}";

        var candidate = Path.Combine(dir, $"{nameWithoutExt}{ext}");

        if (!File.Exists(candidate) || overwrite)
            return candidate;

        var suffix = "_out";
        var result = Path.Combine(dir, $"{nameWithoutExt}{suffix}{ext}");
        var i = 2;
        while (File.Exists(result))
        {
            result = Path.Combine(dir, $"{nameWithoutExt}{suffix}{i}{ext}");
            i++;
        }
        return result;
    }
}