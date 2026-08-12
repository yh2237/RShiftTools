namespace RShiftTools.Services;

internal static class MediaEncodingProfile
{
    public const string AutoCodec = "自動（互換性優先）";
    public const string H264Codec = "H.264";
    public const string H265Codec = "H.265";
    public const string Vp9Codec = "VP9";

    public static string GetResizeVideoCodec(string extension, string hardwareEncoder) =>
        extension.ToLowerInvariant() switch
        {
            ".webm" => "libvpx-vp9",
            ".avi" => "mpeg4",
            ".flv" => "flv",
            ".wmv" => "wmv2",
            _ => GetH264Codec(hardwareEncoder),
        };

    public static string GetCutVideoCodec(string extension, string hardwareEncoder) =>
        GetResizeVideoCodec(extension, hardwareEncoder);

    public static string GetH264Codec(string hardwareEncoder) =>
        hardwareEncoder switch
        {
            "NVIDIA (nvenc)" => "h264_nvenc",
            "AMD (amf)" => "h264_amf",
            "Intel (qsv)" => "h264_qsv",
            _ => "libopenh264",
        };

    public static string GetCompressionVideoCodec(
        string extension,
        string codecMode,
        string hardwareEncoder
    )
    {
        var ext = extension.ToLowerInvariant();
        if (codecMode == AutoCodec)
            return GetResizeVideoCodec(ext, hardwareEncoder);
        if (codecMode == Vp9Codec)
            return ext is ".webm" or ".mkv" or ".mp4" ? "libvpx-vp9" : GetResizeVideoCodec(ext, hardwareEncoder);
        if (codecMode == H265Codec)
        {
            if (ext == ".webm")
                return "libvpx-vp9";
            return hardwareEncoder switch
            {
                "NVIDIA (nvenc)" => "hevc_nvenc",
                "AMD (amf)" => "hevc_amf",
                "Intel (qsv)" => "hevc_qsv",
                _ => "libkvazaar",
            };
        }
        if (codecMode == H264Codec)
        {
            if (ext == ".webm")
                return "libvpx-vp9";
            return GetH264Codec(hardwareEncoder);
        }
        return GetResizeVideoCodec(ext, hardwareEncoder);
    }

    public static string? BuildCompressionFilter(
        int sourceWidth,
        int sourceHeight,
        string resolutionMode,
        int maxFrameRate,
        int estimatedVideoBitrateKbps = 0
    )
    {
        var filters = new List<string>();
        var target = resolutionMode switch
        {
            "1080p" => (1920, 1080),
            "720p" => (1280, 720),
            "480p" => (854, 480),
            _ => (0, 0),
        };

        if (resolutionMode == "自動（目標サイズ優先）")
        {
            var maxWidth = estimatedVideoBitrateKbps switch
            {
                > 0 and < 1200 when sourceWidth > 1280 => 854,
                > 0 and < 2500 when sourceWidth > 1920 => 1280,
                > 0 and < 5000 when sourceWidth > 2560 => 1920,
                _ => sourceWidth,
            };
            target = (maxWidth, 0);
        }

        if (target.Item1 > 0 && sourceWidth > 0 && sourceHeight > 0)
        {
            var width = Math.Min(sourceWidth, target.Item1);
            var height = Math.Min(sourceHeight, target.Item2 > 0 ? target.Item2 : int.MaxValue);
            var scale = Math.Min((double)width / sourceWidth, (double)height / sourceHeight);
            width = Math.Max(8, (int)Math.Floor(sourceWidth * scale / 8) * 8);
            height = Math.Max(8, (int)Math.Floor(sourceHeight * scale / 8) * 8);
            if (width < sourceWidth || height < sourceHeight)
                filters.Add($"scale={width}:{height}:flags=lanczos");
        }

        if (maxFrameRate > 0)
            filters.Add($"fps={maxFrameRate}");

        return filters.Count == 0 ? null : string.Join(',', filters);
    }

    public static List<string> GetVideoQualityArguments(string codec, int crf)
    {
        var value = Math.Clamp(crf, 0, 51).ToString();
        return codec switch
        {
            "h264_nvenc" or "hevc_nvenc" => ["-cq", value],
            "h264_amf" or "hevc_amf" => ["-qp_i", value, "-qp_p", value],
            "h264_qsv" or "hevc_qsv" => ["-global_quality", value],
            "libopenh264" => ["-q:v", Math.Clamp(crf, 1, 31).ToString()],
            "libkvazaar" => ["-kvazaar-params", $"qp={value}"],
            "libvpx-vp9" => ["-crf", value, "-b:v", "0"],
            "mpeg4" or "flv" or "wmv2" =>
                ["-q:v", Math.Clamp((int)Math.Round(crf / 4.0), 2, 15).ToString()],
            _ => ["-crf", value],
        };
    }

    public static List<string> GetCutAudioArguments(string extension) =>
        extension.ToLowerInvariant() switch
        {
            ".mp3" => ["-c:a", "libmp3lame", "-q:a", "2"],
            ".aac" => ["-c:a", "aac", "-b:a", "192k"],
            ".wav" => ["-c:a", "copy"],
            ".flac" => ["-c:a", "flac"],
            ".ogg" => ["-c:a", "libvorbis", "-q:a", "6"],
            ".opus" => ["-c:a", "libopus", "-b:a", "128k"],
            ".m4a" => ["-c:a", "aac", "-b:a", "192k"],
            ".wma" or ".wmv" => ["-c:a", "wmav2"],
            ".webm" => ["-c:a", "libopus", "-b:a", "128k"],
            ".avi" or ".flv" => ["-c:a", "libmp3lame", "-q:a", "2"],
            _ => ["-c:a", "aac"],
        };

    public static string GetCompressedAudioExtension(string inputExtension) =>
        inputExtension.ToLowerInvariant() switch
        {
            ".mp3" => ".mp3",
            ".ogg" => ".ogg",
            ".opus" => ".opus",
            ".aac" => ".aac",
            _ => ".m4a",
        };

    public static List<string> GetCompressedAudioArguments(string outputExtension, int bitrateKbps)
    {
        var bitrate = outputExtension.ToLowerInvariant() switch
        {
            ".mp3" => Math.Clamp(bitrateKbps, 32, 320),
            ".opus" => Math.Clamp(bitrateKbps, 16, 256),
            _ => Math.Clamp(bitrateKbps, 24, 512),
        };
        var codec = outputExtension.ToLowerInvariant() switch
        {
            ".mp3" => "libmp3lame",
            ".ogg" => "libvorbis",
            ".opus" => "libopus",
            ".wma" => "wmav2",
            _ => "aac",
        };
        return ["-c:a", codec, "-b:a", $"{bitrate}k"];
    }
}
