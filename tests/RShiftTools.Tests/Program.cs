using RShiftTools.Services;
using RShiftTools.ViewModels;

var tests = new (string Name, Action Run)[]
{
    ("Media kind classification", TestMediaKinds),
    ("Container-compatible video codecs", TestVideoCodecs),
    ("Hardware quality options", TestQualityOptions),
    ("Audio compression output profiles", TestAudioProfiles),
    ("Hardware fallback argument conversion", TestHardwareFallback),
    ("Audio bit depth inference", TestAudioBitDepthInference),
    ("Audio edit arguments", TestAudioEditArguments),
    ("Convert copy remux rules", TestCopyRemuxRules),
    ("Compress container-safe codecs", TestContainerSafeCodecs),
    ("Audio cut profiles", TestAudioCutProfiles),
    ("Target size planning", TestTargetSizePlanning),
    ("Compression profile options", TestCompressionProfileOptions),
    ("Probe JSON with string bit depth", TestProbeStringBitDepth),
    ("E2E audio edit real ffmpeg", TestE2EAudioEdit),
};

var failed = 0;
foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.Error.WriteLine($"FAIL {test.Name}: {ex.Message}");
    }
}

return failed == 0 ? 0 : 1;

static void TestMediaKinds()
{
    Equal(MediaFormats.MediaKind.Video, MediaFormats.GetKind("movie.webm"));
    Equal(MediaFormats.MediaKind.Audio, MediaFormats.GetKind("sample.WAV"));
    Equal(MediaFormats.MediaKind.Image, MediaFormats.GetKind("image.png"));
    Equal(MediaFormats.MediaKind.Unknown, MediaFormats.GetKind("notes.txt"));
}

static void TestVideoCodecs()
{
    Equal("libvpx-vp9", MediaEncodingProfile.GetResizeVideoCodec(".webm", "NVIDIA (nvenc)"));
    Equal("mpeg4", MediaEncodingProfile.GetResizeVideoCodec(".avi", "NVIDIA (nvenc)"));
    Equal("wmv2", MediaEncodingProfile.GetCutVideoCodec(".wmv", "Intel (qsv)"));
    Equal("h264_nvenc", MediaEncodingProfile.GetResizeVideoCodec(".mp4", "NVIDIA (nvenc)"));
    Equal("libopenh264", MediaEncodingProfile.GetResizeVideoCodec(".mp4", "自動 (CPU)"));
}

static void TestQualityOptions()
{
    SequenceEqual(["-cq", "20"], MediaEncodingProfile.GetVideoQualityArguments("h264_nvenc", 20));
    SequenceEqual(
        ["-qp_i", "20", "-qp_p", "20"],
        MediaEncodingProfile.GetVideoQualityArguments("h264_amf", 20)
    );
    SequenceEqual(
        ["-global_quality", "20"],
        MediaEncodingProfile.GetVideoQualityArguments("h264_qsv", 20)
    );
    SequenceEqual(
        ["-kvazaar-params", "qp=28"],
        MediaEncodingProfile.GetVideoQualityArguments("libkvazaar", 28)
    );
}

static void TestAudioProfiles()
{
    Equal(".m4a", MediaEncodingProfile.GetCompressedAudioExtension(".wav"));
    Equal(".mp3", MediaEncodingProfile.GetCompressedAudioExtension(".mp3"));
    SequenceEqual(
        ["-c:a", "libopus", "-b:a", "256k"],
        MediaEncodingProfile.GetCompressedAudioArguments(".opus", 999)
    );
    SequenceEqual(
        ["-c:a", "wmav2", "-b:a", "128k"],
        MediaEncodingProfile.GetCompressedAudioArguments(".wma", 128)
    );
}

static void TestAudioCutProfiles()
{
    SequenceEqual(
        ["-c:a", "copy"],
        MediaEncodingProfile.GetCutAudioArguments(".wav")
    );
    SequenceEqual(
        ["-c:a", "libmp3lame", "-q:a", "2"],
        MediaEncodingProfile.GetCutAudioArguments(".mp3")
    );
}

