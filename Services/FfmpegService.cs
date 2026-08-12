using System.Diagnostics;
using System.Globalization;
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
    private const int MaxErrorLogChars = 128 * 1024;

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
        var argumentList = AddCommonArguments(arguments);
        var joined = string.Join(' ', argumentList);
        Log.Debug($"Starting ffmpeg: {_ffmpegPath} {joined}");
        using var process = ProcessHelper.StartProcess(
            _ffmpegPath,
            argumentList,
            redirectStdOut: false,
            redirectStdErr: true
        );

        var errorLines = new System.Text.StringBuilder();
        var readTask = Task.Run(async () =>
        {
            try
            {
                while (!process.StandardError.EndOfStream)
                {
                    var line = await process.StandardError.ReadLineAsync();
                    if (line == null)
                        break;

                    AppendBounded(errorLines, line);
                    ReportProgress(line, totalDurationSeconds, onProgress);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error reading ffmpeg stderr: {ex.Message}");
            }
        });

        using var reg = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to kill ffmpeg process: {ex.Message}");
            }
        });

        await Task.WhenAll(readTask, process.WaitForExitAsync(CancellationToken.None));
        cancellationToken.ThrowIfCancellationRequested();

        if (process.ExitCode != 0)
        {
            Log.Error($"ffmpeg exit code={process.ExitCode} stderr={errorLines}");
        }
        return (process.ExitCode == 0, errorLines.ToString());
    }

    public async Task<(bool Success, string ErrorLog)> RunWithHardwareFallbackAsync(
        IEnumerable<string> arguments,
        double totalDurationSeconds,
        IProgress<FfmpegProgress>? onProgress = null,
        CancellationToken cancellationToken = default
    )
    {
        var original = arguments.ToList();
        var first = await RunAsync(original, totalDurationSeconds, onProgress, cancellationToken);
        if (first.Success || cancellationToken.IsCancellationRequested)
            return first;

        var fallback = BuildSoftwareFallback(original);
        if (fallback == null)
            return first;

        Log.Info("Hardware encoder failed. Retrying with the software encoder.");
        var second = await RunAsync(fallback, totalDurationSeconds, onProgress, cancellationToken);
        if (second.Success)
            return second;

        return (
            false,
            $"{first.ErrorLog}\n--- CPU fallback ---\n{second.ErrorLog}"
        );
    }

    internal static List<string>? BuildSoftwareFallback(IReadOnlyList<string> arguments)
    {
        var result = new List<string>(arguments.Count);
        var changed = false;
        string? softwareCodec = null;
        for (var i = 0; i < arguments.Count; i++)
        {
            var replacement = arguments[i] switch
            {
                "h264_nvenc" or "h264_amf" or "h264_qsv" => "libopenh264",
                "hevc_nvenc" or "hevc_amf" or "hevc_qsv" => "libkvazaar",
                _ => null,
            };
            if (replacement != null)
            {
                result.Add(replacement);
                softwareCodec = replacement;
                changed = true;
                continue;
            }

            if (arguments[i] == "-qp_p" && i + 1 < arguments.Count)
            {
                i++;
                continue;
            }

            if (
                arguments[i] is "-cq" or "-global_quality" or "-qp_i"
                && i + 1 < arguments.Count
            )
            {
                var quality = arguments[++i];
                if (softwareCodec == "libkvazaar")
                {
                    result.Add("-kvazaar-params");
                    result.Add($"qp={quality}");
                }
                else
                {
                    result.Add("-q:v");
                    result.Add(
                        int.TryParse(quality, out var qualityNumber)
                            ? Math.Clamp(qualityNumber, 1, 31).ToString()
                            : quality
                    );
                }
                continue;
            }

            result.Add(arguments[i]);
        }

        if (!changed)
            return null;
        return result;
    }

    private static List<string> AddCommonArguments(IEnumerable<string> arguments)
    {
        var result = new List<string>
        {
            "-hide_banner",
            "-nostdin",
            "-y",
            "-progress",
            "pipe:2",
            "-nostats",
        };
        result.AddRange(arguments.Where(a => a is not "-y" and not "-n"));
        return result;
    }

    private static void AppendBounded(System.Text.StringBuilder buffer, string line)
    {
        buffer.AppendLine(line);
        if (buffer.Length > MaxErrorLogChars + 8192)
            buffer.Remove(0, buffer.Length - MaxErrorLogChars);
    }

    private static void ReportProgress(
        string line,
        double totalDurationSeconds,
        IProgress<FfmpegProgress>? onProgress
    )
    {
        if (onProgress == null || totalDurationSeconds <= 0)
            return;

        TimeSpan current;
        if (
            line.StartsWith("out_time_us=", StringComparison.Ordinal)
            && long.TryParse(line.AsSpan("out_time_us=".Length), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var microseconds)
        )
        {
            current = TimeSpan.FromTicks(microseconds * 10);
        }
        else
        {
            var match = TimeRegex.Match(line);
            if (!match.Success)
                return;
            if (!int.TryParse(match.Groups[1].Value, out var h)) h = 0;
            if (!int.TryParse(match.Groups[2].Value, out var m)) m = 0;
            if (!int.TryParse(match.Groups[3].Value, out var s)) s = 0;
            var frac = match.Groups[4].Value.PadRight(3, '0')[..3];
            if (!int.TryParse(frac, out var ms)) ms = 0;
            current = new TimeSpan(0, h, m, s, ms);
        }

        onProgress.Report(new FfmpegProgress
        {
            Percent = Math.Clamp(current.TotalSeconds / totalDurationSeconds, 0, 1),
            CurrentTime = current,
            RawLine = line,
        });
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
