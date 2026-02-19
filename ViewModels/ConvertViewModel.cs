using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using RShiftTools.Models;
using RShiftTools.Services;

namespace RShiftTools.ViewModels;

public class ConvertViewModel : INotifyPropertyChanged
{
    public ObservableCollection<MediaFile> Files { get; } = [];
    public ObservableCollection<string> Formats { get; } = [];
    public ObservableCollection<string> EncodeModes { get; } = ["通常", "非圧縮コピー (-c copy)", "ロスレス (-crf 0)"];
    private string _encodeMode = "通常";
    public string EncodeMode
    {
        get => _encodeMode;
        set { _encodeMode = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsCrfEnabled)); }
    }
    public bool IsCrfEnabled => EncodeMode == "通常";
    public ObservableCollection<string> HwEncoders { get; } = ["自動 (CPU)", "NVIDIA (nvenc)", "AMD (amf)", "Intel (qsv)"];
    private string _hwEncoder = "自動 (CPU)";
    public string HwEncoder
    {
        get => _hwEncoder;
        set { _hwEncoder = value; OnPropertyChanged(); }
    }
    private int _crf = 23;
    public int Crf
    {
        get => _crf;
        set { _crf = value; OnPropertyChanged(); OnPropertyChanged(nameof(CrfLabel)); }
    }
    public string CrfLabel => $"品質 (CRF): {_crf}";
    public ObservableCollection<string> SubtitleModes { get; } = ["コピー (-c:s copy)", "削除 (-sn)"];
    private string _subtitleMode = "コピー (-c:s copy)";
    public string SubtitleMode
    {
        get => _subtitleMode;
        set { _subtitleMode = value; OnPropertyChanged(); }
    }
    private bool _isAdvancedOpen = false;
    public bool IsAdvancedOpen
    {
        get => _isAdvancedOpen;
        set { _isAdvancedOpen = value; OnPropertyChanged(); }
    }

    private string _selectedFormat = "";
    public string SelectedFormat
    {
        get => _selectedFormat;
        set { _selectedFormat = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanRun)); }
    }

    private double _totalProgress;
    public double TotalProgress
    {
        get => _totalProgress;
        set { _totalProgress = value; OnPropertyChanged(); }
    }

    private string _statusText = "待機中";
    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnPropertyChanged(); }
    }

    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        set { _isRunning = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanRun)); }
    }
    public bool CanRun => !_isRunning && Files.Count > 0 && !string.IsNullOrEmpty(SelectedFormat);

    private CancellationTokenSource? _cts;

    private static readonly string[] VideoFormats = ["mp4 (H.264)", "mp4 (H.265)", "mkv", "mov", "webm", "avi", "gif", "mp3", "aac", "wav", "flac", "ogg", "opus", "m4a"];
    private static readonly string[] AudioFormats = ["mp3", "aac", "wav", "flac", "ogg", "opus", "m4a"];
    private static readonly string[] ImageFormats = ["jpg", "png", "webp", "gif", "bmp", "avif", "tiff"];

    private static readonly HashSet<string> AudioExts = [".mp3", ".aac", ".wav", ".flac", ".ogg", ".m4a", ".opus", ".wma"];
    private static readonly HashSet<string> ImageExts = [".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".tiff", ".avif"];
    private static readonly HashSet<string> VideoExts = [".mp4", ".mkv", ".mov", ".avi", ".webm", ".flv", ".wmv", ".m4v"];

    public ConvertViewModel(List<string> filePaths)
    {
        foreach (var path in filePaths)
            Files.Add(new MediaFile { FilePath = path });

        var ext = Path.GetExtension(filePaths[0]).ToLowerInvariant();
        var formats = AudioExts.Contains(ext) ? AudioFormats
                    : ImageExts.Contains(ext) ? ImageFormats
                    : VideoFormats;

        foreach (var f in formats) Formats.Add(f);
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
            if (token.IsCancellationRequested) break;

            file.Status = ProcessStatus.Processing;
            StatusText = $"処理中: {file.FileName}";

            try
            {
                var info = await App.Ffprobe.GetMediaInfoAsync(file.FilePath);
                var duration = info?.DurationSeconds ?? 0;

                var (ext, ffmpegArgs) = BuildArguments(file.FilePath, SelectedFormat, EncodeMode, HwEncoder, Crf, SubtitleMode);

                var rawOutput = Path.Combine(
                    Path.GetDirectoryName(file.FilePath)!,
                    Path.GetFileNameWithoutExtension(file.FilePath) + ext);

                var outputPath = await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => DialogService.AskOutputPath(file.FilePath, ext));

                if (outputPath == null)
                {
                    file.Status = ProcessStatus.Error;
                    file.ErrorMessage = "キャンセルされました";
                    done++;
                    continue;
                }
                var fullArgs = $"{ffmpegArgs} \"{outputPath}\"";

                var progress = new Progress<FfmpegProgress>(p =>
                {
                    file.Progress = p.Percent * 100;
                    TotalProgress = (done + p.Percent) / Files.Count * 100;
                });

                var (success, _) = await App.Ffmpeg.RunAsync(fullArgs, duration, progress, token);
                file.Status = success ? ProcessStatus.Done : ProcessStatus.Error;
                if (!success) file.ErrorMessage = "ffmpeg がエラーを返しました";
            }
            catch (OperationCanceledException)
            {
                file.Status = ProcessStatus.Error;
                file.ErrorMessage = "キャンセルされました";
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

        StatusText = $"完了: {Files.Count(f => f.Status == ProcessStatus.Done)} 件成功  /  {Files.Count(f => f.Status == ProcessStatus.Error)} 件失敗";
        IsRunning = false;
    }

    public void Cancel() => _cts?.Cancel();

    private static (string ext, string args) BuildArguments(
        string inputPath, string format, string encodeMode, string hwEncoder, int crf, string subtitleMode)
    {
        var input = $"-i \"{inputPath}\"";
        var isVideo = VideoExts.Contains(Path.GetExtension(inputPath).ToLowerInvariant());

        if (encodeMode == "非圧縮コピー (-c copy)")
        {
            var copyExt = FormatToExt(format);
            return (copyExt, $"{input} -c copy");
        }

        var audioOnlyFormats = new HashSet<string> { "mp3", "aac", "wav", "flac", "ogg", "opus", "m4a" };
        if (isVideo && audioOnlyFormats.Contains(format))
        {
            return format switch
            {
                "mp3" => (".mp3", $"{input} -vn -c:a libmp3lame -q:a 2"),
                "aac" => (".aac", $"{input} -vn -c:a aac -b:a 192k"),
                "wav" => (".wav", $"{input} -vn -c:a pcm_s16le"),
                "flac" => (".flac", $"{input} -vn -c:a flac"),
                "ogg" => (".ogg", $"{input} -vn -c:a libvorbis -q:a 6"),
                "opus" => (".opus", $"{input} -vn -c:a libopus -b:a 128k"),
                "m4a" => (".m4a", $"{input} -vn -c:a aac -b:a 192k"),
                _ => (".mp3", $"{input} -vn -c:a libmp3lame -q:a 2"),
            };
        }

        string VideoCodec(string sw, string nv, string amd, string intel) => hwEncoder switch
        {
            "NVIDIA (nvenc)" => nv,
            "AMD (amf)" => amd,
            "Intel (qsv)" => intel,
            _ => sw,
        };

        string QualityOpt(string codec) => encodeMode == "ロスレス (-crf 0)"
            ? (hwEncoder == "自動 (CPU)" ? "-crf 0" : "-cq 0")
            : (hwEncoder == "自動 (CPU)" ? $"-crf {crf}" : $"-cq {crf}");

        var subOpt = subtitleMode == "削除 (-sn)" ? "-sn" : "-c:s copy";

        return format switch
        {
            "mp4 (H.264)" => (".mp4", $"{input} -c:v {VideoCodec("libx264", "h264_nvenc", "h264_amf", "h264_qsv")} {QualityOpt("libx264")} -c:a aac {subOpt}"),
            "mp4 (H.265)" => (".mp4", $"{input} -c:v {VideoCodec("libx265", "hevc_nvenc", "hevc_amf", "hevc_qsv")} {QualityOpt("libx265")} -c:a aac {subOpt}"),
            "mkv" => (".mkv", $"{input} -c:v {VideoCodec("libx265", "hevc_nvenc", "hevc_amf", "hevc_qsv")} {QualityOpt("libx265")} -c:a aac {subOpt}"),
            "mov" => (".mov", $"{input} -c:v {VideoCodec("libx264", "h264_nvenc", "h264_amf", "h264_qsv")} {QualityOpt("libx264")} -c:a aac {subOpt}"),
            "webm" => (".webm", $"{input} -c:v {VideoCodec("libvpx-vp9", "vp9_nvenc", "vp9_amf", "vp9_qsv")} {QualityOpt("libvpx-vp9")} -c:a libopus {subOpt}"),
            "avi" => (".avi", $"{input} -c:v {VideoCodec("libx264", "h264_nvenc", "h264_amf", "h264_qsv")} {QualityOpt("libx264")} -c:a mp3 {subOpt}"),
            "gif" => (".gif", $"{input} -vf \"fps=15,scale=480:-1:flags=lanczos\""),
            "mp3" => (".mp3", $"{input} -c:a libmp3lame -q:a 2"),
            "aac" => (".aac", $"{input} -c:a aac -b:a 192k"),
            "wav" => (".wav", $"{input} -c:a pcm_s16le"),
            "flac" => (".flac", $"{input} -c:a flac"),
            "ogg" => (".ogg", $"{input} -c:a libvorbis -q:a 6"),
            "opus" => (".opus", $"{input} -c:a libopus -b:a 128k"),
            "m4a" => (".m4a", $"{input} -c:a aac -b:a 192k"),
            "jpg" => (".jpg", $"{input} -q:v 3"),
            "png" => (".png", $"{input}"),
            "webp" => (".webp", $"{input} -q:v 80"),
            "bmp" => (".bmp", $"{input}"),
            "avif" => (".avif", $"{input} -c:v libaom-av1"),
            "tiff" => (".tiff", $"{input}"),
            _ => (".mp4", $"{input} -c:v libx264 -crf {crf} -c:a aac"),
        };
    }

    private static string FormatToExt(string format) => format switch
    {
        "mp4 (H.264)" or "mp4 (H.265)" => ".mp4",
        "jpg" => ".jpg",
        "m4a" => ".m4a",
        _ => $".{format}",
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}