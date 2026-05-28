using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RShiftTools.Services;

public class MediaInfo
{
    public string FilePath { get; init; } = "";
    public double DurationSeconds { get; init; }
    public long FileSizeBytes { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public string VideoCodec { get; init; } = "";
    public string AudioCodec { get; init; } = "";
    public double VideoFrameRate { get; init; }
    public int AudioSampleRate { get; init; }
    public int AudioBitrateKbps { get; init; }
    public int TotalBitrateKbps { get; init; }
    public MediaType Type { get; init; }
}

public enum MediaType
{
    Video,
    Audio,
    Image,
    Unknown,
}

public class FfprobeService
{
    private readonly string _ffprobePath;
    private static readonly HashSet<string> ImageExts = MediaFormats.ImageExtensions;
    private static readonly HashSet<string> AudioExts = MediaFormats.AudioExtensions;

    public FfprobeService(string ffprobePath)
    {
        _ffprobePath = ffprobePath;
    }

    public async Task<MediaInfo?> GetMediaInfoAsync(string filePath)
    {
        var argList = new List<string>
        {
            "-v",
            "quiet",
            "-print_format",
            "json",
            "-show_format",
            "-show_streams",
            filePath,
        };
        Log.Debug($"Starting ffprobe: {_ffprobePath} {string.Join(' ', argList)}");
        using var process = ProcessHelper.StartProcess(
            _ffprobePath,
            argList,
            redirectStdOut: true,
            redirectStdErr: true
        );
        var jsonTask = process.StandardOutput.ReadToEndAsync();
        var errTask = process.StandardError.ReadToEndAsync();
        await Task.WhenAll(jsonTask, errTask, process.WaitForExitAsync());
        var json = jsonTask.Result;
        var err = errTask.Result;

        if (process.ExitCode != 0)
        {
            Log.Error($"ffprobe exit code={process.ExitCode} stderr={err}");
            return null;
        }
        if (string.IsNullOrWhiteSpace(json))
        {
            Log.Error($"ffprobe produced empty json. stderr={err}");
            return null;
        }

        return ParseMediaInfo(filePath, json);
    }

    private static MediaInfo? ParseMediaInfo(string filePath, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var format = root.GetProperty("format");
        var durationSeconds = 0.0;
        if (format.TryGetProperty("duration", out var dur) && dur.ValueKind == JsonValueKind.String)
            double.TryParse(
                dur.GetString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out durationSeconds
            );

        var fileSizeBytes = 0L;
        if (format.TryGetProperty("size", out var size) && size.ValueKind == JsonValueKind.String)
            long.TryParse(
                size.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out fileSizeBytes
            );

        var totalBitrateKbps = 0;
        if (
            format.TryGetProperty("bit_rate", out var bitRate)
            && bitRate.ValueKind == JsonValueKind.String
            && long.TryParse(
                bitRate.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var totalBitrateBps
            )
        )
        {
            totalBitrateKbps = (int)Math.Max(0, totalBitrateBps / 1000);
        }

        var streams = root.GetProperty("streams");
        string videoCodec = "",
            audioCodec = "";
        int width = 0,
            height = 0,
            audioSampleRate = 0;
        var audioBitrateKbps = 0;
        double frameRate = 0;

        foreach (var stream in streams.EnumerateArray())
        {
            var codecType = stream.TryGetProperty("codec_type", out var ct) ? ct.GetString() : null;

            if (codecType == "video" && videoCodec == "")
            {
                videoCodec = stream.TryGetProperty("codec_name", out var vc)
                    ? vc.GetString() ?? string.Empty
                    : string.Empty;
                width =
                    stream.TryGetProperty("width", out var w) && w.TryGetInt32(out var wi) ? wi : 0;
                height =
                    stream.TryGetProperty("height", out var h) && h.TryGetInt32(out var hi)
                        ? hi
                        : 0;

                if (
                    stream.TryGetProperty("r_frame_rate", out var fr)
                    && fr.ValueKind == JsonValueKind.String
                )
                {
                    var parts = (fr.GetString() ?? "0/1").Split('/');
                    if (
                        parts.Length == 2
                        && double.TryParse(
                            parts[0],
                            NumberStyles.Float,
                            CultureInfo.InvariantCulture,
                            out var num
                        )
                        && double.TryParse(
                            parts[1],
                            NumberStyles.Float,
                            CultureInfo.InvariantCulture,
                            out var den
                        )
                        && den != 0
                    )
                    {
                        frameRate = num / den;
                    }
                }
            }
            else if (codecType == "audio" && audioCodec == "")
            {
                audioCodec = stream.TryGetProperty("codec_name", out var ac)
                    ? ac.GetString() ?? string.Empty
                    : string.Empty;
                if (
                    stream.TryGetProperty("sample_rate", out var sr)
                    && sr.ValueKind == JsonValueKind.String
                )
                    int.TryParse(
                        sr.GetString(),
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out audioSampleRate
                    );

                if (
                    stream.TryGetProperty("bit_rate", out var abr)
                    && abr.ValueKind == JsonValueKind.String
                    && long.TryParse(
                        abr.GetString(),
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var audioBitrateBps
                    )
                )
                {
                    audioBitrateKbps = (int)Math.Max(0, audioBitrateBps / 1000);
                }
            }
        }

        var type = DetermineMediaType(filePath, videoCodec, audioCodec, durationSeconds);

        return new MediaInfo
        {
            FilePath = filePath,
            DurationSeconds = durationSeconds,
            FileSizeBytes = fileSizeBytes,
            Width = width,
            Height = height,
            VideoCodec = videoCodec,
            AudioCodec = audioCodec,
            VideoFrameRate = frameRate,
            AudioSampleRate = audioSampleRate,
            AudioBitrateKbps = audioBitrateKbps,
            TotalBitrateKbps = totalBitrateKbps,
            Type = type,
        };
    }

    private static MediaType DetermineMediaType(
        string filePath,
        string videoCodec,
        string audioCodec,
        double duration
    )
    {
        var ext = Path.GetExtension(filePath);

        if (ImageExts.Contains(ext))
            return MediaType.Image;
        if (AudioExts.Contains(ext))
            return MediaType.Audio;

        if (!string.IsNullOrEmpty(videoCodec))
            return MediaType.Video;
        if (!string.IsNullOrEmpty(audioCodec))
            return MediaType.Audio;

        return MediaType.Unknown;
    }
}
