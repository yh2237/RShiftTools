using System.Globalization;

namespace RShiftTools.Services;

internal static class AudioEditProfile
{
    public const string Keep = "維持";
    public const string Bit16 = "16-bit integer";
    public const string Bit24 = "24-bit integer";
    public const string Bit32Float = "32-bit float";
    public const string DitherAuto = "自動（ビット深度を下げる場合）";
    public const string DitherNone = "なし";
    public const string DitherTriangular = "三角分布";
    public const string DitherTriangularHighPass = "高域三角分布";

    public static string GetOutputExtension(string outputFormat) =>
        outputFormat == "FLAC" ? ".flac" : ".wav";

    public static int GetTargetBitDepth(string selection, MediaInfo source) =>
        selection switch
        {
            Bit16 => 16,
            Bit24 => 24,
            Bit32Float => 32,
            _ => source.AudioBitDepth > 0 ? source.AudioBitDepth : 16,
        };

    public static List<string> BuildArguments(
        string inputPath,
        string outputPath,
        MediaInfo source,
        string outputFormat,
        string bitDepth,
        string sampleRate,
        string channels,
        string dither
    )
    {
        var targetBits = GetTargetBitDepth(bitDepth, source);
        var isFloat = bitDepth == Bit32Float
            || bitDepth == Keep
            && source.AudioSampleFormat.StartsWith("flt", StringComparison.OrdinalIgnoreCase);

        if (outputFormat == "FLAC" && (isFloat || targetBits > 24))
            throw new InvalidOperationException("FLACはこのビット深度を保存できません。24-bit以下を選択してください。");

        var args = new List<string>
        {
            "-i", inputPath,
            "-map_metadata", "0",
            "-vn",
        };

        var sampleRateValue = ParseSampleRate(sampleRate);
        if (sampleRateValue > 0)
            args.AddRange(["-ar", sampleRateValue.ToString(CultureInfo.InvariantCulture)]);

        var channelCount = channels switch
        {
            "Mono" => 1,
            "Stereo" => 2,
            _ => 0,
        };
        if (channelCount > 0)
            args.AddRange(["-ac", channelCount.ToString(CultureInfo.InvariantCulture)]);

        var sourceBitsForDither = source.AudioBitDepth > 0
            ? source.AudioBitDepth
            : source.AudioSampleFormat.StartsWith("flt", StringComparison.OrdinalIgnoreCase) ? 32
            : targetBits;
        var ditherMethod = ResolveDitherMethod(dither, sourceBitsForDither, targetBits);
        if (targetBits == 16 && ditherMethod != null)
            args.AddRange(["-af", $"aresample=osf=s16:dither_method={ditherMethod}"]);

        if (outputFormat == "FLAC")
        {
            args.AddRange(["-c:a", "flac"]);
            if (targetBits <= 16)
                args.AddRange(["-sample_fmt", "s16"]);
            else
                args.AddRange(["-sample_fmt", "s32", "-bits_per_raw_sample", "24"]);
        }
        else
        {
            var codec = isFloat
                ? "pcm_f32le"
                : targetBits <= 16 ? "pcm_s16le"
                : targetBits <= 24 ? "pcm_s24le"
                : "pcm_s32le";
            args.AddRange(["-c:a", codec]);
        }

        args.Add(outputPath);
        return args;
    }

    public static string FormatSourceDetails(MediaInfo info)
    {
        var bitDepth = info.AudioBitDepth > 0 ? $"{info.AudioBitDepth}-bit" : "bit深度不明";
        var sampleRate = info.AudioSampleRate > 0
            ? $"{info.AudioSampleRate / 1000.0:0.###} kHz"
            : "サンプルレート不明";
        var channels = info.AudioChannels > 0 ? $"{info.AudioChannels} ch" : "ch不明";
        return $"{bitDepth} / {sampleRate} / {channels} / {info.AudioCodec}";
    }

    private static int ParseSampleRate(string value) =>
        value switch
        {
            "44.1 kHz" => 44100,
            "48 kHz" => 48000,
            "88.2 kHz" => 88200,
            "96 kHz" => 96000,
            "176.4 kHz" => 176400,
            "192 kHz" => 192000,
            _ => 0,
        };

    private static string? ResolveDitherMethod(
        string selection,
        int sourceBitDepth,
        int targetBitDepth
    ) =>
        selection switch
        {
            DitherNone => null,
            DitherTriangular => "triangular",
            DitherTriangularHighPass => "triangular_hp",
            _ when sourceBitDepth > targetBitDepth => "triangular_hp",
            _ => null,
        };
}
