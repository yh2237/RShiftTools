using System.Collections.ObjectModel;
using System.IO;
using RShiftTools.Models;
using RShiftTools.Services;

namespace RShiftTools.ViewModels;

public class CutViewModel : BaseViewModel
{
    public MediaFile File { get; }

    private static readonly HashSet<string> AnimatedImageExts = [".gif", ".webp", ".apng"];
    public bool IsAnimatedImage =>
        AnimatedImageExts.Contains(Path.GetExtension(File.FilePath).ToLowerInvariant());
    public bool IsPreviewAvailable =>
        !AnimatedImageExts.Contains(Path.GetExtension(File.FilePath).ToLowerInvariant());

    private double _zoomLevel = 1.0;
    private const double BasePixelsPerSecond = 80.0;
    private double _minZoom = 0.1;
    public double ZoomLevel
    {
        get => _zoomLevel;
        set
        {
            _zoomLevel = Math.Max(_minZoom, Math.Min(100, value));
            OnPropertyChanged();
            OnPropertyChanged(nameof(PixelsPerSecond));
            OnPropertyChanged(nameof(TimelineWidth));
            OnPropertyChanged(nameof(InPointPosition));
            OnPropertyChanged(nameof(OutPointPosition));
            OnPropertyChanged(nameof(CurrentPosition));
            OnPropertyChanged(nameof(ZoomLabel));
            ZoomChanged?.Invoke(_zoomLevel);
        }
    }
    public double PixelsPerSecond => BasePixelsPerSecond * _zoomLevel;
    public double TimelineWidth => TotalSeconds * PixelsPerSecond + 40;
    public double InPointPosition => InPoint * PixelsPerSecond + 20;
    public double OutPointPosition => OutPoint * PixelsPerSecond + 20;
    public double CurrentPosition => _currentSeconds * PixelsPerSecond + 20;
    public string ZoomLabel => $"{_zoomLevel:F1}x";

    public void SetViewportWidth(double viewportWidth)
    {
        if (TotalSeconds > 0 && viewportWidth > 0)
        {
            _minZoom = (viewportWidth - 40) / (TotalSeconds * BasePixelsPerSecond);
            if (_zoomLevel < _minZoom)
                ZoomLevel = _minZoom;
            else
                OnPropertyChanged(nameof(ZoomLevel));
        }
    }

    public void ZoomIn() => ZoomLevel *= 2.0;
    public void ZoomOut() => ZoomLevel /= 2.0;
    public void ZoomInAt(double anchorSeconds)
    {
        var oldPps = PixelsPerSecond;
        ZoomIn();
        var newPps = PixelsPerSecond;
        var offset = (anchorSeconds * oldPps) - (anchorSeconds * newPps);
        ScrollRequested?.Invoke(offset);
    }
    public void ZoomOutAt(double anchorSeconds)
    {
        var oldPps = PixelsPerSecond;
        ZoomOut();
        var newPps = PixelsPerSecond;
        var offset = (anchorSeconds * oldPps) - (anchorSeconds * newPps);
        ScrollRequested?.Invoke(offset);
    }
    public void ZoomToInOut()
    {
        if (OutPoint > InPoint && OutPoint - InPoint > 0.001)
        {
            var range = OutPoint - InPoint;
            ZoomLevel = 750.0 / (range * BasePixelsPerSecond);
        }
    }

    public event Action<double>? ZoomChanged;
    public event Action<double>? ScrollRequested;

    private double _totalSeconds;
    public double TotalSeconds
    {
        get => _totalSeconds;
        set
        {
            _totalSeconds = value;
            OnPropertyChanged();
        }
    }

