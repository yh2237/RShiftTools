using System.Collections.ObjectModel;
using System.IO;
using RShiftTools.Models;
using RShiftTools.Services;

namespace RShiftTools.ViewModels;

public class ConvertViewModel : BaseViewModel
{
    public ObservableCollection<MediaFile> Files { get; } = [];
    public ObservableCollection<string> Formats { get; } = [];
    public ObservableCollection<string> EncodeModes { get; } =
    ["通常", "非圧縮コピー (-c copy)", "ロスレス (-crf 0)"];
    private string _encodeMode = "通常";
    public string EncodeMode
    {
        get => _encodeMode;
        set
        {
            _encodeMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsCrfEnabled));
        }
    }
    public bool IsCrfEnabled => EncodeMode == "通常";

    public ObservableCollection<string> HwEncoders { get; } =
    ["自動 (CPU)", "NVIDIA (nvenc)", "AMD (amf)", "Intel (qsv)"];
    private string _hwEncoder = UserSettings.HwEncoder;
    public string HwEncoder
    {
        get => _hwEncoder;
        set
        {
            _hwEncoder = value;
            OnPropertyChanged();
            UserSettings.HwEncoder = value;
        }
    }

    private int _crf = 23;
    public int Crf
    {
        get => _crf;
        set
        {
            _crf = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CrfLabel));
        }
    }
    public string CrfLabel => $"品質 (CRF): {_crf}";

    public ObservableCollection<string> SubtitleModes { get; } =
    ["コピー (-c:s copy)", "削除 (-sn)"];
    private string _subtitleMode = "コピー (-c:s copy)";
    public string SubtitleMode
    {
        get => _subtitleMode;
        set
        {
            _subtitleMode = value;
            OnPropertyChanged();
        }
    }

    private bool _isAdvancedOpen;
    public bool IsAdvancedOpen
    {
        get => _isAdvancedOpen;
        set
        {
            _isAdvancedOpen = value;
            OnPropertyChanged();
        }
    }

    private string _selectedFormat = "";
    public string SelectedFormat
    {
        get => _selectedFormat;
        set
        {
            _selectedFormat = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanRun));
        }
    }

    private double _totalProgress;
    public double TotalProgress
    {
        get => _totalProgress;
        set
        {
            _totalProgress = value;
            OnPropertyChanged();
        }
    }

    private string _statusText = AppStrings.Status_Waiting;
    public string StatusText
    {
        get => _statusText;
        set
        {
            _statusText = value;
            OnPropertyChanged();
        }
    }

    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        set
        {
            _isRunning = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanRun));
        }
    }
    public bool CanRun => !_isRunning && Files.Count > 0 && !string.IsNullOrEmpty(SelectedFormat);

    private CancellationTokenSource? _cts;

    private static readonly string[] VideoFormats =
    [
        "mp4 (H.264)",
        "mp4 (H.265)",
        "mkv",
        "mov",
        "webm",
        "avi",
        "gif",
        "mp3",
        "aac",
        "wav",
        "flac",
        "ogg",
        "opus",
        "m4a",
    ];
    private static readonly string[] AudioFormats =
    [
        "mp3",
        "aac",
        "wav",
        "flac",
        "ogg",
        "opus",
        "m4a",
    ];
    private static readonly string[] ImageFormats =
    [
        "jpg",
        "png",
        "webp",
        "gif",
        "bmp",
        "avif",
        "tiff",
    ];

    private static readonly HashSet<string> AudioExts =
    [
        ".mp3",
        ".aac",
        ".wav",
        ".flac",
        ".ogg",
        ".m4a",
        ".opus",
        ".wma",
    ];
    private static readonly HashSet<string> ImageExts =
    [
        ".jpg",
        ".jpeg",
        ".png",
        ".gif",
        ".webp",
        ".bmp",
        ".tiff",
        ".avif",
    ];
    private static readonly HashSet<string> VideoExts =
    [
        ".mp4",
        ".mkv",
        ".mov",
        ".avi",
        ".webm",
        ".flv",
        ".wmv",
        ".m4v",
    ];

    private readonly IDialogService _dialogService;

    public ConvertViewModel(List<string> filePaths, IDialogService dialogService)
    {
        _dialogService = dialogService;
        foreach (var path in filePaths)
            Files.Add(new MediaFile { FilePath = path });

        var ext = Path.GetExtension(filePaths[0]).ToLowerInvariant();
        var formats =
            AudioExts.Contains(ext) ? AudioFormats
            : ImageExts.Contains(ext) ? ImageFormats
            : VideoFormats;

        foreach (var f in formats)
            Formats.Add(f);
        SelectedFormat = Formats[0];
    }

    public async Task RunAsync()
    {
        IsRunning = true;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        var done = 0;

        foreach (var file in Files)
        {
            if (token.IsCancellationRequested)
                break;

            file.Status = ProcessStatus.Processing;
            StatusText = $"処理中: {file.FileName}";

            try
            {
                var info = await App.Ffprobe.GetMediaInfoAsync(file.FilePath);
                var duration = info?.DurationSeconds ?? 0;

                var (ext, argsList) = BuildArgumentsList(
                    file.FilePath,
                    SelectedFormat,
                    EncodeMode,
                    HwEncoder,
                    Crf,
                    SubtitleMode
                );

                var outputPath = await System.Windows.Application.Current.Dispatcher.InvokeAsync(
                    () =>
                        _dialogService.AskOutputPath(file.FilePath, ext)
                );

                if (outputPath == null)
                {
                    file.Status = ProcessStatus.Error;
                    file.ErrorMessage = AppStrings.Error_Cancelled;
                    done++;
                    continue;
                }

                argsList.Add(outputPath);

                var progress = new Progress<FfmpegProgress>(p =>
                {
                    file.Progress = p.Percent * 100;
                    TotalProgress = (done + p.Percent) / Files.Count * 100;
                });

                var (success, _) = await App.Ffmpeg.RunAsync(argsList, duration, progress, token);
                file.Status = success ? ProcessStatus.Done : ProcessStatus.Error;
                if (!success)
                    file.ErrorMessage = AppStrings.Error_FfmpegFailed;
            }
            catch (OperationCanceledException)
            {
                file.Status = ProcessStatus.Error;
                file.ErrorMessage = AppStrings.Error_Cancelled;
                break;
            }
            catch (Exception ex)
            {
                file.Status = ProcessStatus.Error;
                file.ErrorMessage = ex.Message;
            }

            done++;
            TotalProgress = (double)done / Files.Count * 100;
        }

        StatusText = string.Format(
            AppStrings.Status_CompleteFormat,
            Files.Count(f => f.Status == ProcessStatus.Done),
            Files.Count(f => f.Status == ProcessStatus.Error)
        );
        IsRunning = false;
    }

    public void Cancel() => _cts?.Cancel();

    private static (string ext, List<string> args) BuildArgumentsList(
        string inputPath,
        string format,
        string encodeMode,
        string hwEncoder,
        int crf,
        string subtitleMode
    )
    {
        var isVideo = VideoExts.Contains(Path.GetExtension(inputPath).ToLowerInvariant());

        if (encodeMode == "非圧縮コピー (-c copy)")
        {
            var copyExt = FormatToExt(format);
            return (copyExt, ["-i", inputPath, "-c", "copy"]);
        }

        var audioOnlyFormats = new HashSet<string>
        {
            "mp3",
            "aac",
            "wav",
            "flac",
            "ogg",
            "opus",
            "m4a",
        };
        if (isVideo && audioOnlyFormats.Contains(format))
        {
            return format switch
            {
                "mp3" => (".mp3", ["-i", inputPath, "-vn", "-c:a", "libmp3lame", "-q:a", "2"]),
                "aac" => (".aac", ["-i", inputPath, "-vn", "-c:a", "aac", "-b:a", "192k"]),
                "wav" => (".wav", ["-i", inputPath, "-vn", "-c:a", "pcm_s16le"]),
                "flac" => (".flac", ["-i", inputPath, "-vn", "-c:a", "flac"]),
                "ogg" => (".ogg", ["-i", inputPath, "-vn", "-c:a", "libvorbis", "-q:a", "6"]),
                "opus" => (".opus", ["-i", inputPath, "-vn", "-c:a", "libopus", "-b:a", "128k"]),
                "m4a" => (".m4a", ["-i", inputPath, "-vn", "-c:a", "aac", "-b:a", "192k"]),
                _ => (".mp3", ["-i", inputPath, "-vn", "-c:a", "libmp3lame", "-q:a", "2"]),
            };
        }

        string VideoCodec(string sw, string nv, string amd, string intel) =>
            hwEncoder switch
            {
                "NVIDIA (nvenc)" => nv,
                "AMD (amf)" => amd,
                "Intel (qsv)" => intel,
                _ => sw,
            };

        string QualityOpt() =>
            encodeMode == "ロスレス (-crf 0)"
                ? (hwEncoder == "自動 (CPU)" ? "-crf 0" : "-cq 0")
                : (hwEncoder == "自動 (CPU)" ? $"-crf {crf}" : $"-cq {crf}");

        var subOpt = subtitleMode == "削除 (-sn)" ? "-sn" : "-c:s copy";

        List<string> Args(string vc, string ac, string? extra = null)
        {
            var list = new List<string> { "-i", inputPath, "-c:v", vc };
            list.AddRange(QualityOpt().Split(' '));
            list.AddRange(["-c:a", ac]);
            list.AddRange(subOpt.Split(' '));
            if (extra != null)
                list.AddRange(extra.Split(' '));
            return list;
        }

        return format switch
        {
            "mp4 (H.264)" => (
                ".mp4",
                Args(VideoCodec("libx264", "h264_nvenc", "h264_amf", "h264_qsv"), "aac")
            ),
            "mp4 (H.265)" => (
                ".mp4",
                Args(VideoCodec("libx265", "hevc_nvenc", "hevc_amf", "hevc_qsv"), "aac")
            ),
            "mkv" => (
                ".mkv",
                Args(VideoCodec("libx265", "hevc_nvenc", "hevc_amf", "hevc_qsv"), "aac")
            ),
            "mov" => (
                ".mov",
                Args(VideoCodec("libx264", "h264_nvenc", "h264_amf", "h264_qsv"), "aac")
            ),
            "webm" => (
                ".webm",
                Args(VideoCodec("libvpx-vp9", "vp9_nvenc", "vp9_amf", "vp9_qsv"), "libopus")
            ),
            "avi" => (
                ".avi",
                Args(VideoCodec("libx264", "h264_nvenc", "h264_amf", "h264_qsv"), "mp3")
            ),
            "gif" => (".gif", ["-i", inputPath, "-vf", "fps=15,scale=480:-1:flags=lanczos"]),
            "mp3" => (".mp3", ["-i", inputPath, "-c:a", "libmp3lame", "-q:a", "2"]),
            "aac" => (".aac", ["-i", inputPath, "-c:a", "aac", "-b:a", "192k"]),
            "wav" => (".wav", ["-i", inputPath, "-c:a", "pcm_s16le"]),
            "flac" => (".flac", ["-i", inputPath, "-c:a", "flac"]),
            "ogg" => (".ogg", ["-i", inputPath, "-c:a", "libvorbis", "-q:a", "6"]),
            "opus" => (".opus", ["-i", inputPath, "-c:a", "libopus", "-b:a", "128k"]),
            "m4a" => (".m4a", ["-i", inputPath, "-c:a", "aac", "-b:a", "192k"]),
            "jpg" => (".jpg", ["-i", inputPath, "-q:v", "3"]),
            "png" => (".png", ["-i", inputPath]),
            "webp" => (".webp", ["-i", inputPath, "-q:v", "80"]),
            "bmp" => (".bmp", ["-i", inputPath]),
            "avif" => (".avif", ["-i", inputPath, "-c:v", "libaom-av1"]),
            "tiff" => (".tiff", ["-i", inputPath]),
            _ => (".mp4", ["-i", inputPath, "-c:v", "libx264", $"-crf {crf}", "-c:a", "aac"]),
        };
    }

    private static string FormatToExt(string format) =>
        format switch
        {
            "mp4 (H.264)" or "mp4 (H.265)" => ".mp4",
            "jpg" => ".jpg",
            "m4a" => ".m4a",
            _ => $".{format}",
        };
}
