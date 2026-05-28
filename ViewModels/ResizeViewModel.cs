using System.Collections.ObjectModel;
using System.IO;
using RShiftTools.Models;
using RShiftTools.Services;

namespace RShiftTools.ViewModels;

public class ResizeViewModel : BaseViewModel
{
    public ObservableCollection<MediaFile> Files { get; } = [];
    public ObservableCollection<string> Presets { get; } =
    [
        "カスタム",
        "4K (3840×2160)",
        "1080p (1920×1080)",
        "720p (1280×720)",
        "480p (854×480)",
        "50%",
        "25%",
    ];
    private string _selectedPreset = "カスタム";
    public string SelectedPreset
    {
        get => _selectedPreset;
        set
        {
            _selectedPreset = value;
            OnPropertyChanged();
            ApplyPreset(value);
        }
    }

    private int _width = 1920;
    public int Width
    {
        get => _width;
        set
        {
            if (_width == value)
                return;
            _width = value;
            OnPropertyChanged();
            if (IsAspectLocked && _sourceWidth > 0 && _sourceHeight > 0)
            {
                _height = (int)Math.Round(value / _sourceAspect);
                OnPropertyChanged(nameof(Height));
            }
        }
    }

    private int _height = 1080;
    public int Height
    {
        get => _height;
        set
        {
            if (_height == value)
                return;
            _height = value;
            OnPropertyChanged();
            if (IsAspectLocked && _sourceWidth > 0 && _sourceHeight > 0)
            {
                _width = (int)Math.Round(value * _sourceAspect);
                OnPropertyChanged(nameof(Width));
            }
        }
    }

    private bool _isAspectLocked = true;
    public bool IsAspectLocked
    {
        get => _isAspectLocked;
        set
        {
            _isAspectLocked = value;
            OnPropertyChanged();
        }
    }

    public ObservableCollection<string> FitModes { get; } =
    ["stretch（そのまま）", "fit（letterbox）", "fill（クロップ）"];
    private string _fitMode = "fit（letterbox）";
    public string FitMode
    {
        get => _fitMode;
        set
        {
            _fitMode = value;
            OnPropertyChanged();
        }
    }

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
    public bool CanRun => !_isRunning && Files.Count > 0;

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

    private int _sourceWidth;
    private int _sourceHeight;
    private double _sourceAspect => _sourceHeight == 0 ? 0.0 : (double)_sourceWidth / _sourceHeight;

    private static readonly HashSet<string> ImageExts = MediaFormats.ImageExtensions;

    private readonly IDialogService _dialogService;

    public ResizeViewModel(List<string> filePaths, IDialogService dialogService)
    {
        _dialogService = dialogService;
        foreach (var path in filePaths)
            Files.Add(new MediaFile { FilePath = path });
    }

    public async Task InitAsync()
    {
        if (Files.Count == 0)
            return;
        var info = await App.Ffprobe.GetMediaInfoAsync(Files[0].FilePath);
        if (info != null && info.Width > 0)
        {
            _sourceWidth = info.Width;
            _sourceHeight = info.Height;
            _width = info.Width;
            _height = info.Height;
            OnPropertyChanged(nameof(Width));
            OnPropertyChanged(nameof(Height));
        }
    }

    private void ApplyPreset(string preset)
    {
        if (preset == "50%" || preset == "25%")
        {
            if (_sourceWidth == 0)
                return;
            var scale = preset == "50%" ? 0.5 : 0.25;
            _width = (int)(_sourceWidth * scale);
            _height = (int)(_sourceHeight * scale);
            OnPropertyChanged(nameof(Width));
            OnPropertyChanged(nameof(Height));
            return;
        }

        (int w, int h) = preset switch
        {
            "4K (3840×2160)" => (3840, 2160),
            "1080p (1920×1080)" => (1920, 1080),
            "720p (1280×720)" => (1280, 720),
            "480p (854×480)" => (854, 480),
            _ => (0, 0),
        };

        if (w == 0)
            return;

        if (IsAspectLocked && _sourceWidth > 0 && _sourceHeight > 0)
        {
            _width = w;
            _height = (int)Math.Round(w / _sourceAspect);
        }
        else
        {
            _width = w;
            _height = h;
        }
        OnPropertyChanged(nameof(Width));
        OnPropertyChanged(nameof(Height));
    }