static void TestHardwareFallback()
{
    var fallback = FfmpegService.BuildSoftwareFallback(
        ["-i", "input.mp4", "-c:v", "h264_amf", "-qp_i", "21", "-qp_p", "21", "out.mp4"]
    );
    NotNull(fallback);
    SequenceEqual(
        ["-i", "input.mp4", "-c:v", "libopenh264", "-q:v", "21", "out.mp4"],
        fallback!
    );

    var hevcFallback = FfmpegService.BuildSoftwareFallback(
        ["-i", "input.mp4", "-c:v", "hevc_qsv", "-global_quality", "25", "out.mp4"]
    );
    NotNull(hevcFallback);
    SequenceEqual(
        ["-i", "input.mp4", "-c:v", "libkvazaar", "-kvazaar-params", "qp=25", "out.mp4"],
        hevcFallback!
    );
}

static void TestAudioBitDepthInference()
{
    Equal(24, FfprobeService.InferAudioBitDepth("s32", 0, 24, "pcm_s24le"));
    Equal(16, FfprobeService.InferAudioBitDepth("s16", 0, 0, "pcm_s16le"));
    Equal(32, FfprobeService.InferAudioBitDepth("flt", 0, 0, "pcm_f32le"));
    Equal(0, FfprobeService.InferAudioBitDepth("fltp", 0, 0, "aac"));
}

static void TestAudioEditArguments()
{
    var source24 = new MediaInfo
    {
        AudioBitDepth = 24,
        AudioSampleFormat = "s32",
        AudioSampleRate = 48000,
        AudioChannels = 2,
        AudioCodec = "pcm_s24le",
    };
    var wav16 = AudioEditProfile.BuildArguments(
        "input.wav",
        "output.wav",
        source24,
        "WAV",
        AudioEditProfile.Bit16,
        AudioEditProfile.Keep,
        AudioEditProfile.Keep,
        AudioEditProfile.DitherAuto
    );
    ContainsSequence(wav16, ["-af", "aresample=osf=s16:dither_method=triangular_hp"]);
    ContainsSequence(wav16, ["-c:a", "pcm_s16le"]);

    var flac24 = AudioEditProfile.BuildArguments(
        "input.wav",
        "output.flac",
        source24,
        "FLAC",
        AudioEditProfile.Bit24,
        "96 kHz",
        "Mono",
        AudioEditProfile.DitherNone
    );
    ContainsSequence(flac24, ["-ar", "96000"]);
    ContainsSequence(flac24, ["-ac", "1"]);
    ContainsSequence(flac24, ["-sample_fmt", "s32", "-bits_per_raw_sample", "24"]);

    var rejected = false;
    try
    {
        AudioEditProfile.BuildArguments(
            "input.wav", "output.flac", source24, "FLAC", AudioEditProfile.Bit32Float,
            AudioEditProfile.Keep, AudioEditProfile.Keep, AudioEditProfile.DitherNone
        );
    }
    catch (InvalidOperationException)
    {
        rejected = true;
    }
    Equal(true, rejected);

    rejected = false;
    try
    {
        AudioEditProfile.BuildArguments(
            "input.wav", "output.flac",
            new MediaInfo { AudioBitDepth = 32, AudioSampleFormat = "s32", AudioCodec = "pcm_s32le" },
            "FLAC", AudioEditProfile.Keep, AudioEditProfile.Keep,
            AudioEditProfile.Keep, AudioEditProfile.DitherNone
        );
    }
    catch (InvalidOperationException)
    {
        rejected = true;
    }
    Equal(true, rejected);
}

static void TestTargetSizePlanning()
{
    Equal(50_000_000L, TargetSizePlanner.GetTargetBytes(50, TargetSizePlanner.DecimalMegabytes));
    Equal(52_428_800L, TargetSizePlanner.GetTargetBytes(50, TargetSizePlanner.BinaryMegabytes));

    var initial = TargetSizePlanner.CalculateInitialVideoBitrateKbps(
        10_000_000,
        10,
        128
    );
    Equal(7832, initial);

    var adjusted = TargetSizePlanner.AdjustVideoBitrateKbps(
        initial,
        128,
        10_000_000,
        11_000_000
    );
    Equal(true, adjusted > 0 && adjusted < initial);
    Equal(128, TargetSizePlanner.AdjustBitrateKbps(256, 5_000_000, 9_900_000));
}

