using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using RShiftTools.Models;

namespace RShiftTools.Services;

public class FfmpegService : IFfmpegService
{
    private readonly string _ffmpegPath;
    private static readonly Regex TimeRegex = new(
        @"time=(\d+):(\d+):(\d+)\.(\d+)",
        RegexOptions.Compiled
    );

    public FfmpegService(string ffmpegPath)
    {
        _ffmpegPath = ffmpegPath;
    }

    public async Task<(bool Success, string ErrorLog)> RunAsync(
        string arguments,
        double totalDurationSeconds,
        IProgress<FfmpegProgress>? onProgress = null,
        CancellationToken cancellationToken = default
    )
    {
        var argList = ProcessHelper.SplitCommandLinePublic(arguments);
        return await RunAsync(argList, totalDurationSeconds, onProgress, cancellationToken);
    }

    public async Task<(bool Success, string ErrorLog)> RunAsync(
        IEnumerable<string> arguments,
        double totalDurationSeconds,
        IProgress<FfmpegProgress>? onProgress = null,
        CancellationToken cancellationToken = default
    )
    {
        var joined = string.Join(' ', arguments);
        Log.Debug($"Starting ffmpeg: {_ffmpegPath} {joined}");
        using var process = ProcessHelper.StartProcess(
            _ffmpegPath,
            arguments,
            redirectStdOut: false,
            redirectStdErr: true
        );

        var errorLines = new System.Text.StringBuilder();

        var readTask = Task.Run(
            async () =>
            {
                try
                {
                    while (!process.StandardError.EndOfStream)
                    {
                        var line = await process.StandardError.ReadLineAsync(cancellationToken);
                        if (line == null)
                            break;

                        errorLines.AppendLine(line);

                        var match = TimeRegex.Match(line);
                        if (match.Success && totalDurationSeconds > 0)
                        {
                            if (!int.TryParse(match.Groups[1].Value, out var h))
                                h = 0;
                            if (!int.TryParse(match.Groups[2].Value, out var m))
                                m = 0;
                            if (!int.TryParse(match.Groups[3].Value, out var s))
                                s = 0;
                            if (!int.TryParse(match.Groups[4].Value, out var cs))
                                cs = 0;
                            var current = new TimeSpan(0, h, m, s, cs * 10);
                            var percent = Math.Min(
                                current.TotalSeconds / totalDurationSeconds,
                                1.0
                            );

                            onProgress?.Report(
                                new FfmpegProgress
                                {
                                    Percent = percent,
                                    CurrentTime = current,
                                    RawLine = line,
                                }
                            );
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"Error reading ffmpeg stderr: {ex.Message}");
                }
            },
            cancellationToken
        );

        await using var reg = cancellationToken.Register(() =>
        {
            try
            {
                process.Kill();
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to kill ffmpeg process: {ex.Message}");
            }
        });

        await readTask;
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            Log.Error($"ffmpeg exit code={process.ExitCode} stderr={errorLines}");
        }
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
