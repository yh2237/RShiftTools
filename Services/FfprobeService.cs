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
    public int AudioBitDepth { get; init; }
    public int AudioChannels { get; init; }
    public string AudioSampleFormat { get; init; } = "";
    public string AudioChannelLayout { get; init; } = "";
    public int SubtitleStreamCount { get; init; }
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

    public async Task<MediaInfo?> GetMediaInfoAsync(
        string filePath,
        CancellationToken cancellationToken = default
    )
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
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var cancellationRegistration = timeout.Token.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to stop ffprobe: {ex.Message}");
            }
        });
        var jsonTask = process.StandardOutput.ReadToEndAsync();
        var errTask = process.StandardError.ReadToEndAsync();
        try
        {
            await Task.WhenAll(jsonTask, errTask, process.WaitForExitAsync(timeout.Token));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.WhenAll(
                    jsonTask,
                    errTask,
                    process.WaitForExitAsync(CancellationToken.None)
                );
            }
            catch { }
            Log.Error($"ffprobe timed out: {filePath}");
            return null;
        }
        cancellationToken.ThrowIfCancellationRequested();
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

    internal static MediaInfo? ParseMediaInfo(string filePath, string json)
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
            audioSampleRate = 0,
            audioBitDepth = 0,
            audioChannels = 0;
        var audioBitrateKbps = 0;
        var subtitleStreamCount = 0;
        string audioSampleFormat = "",
            audioChannelLayout = "";
        double frameRate = 0;

        foreach (var stream in streams.EnumerateArray())
        {
            var codecType = stream.TryGetProperty("codec_type", out var ct) ? ct.GetString() : null;

            if (codecType == "subtitle")
            {
                subtitleStreamCount++;
                continue;
            }

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

                audioSampleFormat = stream.TryGetProperty("sample_fmt", out var sf)
                    ? sf.GetString() ?? string.Empty
                    : string.Empty;
                audioChannels =
                    stream.TryGetProperty("channels", out var channels)
                    && channels.TryGetInt32(out var channelCount)
                        ? channelCount
                        : 0;
                audioChannelLayout = stream.TryGetProperty("channel_layout", out var layout)
                    ? layout.GetString() ?? string.Empty
                    : string.Empty;

                var bitsPerSample = ReadJsonInt(stream, "bits_per_sample");
                var bitsPerRawSample = ReadJsonInt(stream, "bits_per_raw_sample");
                audioBitDepth = InferAudioBitDepth(
                    audioSampleFormat,
                    bitsPerSample,
                    bitsPerRawSample,
                    audioCodec
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
            AudioBitDepth = audioBitDepth,
            AudioChannels = audioChannels,
            AudioSampleFormat = audioSampleFormat,
            AudioChannelLayout = audioChannelLayout,
            SubtitleStreamCount = subtitleStreamCount,
            AudioBitrateKbps = audioBitrateKbps,
            TotalBitrateKbps = totalBitrateKbps,
            Type = type,
        };
    }

    private static int ReadJsonInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
            return 0;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            return number;
        return value.ValueKind == JsonValueKind.String
            && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number)
                ? number
                : 0;
    }

    internal static int InferAudioBitDepth(
        string sampleFormat,
        int bitsPerSample,
        int bitsPerRawSample,
        string codec
    )
    {
        if (bitsPerRawSample > 0) return bitsPerRawSample;
        if (bitsPerSample > 0) return bitsPerSample;
        if (codec.Contains("s24", StringComparison.OrdinalIgnoreCase)) return 24;
        var hasMeaningfulPcmDepth = codec.StartsWith("pcm_", StringComparison.OrdinalIgnoreCase)
            || codec.Equals("flac", StringComparison.OrdinalIgnoreCase)
            || codec.Equals("alac", StringComparison.OrdinalIgnoreCase);
        if (!hasMeaningfulPcmDepth) return 0;
        if (sampleFormat.StartsWith("u8", StringComparison.OrdinalIgnoreCase)) return 8;
        if (sampleFormat.StartsWith("s16", StringComparison.OrdinalIgnoreCase)) return 16;
        if (sampleFormat.StartsWith("s32", StringComparison.OrdinalIgnoreCase)) return 32;
        if (sampleFormat.StartsWith("s64", StringComparison.OrdinalIgnoreCase)) return 64;
        if (sampleFormat.StartsWith("flt", StringComparison.OrdinalIgnoreCase)) return 32;
        if (sampleFormat.StartsWith("dbl", StringComparison.OrdinalIgnoreCase)) return 64;
        return 0;
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