static void TestCompressionProfileOptions()
{
    Equal("libvpx-vp9", MediaEncodingProfile.GetCompressionVideoCodec(".webm", MediaEncodingProfile.H264Codec, "自動 (CPU)"));
    Equal("libkvazaar", MediaEncodingProfile.GetCompressionVideoCodec(".mp4", MediaEncodingProfile.H265Codec, "自動 (CPU)"));
    Equal("libvpx-vp9", MediaEncodingProfile.GetCompressionVideoCodec(".webm", MediaEncodingProfile.Vp9Codec, "自動 (CPU)"));

    Equal(
        "scale=1280:720:flags=lanczos,fps=30",
        MediaEncodingProfile.BuildCompressionFilter(3840, 2160, "720p", 30)
    );
    Equal(null, MediaEncodingProfile.BuildCompressionFilter(1280, 720, "維持", 0));
    Equal(
        "scale=1280:720:flags=lanczos",
        MediaEncodingProfile.BuildCompressionFilter(3840, 2160, "自動（目標サイズ優先）", 0, 2000)
    );
}

static void TestCopyRemuxRules()
{
    var h264mp4 = new MediaInfo { VideoCodec = "h264", AudioCodec = "aac" };
    var h264flac = new MediaInfo { VideoCodec = "h264", AudioCodec = "flac" };
    var vp9 = new MediaInfo { VideoCodec = "vp9", AudioCodec = "opus" };
    var pcm = new MediaInfo { AudioCodec = "pcm_s24le" };
    var opus = new MediaInfo { AudioCodec = "opus" };

    Equal(true, ConvertViewModel.CanCopyRemux(h264mp4, "mkv"));
    Equal(true, ConvertViewModel.CanCopyRemux(h264mp4, "mov"));
    Equal(true, ConvertViewModel.CanCopyRemux(h264mp4, "mp4 (H.264)"));
    Equal(false, ConvertViewModel.CanCopyRemux(h264flac, "mp4 (H.264)"));
    Equal(false, ConvertViewModel.CanCopyRemux(vp9, "mp4 (H.264)"));
    Equal(false, ConvertViewModel.CanCopyRemux(vp9, "webm"));
    Equal(true, ConvertViewModel.CanCopyRemux(pcm, "wav"));
    Equal(false, ConvertViewModel.CanCopyRemux(opus, "wav"));
    Equal(false, ConvertViewModel.CanCopyRemux(null, "mkv"));
}

static void TestContainerSafeCodecs()
{
    Equal(
        "wmv2",
        MediaEncodingProfile.GetCompressionVideoCodec(".wmv", MediaEncodingProfile.H265Codec, "NVIDIA (nvenc)")
    );
    Equal(
        "wmv2",
        MediaEncodingProfile.GetCompressionVideoCodec(".wmv", MediaEncodingProfile.H264Codec, "NVIDIA (nvenc)")
    );
    Equal(
        "flv",
        MediaEncodingProfile.GetCompressionVideoCodec(".flv", MediaEncodingProfile.H265Codec, "NVIDIA (nvenc)")
    );
    Equal(
        "hevc_nvenc",
        MediaEncodingProfile.GetCompressionVideoCodec(".mp4", MediaEncodingProfile.H265Codec, "NVIDIA (nvenc)")
    );
}

static void TestProbeStringBitDepth()
{
    const string json = """
        {
          "format": {
            "filename": "Vocal_only - FX.wav",
            "duration": "76.680833",
            "size": "22084262",
            "bit_rate": "2304018"
          },
          "streams": [
            {
              "index": 0,
              "codec_name": "pcm_s24le",
              "codec_type": "audio",
              "sample_fmt": "s32",
              "sample_rate": "48000",
              "channels": 2,
              "bits_per_sample": 24,
              "bits_per_raw_sample": "24"
            }
          ]
        }
        """;
    var info = FfprobeService.ParseMediaInfo("Vocal_only - FX.wav", json);
    NotNull(info);
    Equal(24, info!.AudioBitDepth);
    Equal(48000, info.AudioSampleRate);
    Equal(2, info.AudioChannels);
    Equal("pcm_s24le", info.AudioCodec);
}

