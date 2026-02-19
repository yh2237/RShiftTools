using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;

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
    public MediaType Type { get; init; }
}

public enum MediaType { Video, Audio, Image, Unknown }

public class FfprobeService
{
    private readonly string _ffprobePath;

    public FfprobeService(string ffprobePath)
    {
        _ffprobePath = ffprobePath;
    }

    public async Task<MediaInfo?> GetMediaInfoAsync(string filePath)
    {
        var args = $"-v quiet -print_format json -show_format -show_streams \"{filePath}\"";

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _ffprobePath,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(_ffprobePath) ?? "",
            }
        };

        var ffmpegDir = Path.GetDirectoryName(_ffprobePath) ?? "";
        process.StartInfo.Environment["PATH"] = ffmpegDir + ";" + Environment.GetEnvironmentVariable("PATH");

        process.Start();
        var json = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(json))
            return null;

        return ParseMediaInfo(filePath, json);
    }

    private static MediaInfo? ParseMediaInfo(string filePath, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var format = root.GetProperty("format");
        var durationSeconds = format.TryGetProperty("duration", out var dur)
            ? double.Parse(dur.GetString() ?? "0", System.Globalization.CultureInfo.InvariantCulture)
            : 0;
        var fileSizeBytes = format.TryGetProperty("size", out var size)
            ? long.Parse(size.GetString() ?? "0")
            : 0;

        var streams = root.GetProperty("streams");
        string videoCodec = "", audioCodec = "";
        int width = 0, height = 0, audioSampleRate = 0;
        double frameRate = 0;

        foreach (var stream in streams.EnumerateArray())
        {
            var codecType = stream.TryGetProperty("codec_type", out var ct)
                ? ct.GetString() : null;

            if (codecType == "video" && videoCodec == "")
            {
                videoCodec = stream.TryGetProperty("codec_name", out var vc)
                    ? vc.GetString() ?? "" : "";
                width = stream.TryGetProperty("width", out var w) ? w.GetInt32() : 0;
                height = stream.TryGetProperty("height", out var h) ? h.GetInt32() : 0;

                if (stream.TryGetProperty("r_frame_rate", out var fr))
                {
                    var parts = (fr.GetString() ?? "0/1").Split('/');
                    if (parts.Length == 2 && double.TryParse(parts[0], out var num)
                        && double.TryParse(parts[1], out var den) && den != 0)
                        frameRate = num / den;
                }
            }
            else if (codecType == "audio" && audioCodec == "")
            {
                audioCodec = stream.TryGetProperty("codec_name", out var ac)
                    ? ac.GetString() ?? "" : "";
                audioSampleRate = stream.TryGetProperty("sample_rate", out var sr)
                    ? int.Parse(sr.GetString() ?? "0") : 0;
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
            Type = type,
        };
    }

    private static MediaType DetermineMediaType(string filePath, string videoCodec, string audioCodec, double duration)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();

        var imageExts = new HashSet<string> { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".tiff", ".avif" };
        if (imageExts.Contains(ext)) return MediaType.Image;

        var audioExts = new HashSet<string> { ".mp3", ".aac", ".wav", ".flac", ".ogg", ".m4a", ".opus", ".wma" };
        if (audioExts.Contains(ext)) return MediaType.Audio;

        if (!string.IsNullOrEmpty(videoCodec)) return MediaType.Video;
        if (!string.IsNullOrEmpty(audioCodec)) return MediaType.Audio;

        return MediaType.Unknown;
    }
}