    private double _currentSeconds;
    public double CurrentSeconds
    {
        get => _currentSeconds;
        set
        {
            _currentSeconds = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentTimeText));
        }
    }
    public string CurrentTimeText
    {
        get => SecondsToText(_currentSeconds);
        set
        {
            if (TryParseTime(value, out var seconds))
                Seek(seconds);
            OnPropertyChanged();
        }
    }

    private double _inPoint;
    public double InPoint
    {
        get => _inPoint;
        set
        {
            _inPoint = Math.Max(0, Math.Min(value, _outPoint - 0.1));
            OnPropertyChanged();
            OnPropertyChanged(nameof(InPointText));
            OnPropertyChanged(nameof(CanRun));
        }
    }
    public string InPointText
    {
        get => SecondsToText(_inPoint);
        set
        {
            if (TryParseTime(value, out var seconds))
                InPoint = seconds;
            OnPropertyChanged();
        }
    }

    private double _outPoint;
    public double OutPoint
    {
        get => _outPoint;
        set
        {
            _outPoint = Math.Max(_inPoint + 0.1, Math.Min(value, _totalSeconds));
            OnPropertyChanged();
            OnPropertyChanged(nameof(OutPointText));
            OnPropertyChanged(nameof(CanRun));
        }
    }
    public string OutPointText
    {
        get => SecondsToText(_outPoint);
        set
        {
            if (TryParseTime(value, out var seconds))
                OutPoint = seconds;
            OnPropertyChanged();
        }
    }

    private bool _isPlaying;
    public bool IsPlaying
    {
        get => _isPlaying;
        set
        {
            _isPlaying = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PlayButtonText));
        }
    }
    public string PlayButtonText => _isPlaying ? "\uE769" : "\uE768";

    public List<double> SpeedOptions { get; } = [0.25, 0.5, 1.0, 1.5, 2.0];
    private double _playbackSpeed = 1.0;
    public double PlaybackSpeed
    {
        get => _playbackSpeed;
        set
        {
            _playbackSpeed = value;
            OnPropertyChanged();
            SpeedChanged?.Invoke(value);
        }
    }

    private double _volume = 100;
    public double Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Max(0, Math.Min(100, value));
            OnPropertyChanged();
            OnPropertyChanged(nameof(VolumeIcon));
            OnPropertyChanged(nameof(VolumeText));
            VolumeChanged?.Invoke(_volume / 100.0);
        }
    }
    public string VolumeIcon =>
        _volume == 0 ? "\uE74F"
        : _volume < 40 ? "\uE992"
        : _volume < 75 ? "\uE993"
        : "\uE994";
    public string VolumeText => $"{(int)_volume}%";

    private double _progress;
    public double Progress
    {
        get => _progress;
        set
        {
            _progress = value;
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
    public bool CanRun => !_isRunning && _outPoint > _inPoint;

    private int _crf = 18;
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

    private CancellationTokenSource? _cts;

    private string? _lastOutputDir;
    public string? LastOutputDir
    {
        get => _lastOutputDir;
        private set
        {
            _lastOutputDir = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasOutput));
        }
    }
    public bool HasOutput => !string.IsNullOrEmpty(_lastOutputDir);

    public event Action<double>? SpeedChanged;
    public event Action<double>? VolumeChanged;
    public event Action? PlayRequested;
    public event Action? PauseRequested;
    public event Action<double>? SeekRequested;

    private readonly IDialogService _dialogService;

    public CutViewModel(string filePath, IDialogService dialogService)
    {
        _dialogService = dialogService;
        File = new MediaFile { FilePath = filePath };
    }

    public async Task InitAsync()
    {
        var info = await App.Ffprobe.GetMediaInfoAsync(File.FilePath);
        if (info != null && info.DurationSeconds > 0)
        {
            TotalSeconds = info.DurationSeconds;
            OutPoint = info.DurationSeconds;
        }
        else if (IsAnimatedImage)
        {
            TotalSeconds = 60;
            OutPoint = 60;
        }
    }

    public void SetInPoint() => InPoint = _currentSeconds;

    public void SetOutPoint() => OutPoint = _currentSeconds;

    public void TogglePlay()
    {
        if (_isPlaying)
        {
            IsPlaying = false;
            PauseRequested?.Invoke();
        }
        else
        {
            IsPlaying = true;
            PlayRequested?.Invoke();
        }
    }

    public void Seek(double seconds)
    {
        CurrentSeconds = seconds;
        SeekRequested?.Invoke(seconds);
    }

    public async Task RunAsync()
    {
        if (IsRunning)
            return;
        IsRunning = true;
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        try
        {
            StatusText = "処理中...";
            var ext = Path.GetExtension(File.FilePath).ToLowerInvariant();

            var outputPath = await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                _dialogService.AskOutputPath(File.FilePath, ext)
            );

            if (outputPath == null)
            {
                StatusText = AppStrings.Error_Cancelled;
                return;
            }

            var inStr = SecondsToText(_inPoint);
            var cutDuration = _outPoint - _inPoint;
            var cutDurationStr = cutDuration.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);

            List<string> argsList;

            if (AnimatedImageExts.Contains(ext))
            {
                argsList = [
                    "-y", "-i", File.FilePath, "-ss", inStr, "-t", cutDurationStr,
                    "-an", "-vsync", "0"
                ];
                if (ext == ".gif")
                    argsList.AddRange(["-c:v", "gif", "-loop", "0"]);
                else if (ext == ".webp")
                    argsList.AddRange(["-c:v", "libwebp", "-loop", "0", "-lossless", "0"]);
                else
                    argsList.AddRange(["-c:v", "apng", "-plays", "0"]);
                argsList.Add(outputPath);
            }
            else if (MediaFormats.AudioExtensions.Contains(ext))
            {
                argsList = [
                    "-ss", inStr, "-i", File.FilePath, "-t", cutDurationStr,
                    "-vn", "-sn", "-map_metadata", "0", "-avoid_negative_ts", "make_zero"
                ];
                argsList.AddRange(MediaEncodingProfile.GetCutAudioArguments(ext));
                argsList.Add(outputPath);
            }
            else
            {
                var videoCodec = MediaEncodingProfile.GetCutVideoCodec(ext, _hwEncoder);

                argsList =
                [
                    "-y",
                    "-ss",
                    inStr,
                    "-i",
                    File.FilePath,
                    "-t",
                    cutDurationStr,
                    "-c:v",
                    videoCodec,
                ];
                argsList.AddRange(MediaEncodingProfile.GetVideoQualityArguments(videoCodec, _crf));
                argsList.AddRange(MediaEncodingProfile.GetCutAudioArguments(ext));
                argsList.Add(outputPath);
            }

            var progress = new Progress<FfmpegProgress>(p =>
            {
                Progress = p.Percent * 100;
            });

            var (success, error) = await App.Ffmpeg.RunWithHardwareFallbackAsync(
                argsList,
                cutDuration,
                progress,
                token
            );

            StatusText = success
                ? AppStrings.Status_Success
                : $"{AppStrings.Status_Error}: {error}";
            if (success)
                LastOutputDir = System.IO.Path.GetDirectoryName(outputPath);
            else
                System.Windows.MessageBox.Show(error, AppStrings.AppName);
        }
        catch (OperationCanceledException)
        {
            StatusText = AppStrings.Error_Cancelled;
        }
        catch (Exception ex)
        {
            StatusText = $"{AppStrings.Status_Error}: {ex.Message}";
        }
        finally
        {
            IsRunning = false;
        }
    }

    public void Cancel() => _cts?.Cancel();

    public static string SecondsToText(double seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds:D3}";
    }

    internal static bool TryParseTime(string text, out double seconds)
    {
        seconds = 0;
        if (TimeSpan.TryParseExact(text, @"hh\:mm\:ss\.fff", null, out var ts))
        {
            seconds = ts.TotalSeconds;
            return true;
        }
        return false;
    }
}