    public async Task RunAsync()
    {
        IsRunning = true;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        var done = 0;

        string? multiOutputDir = null;
        if (Files.Count > 1)
        {
            var suggestedDir =
                System.IO.Path.GetDirectoryName(Files[0].FilePath) ?? "";
            multiOutputDir = await System.Windows.Application.Current.Dispatcher.InvokeAsync(
                () => _dialogService.AskOutputFolder(suggestedDir)
            );
            if (multiOutputDir == null)
            {
                foreach (var f in Files)
                {
                    f.Status = ProcessStatus.Cancelled;
                    f.ErrorMessage = AppStrings.Error_Cancelled;
                }
                StatusText = AppStrings.Status_Waiting;
                IsRunning = false;
                return;
            }
        }

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
                var isImage = ImageExts.Contains(
                    Path.GetExtension(file.FilePath).ToLowerInvariant()
                );

                var vfFilter = BuildVfFilter(Width, Height, FitMode);
                var ext = Path.GetExtension(file.FilePath).ToLowerInvariant();

                string? outputPath;
                if (multiOutputDir != null)
                {
                    outputPath = BuildUniqueOutputPath(multiOutputDir, file.FilePath, ext);
                }
                else
                {
                    outputPath = await System.Windows.Application.Current.Dispatcher.InvokeAsync(
                        () => _dialogService.AskOutputPath(file.FilePath, ext)
                    );
                }

                if (outputPath == null)
                {
                    file.Status = ProcessStatus.Cancelled;
                    file.ErrorMessage = AppStrings.Error_Cancelled;
                    done++;
                    continue;
                }

                var argsList = isImage
                    ? new List<string> { "-i", file.FilePath, "-vf", vfFilter, outputPath }
                    : new List<string>
                    {
                        "-i",
                        file.FilePath,
                        "-c:v",
                        _hwEncoder switch
                        {
                            "NVIDIA (nvenc)" => "h264_nvenc",
                            "AMD (amf)" => "h264_amf",
                            "Intel (qsv)" => "h264_qsv",
                            _ => "libx264",
                        },
                        "-vf",
                        vfFilter,
                        "-c:a",
                        "copy",
                        outputPath,
                    };

                var progress = new Progress<FfmpegProgress>(p =>
                {
                    file.Progress = p.Percent * 100;
                    TotalProgress = (done + p.Percent) / Files.Count * 100;
                });

                var (success, error) = await App.Ffmpeg.RunAsync(
                    argsList,
                    duration,
                    progress,
                    token
                );
                file.Status = success ? ProcessStatus.Done : ProcessStatus.Error;
                if (success)
                    _lastOutputDir = System.IO.Path.GetDirectoryName(outputPath);
                else
                    file.ErrorMessage = $"{AppStrings.Error_FfmpegFailed}\n{error}";
            }
            catch (OperationCanceledException)
            {
                file.Status = ProcessStatus.Cancelled;
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
        OnPropertyChanged(nameof(LastOutputDir));
        OnPropertyChanged(nameof(HasOutput));
        IsRunning = false;
    }

    public void Cancel() => _cts?.Cancel();

    private static string BuildVfFilter(int width, int height, string fitMode) =>
        fitMode switch
        {
            "stretch（そのまま）" => $"scale={width}:{height}",
            "fill（クロップ）" =>
                $"scale={width}:{height}:force_original_aspect_ratio=increase,crop={width}:{height}",
            _ =>
                $"scale={width}:{height}:force_original_aspect_ratio=decrease,pad={width}:{height}:(ow-iw)/2:(oh-ih)/2",
        };
}