static void TestE2EAudioEdit()
{
    var root = AppContext.BaseDirectory;
    var ffmpeg = Path.Combine(root, "ffmpeg.exe");
    var ffprobe = Path.Combine(root, "ffprobe.exe");
    if (!File.Exists(ffmpeg) || !File.Exists(ffprobe))
    {
        Console.WriteLine("   e2e skipped (ffmpeg not present)");
        return;
    }

    var temp = Path.Combine(Path.GetTempPath(), "rshift-e2e-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(temp);
    try
    {
        var input = Path.Combine(temp, "src.wav");
        var ff = new FfmpegService(ffmpeg);
        var (ok, err) = ff.RunAsync(
            ["-f", "lavfi", "-i", "sine=frequency=440:duration=2", "-ar", "44100", "-c:a", "pcm_s16le", input],
            0, null).GetAwaiter().GetResult();
        if (!ok) throw new InvalidOperationException($"make src failed: {err}");

        var probe = new FfprobeService(ffprobe);
        var info = probe.GetMediaInfoAsync(input).GetAwaiter().GetResult();
        if (info == null) throw new InvalidOperationException("probe returned null");

        var cases = new (string fmt, string bit, string rate, string ch, string dither, string outName)[]
        {
            ("WAV", AudioEditProfile.Bit16, AudioEditProfile.Keep, AudioEditProfile.Keep, AudioEditProfile.DitherAuto, "c1.wav"),
            ("WAV", AudioEditProfile.Bit32Float, "96 kHz", AudioEditProfile.Keep, AudioEditProfile.DitherNone, "c2.wav"),
            ("FLAC", AudioEditProfile.Bit16, AudioEditProfile.Keep, AudioEditProfile.Keep, AudioEditProfile.DitherAuto, "c3.flac"),
            ("FLAC", AudioEditProfile.Bit24, "48 kHz", "Stereo", AudioEditProfile.DitherNone, "c4.flac"),
            ("WAV", AudioEditProfile.Keep, "48 kHz", "Mono", AudioEditProfile.DitherAuto, "c5.wav"),
        };
        foreach (var (fmt, bit, rate, ch, dither, outName) in cases)
        {
            var output = Path.Combine(temp, outName);
            var args = AudioEditProfile.BuildArguments(input, output, info, fmt, bit, rate, ch, dither);
            var (s, e) = ff.RunAsync(args, info.DurationSeconds, null).GetAwaiter().GetResult();
            if (!s || !File.Exists(output))
                throw new InvalidOperationException($"{fmt}/{bit}/{rate}/{ch}/{dither} FAILED: {e}");
            Console.WriteLine($"   e2e ok: {outName}");
        }
    }
    finally
    {
        Directory.Delete(temp, true);
    }
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"expected={expected}, actual={actual}");
}

static void SequenceEqual(IEnumerable<string> expected, IEnumerable<string> actual)
{
    if (!expected.SequenceEqual(actual))
        throw new InvalidOperationException(
            $"expected=[{string.Join(", ", expected)}], actual=[{string.Join(", ", actual)}]"
        );
}

static void ContainsSequence(IReadOnlyList<string> actual, IReadOnlyList<string> expected)
{
    for (var start = 0; start <= actual.Count - expected.Count; start++)
    {
        var found = true;
        for (var offset = 0; offset < expected.Count; offset++)
            found &= actual[start + offset] == expected[offset];
        if (found)
            return;
    }
    throw new InvalidOperationException(
        $"sequence [{string.Join(", ", expected)}] not found in [{string.Join(", ", actual)}]"
    );
}

static void NotNull(object? value)
{
    if (value == null)
        throw new InvalidOperationException("value was null");
}
