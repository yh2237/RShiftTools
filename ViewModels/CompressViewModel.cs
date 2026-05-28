using System.Collections.ObjectModel;
using System.IO;
using RShiftTools.Models;
using RShiftTools.Services;

namespace RShiftTools.ViewModels;

public class CompressViewModel : BaseViewModel
{
    public ObservableCollection<MediaFile> Files { get; } = [];

    public bool IsImageMode { get; }
    public bool IsAudioMode { get; }
    public bool IsVideoMode => !IsImageMode && !IsAudioMode;

    private double _targetSizeMb = 50;
    public double TargetSizeMb
    {
        get => _targetSizeMb;
        set
        {
            _targetSizeMb = value;
            OnPropertyChanged();
            UpdateEstimate();
        }
    }

    public ObservableCollection<string> AudioQualities { get; } =
    ["低 (96kbps)", "中 (128kbps)", "高 (192kbps)", "コピー"];

    private string _audioQuality = "中 (128kbps)";
    public string AudioQuality
    {
        get => _audioQuality;
        set
        {
            _audioQuality = value;
            OnPropertyChanged();
            UpdateEstimate();
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

    private string _estimateText = "";
    public string EstimateText
    {
        get => _estimateText;
        set
        {
            _estimateText = value;
            OnPropertyChanged();
        }
    }

    private int _imageQuality = 5;
    public int ImageQuality
    {
        get => _imageQuality;
        set
        {
            _imageQuality = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ImageQualityLabel));
        }
    }

    public string ImageQualityLabel
    {
        get
        {
            var ext = Files.Count > 0
                ? Path.GetExtension(Files[0].FilePath).ToLowerInvariant()
                : "";
            if (ext == ".png")
                return $"圧縮レベル: {_imageQuality}  (0=無圧縮 / 9=最大圧縮)";
            return $"画質: {_imageQuality}  (2=高品質 / 31=低品質)";
        }
    }

    public int ImageQualityMin => 2;
    public int ImageQualityMax
    {
        get
        {
            var ext = Files.Count > 0
                ? Path.GetExtension(Files[0].FilePath).ToLowerInvariant()
                : "";
            return ext == ".png" ? 9 : 31;
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

    private readonly Dictionary<string, double> _durationCache = [];
    private readonly Dictionary<string, int> _audioBitrateCacheKbps = [];
    private readonly IDialogService _dialogService;

    public CompressViewModel(List<string> filePaths, IDialogService dialogService)
    {
        _dialogService = dialogService;
        foreach (var path in filePaths)
            Files.Add(new MediaFile { FilePath = path });

        var firstExt = filePaths.Count > 0
            ? Path.GetExtension(filePaths[0]).ToLowerInvariant()
            : "";
        IsImageMode = MediaFormats.ImageExtensions.Contains(firstExt);
        IsAudioMode = MediaFormats.AudioExtensions.Contains(firstExt);
    }

    public async Task InitAsync()
    {
        if (Files.Count == 0)
            return;

        if (IsImageMode)
        {
            var ext = Path.GetExtension(Files[0].FilePath).ToLowerInvariant();
            _imageQuality = ext == ".png" ? 6 : 10;
            OnPropertyChanged(nameof(ImageQuality));
            OnPropertyChanged(nameof(ImageQualityLabel));
            OnPropertyChanged(nameof(ImageQualityMin));
            OnPropertyChanged(nameof(ImageQualityMax));
            return;
        }

        if (IsAudioMode)
        {
            var audioInfo = await App.Ffprobe.GetMediaInfoAsync(Files[0].FilePath);
            if (audioInfo != null)
            {
                _durationCache[Files[0].FilePath] = audioInfo.DurationSeconds;
                UpdateEstimate();
            }
            return;
        }

        var info = await App.Ffprobe.GetMediaInfoAsync(Files[0].FilePath);
        if (info != null)
        {
            _durationCache[Files[0].FilePath] = info.DurationSeconds;
            _audioBitrateCacheKbps[Files[0].FilePath] = info.AudioBitrateKbps;
            UpdateEstimate();
        }
    }

    private void UpdateEstimate()
    {
        if (IsImageMode || Files.Count == 0)
            return;

        if (IsAudioMode)
        {
            if (_durationCache.TryGetValue(Files[0].FilePath, out var dur) && dur > 0)
            {
                var audioTargetBits = _targetSizeMb * 8 * 1024 * 1024;
                var audioBitrate = (int)(audioTargetBits / dur / 1000);
                EstimateText = audioBitrate > 0
                    ? $"推定音声ビットレート：{audioBitrate} kbps"
                    : "目標サイズが小さすぎます";
            }
            return;
        }

        if (!_durationCache.TryGetValue(Files[0].FilePath, out var duration) || duration <= 0)
        {
            EstimateText = "推定ビットレート：計算中...";
            return;
        }

        var estimatedAudioBitrateKbps = GetEstimatedAudioBitrateKbps(Files[0].FilePath);

        var targetBits = _targetSizeMb * 8 * 1024 * 1024;
        var videoBitrate = (int)(targetBits / duration / 1000) - estimatedAudioBitrateKbps;

        if (videoBitrate <= 0)
        {
            EstimateText = "目標サイズが小さすぎます";
            return;
        }

        var audioDisplay =
            AudioQuality == "コピー"
                ? $"コピー（入力推定: {estimatedAudioBitrateKbps} kbps）"
                : $"{estimatedAudioBitrateKbps} kbps";

        EstimateText =
            $"推定映像ビットレート：{videoBitrate} kbps  /  音声：{audioDisplay}";
    }

    private int GetEstimatedAudioBitrateKbps(string filePath)
    {
        if (AudioQuality == "コピー")
        {
            if (_audioBitrateCacheKbps.TryGetValue(filePath, out var cachedCopy) && cachedCopy > 0)
                return cachedCopy;

            return 128;
        }

        return AudioQuality switch
        {
            "低 (96kbps)" => 96,
            "中 (128kbps)" => 128,
            "高 (192kbps)" => 192,
            _ => 128,
        };
    }

    public async Task RunAsync()
    {
        if (IsImageMode)
            await RunImageAsync();
        else if (IsAudioMode)
            await RunAudioAsync();
        else
            await RunVideoAsync();
    }

    private async Task RunImageAsync()
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
            file.Progress = 0;

            try
            {
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

                List<string> argsList = BuildImageArgs(file.FilePath, ext, outputPath);

                var (success, error) = await App.Ffmpeg.RunAsync(argsList, 0, null, token);

                file.Progress = 100;
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
                Log.Error($"Compress image error: {ex}");
                ShowError($"画像圧縮エラー:\n{ex.Message}");
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

    private async Task RunAudioAsync()
    {
        IsRunning = true;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        var done = 0;

        string? multiOutputDir = null;
        if (Files.Count > 1)
        {
            var suggestedDir = Path.GetDirectoryName(Files[0].FilePath) ?? "";
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
                var duration = _durationCache.GetValueOrDefault(file.FilePath, 0);
                if (duration <= 0)
                {
                    var mediaInfo = await App.Ffprobe.GetMediaInfoAsync(file.FilePath);
                    if (mediaInfo != null)
                    {
                        duration = mediaInfo.DurationSeconds;
                        _durationCache[file.FilePath] = duration;
                    }
                }
                if (duration <= 0)
                {
                    file.Status = ProcessStatus.Error;
                    file.ErrorMessage = "音声の長さを取得できませんでした";
                    continue;
                }

                var targetBitsAudio = _targetSizeMb * 8 * 1024 * 1024;
                var audioBitrateKbps = (int)(targetBitsAudio / duration / 1000);
                if (audioBitrateKbps <= 0)
                {
                    file.Status = ProcessStatus.Error;
                    file.ErrorMessage = "目標サイズが小さすぎます";
                    continue;
                }

                var ext = Path.GetExtension(file.FilePath).ToLowerInvariant();
                string? outputPath;
                if (multiOutputDir != null)
                    outputPath = BuildUniqueOutputPath(multiOutputDir, file.FilePath, ext);
                else
                    outputPath = await System.Windows.Application.Current.Dispatcher.InvokeAsync(
                        () => _dialogService.AskOutputPath(file.FilePath, ext)
                    );

                if (outputPath == null)
                {
                    file.Status = ProcessStatus.Cancelled;
                    file.ErrorMessage = AppStrings.Error_Cancelled;
                    done++;
                    continue;
                }

                var audioCodec = AudioQuality == "コピー" ? "copy" : "aac";
                var audioArgs = AudioQuality == "コピー"
                    ? new[] { "-c:a", "copy" }
                    : new[] { "-c:a", "aac", "-b:a", $"{audioBitrateKbps}k" };

                var argsList = new List<string>
                {
                    "-y",
                    "-i", file.FilePath,
                    "-vn",
                };
                argsList.AddRange(audioArgs);
                argsList.Add(outputPath);

                var progress = new Progress<FfmpegProgress>(p =>
                {
                    file.Progress = p.Percent * 100;
                    TotalProgress = (done + p.Percent) / Files.Count * 100;
                });

                var (success, error) = await App.Ffmpeg.RunAsync(argsList, duration, progress, token);

                file.Status = success ? ProcessStatus.Done : ProcessStatus.Error;
                if (success)
                    _lastOutputDir = Path.GetDirectoryName(outputPath);
                else
                    file.ErrorMessage = $"エンコード失敗:\n{error}";
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
                Log.Error($"Compress audio error: {ex}");
                ShowError($"音声圧縮エラー:\n{ex.Message}");
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

    private List<string> BuildImageArgs(string inputPath, string ext, string outputPath)
    {
        return ext switch
        {
            ".png" => ["-y", "-i", inputPath, "-compression_level", _imageQuality.ToString(), outputPath],
            ".webp" => ["-y", "-i", inputPath, "-q:v", _imageQuality.ToString(), outputPath],
            ".avif" => ["-y", "-i", inputPath, "-crf", _imageQuality.ToString(), "-c:v", "libaom-av1", outputPath],
            _ =>
                ["-y", "-i", inputPath, "-q:v", _imageQuality.ToString(), outputPath],
        };
    }

    private async Task RunVideoAsync()
    {
        Log.Info("Compress RunVideoAsync started");
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
                var duration = _durationCache.GetValueOrDefault(file.FilePath, 0);
                if (duration <= 0)
                {
                    Log.Info($"Getting media info for: {file.FilePath}");
                    var info2 = await App.Ffprobe.GetMediaInfoAsync(file.FilePath);
                    if (info2 != null)
                    {
                        duration = info2.DurationSeconds;
                        _durationCache[file.FilePath] = duration;
                        _audioBitrateCacheKbps[file.FilePath] = info2.AudioBitrateKbps;
                    }
                    Log.Info($"Duration result: {duration}");
                }
                if (duration <= 0)
                {
                    file.Status = ProcessStatus.Error;
                    file.ErrorMessage = "動画の長さを取得できませんでした";
                    Log.Error($"Zero duration for: {file.FilePath}");
                    ShowError($"動画の長さを取得できませんでした。\nファイル: {file.FilePath}");
                    continue;
                }

                var audioBitrateKbps = GetEstimatedAudioBitrateKbps(file.FilePath);
                var targetBits = _targetSizeMb * 8 * 1024 * 1024;
                var videoBitrateKbps = (int)(targetBits / duration / 1000) - audioBitrateKbps;

                Log.Info($"Bitrate calc: targetMb={_targetSizeMb}, dur={duration}, audio={audioBitrateKbps}, video={videoBitrateKbps}");

                if (videoBitrateKbps <= 0)
                {
                    file.Status = ProcessStatus.Error;
                    file.ErrorMessage = "目標サイズが小さすぎます";
                    ShowError($"目標サイズが小さすぎます。\n動画長: {duration:F1} 秒\n目標: {_targetSizeMb} MB\n最低: {audioBitrateKbps + 1} kbps");
                    continue;
                }

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

                var videoCodec = _hwEncoder switch
                {
                    "NVIDIA (nvenc)" => "h264_nvenc",
                    "AMD (amf)" => "h264_amf",
                    "Intel (qsv)" => "h264_qsv",
                    _ => "libx264",
                };

                StatusText = $"処理中: {file.FileName}";

                var audioArgs =
                    AudioQuality == "コピー"
                        ? new[] { "-c:a", "copy" }
                        : new[] { "-c:a", "aac", "-b:a", $"{audioBitrateKbps}k" };

                var encodeArgsList = new List<string>
                {
                    "-y",
                    "-i", file.FilePath,
                    "-c:v", videoCodec,
                    "-b:v", videoBitrateKbps + "k",
                };
                encodeArgsList.AddRange(audioArgs);
                encodeArgsList.Add(outputPath);

                Log.Info($"Running ffmpeg: codec={videoCodec}, output={outputPath}");

                var progressFinal = new Progress<FfmpegProgress>(p =>
                {
                    file.Progress = p.Percent * 100;
                    TotalProgress = (done + p.Percent) / Files.Count * 100;
                });

                var (success, error) = await App.Ffmpeg.RunAsync(
                    encodeArgsList,
                    duration,
                    progressFinal,
                    token
                );

                if (!success && videoCodec != "libx264")
                {
                    Log.Info($"HW encoder failed, retrying with CPU: {file.FilePath}");
                    var cpuArgsList = new List<string>
                    {
                        "-y",
                        "-i", file.FilePath,
                        "-c:v", "libx264",
                        "-b:v", videoBitrateKbps + "k",
                    };
                    cpuArgsList.AddRange(audioArgs);
                    cpuArgsList.Add(outputPath);

                    var (cpuOk, cpuError) = await App.Ffmpeg.RunAsync(
                        cpuArgsList,
                        duration,
                        progressFinal,
                        token
                    );

                    if (cpuOk)
                    {
                        success = true;
                        error = "";
                    }
                    else
                    {
                        error = $"{error}\n\nCPUフォールバック失敗:\n{cpuError}";
                    }
                }

                file.Status = success ? ProcessStatus.Done : ProcessStatus.Error;
                if (success)
                    _lastOutputDir = System.IO.Path.GetDirectoryName(outputPath);
                else
                {
                    var msg = string.IsNullOrWhiteSpace(error)
                        ? $"エンコード失敗 (stderr空, exit code非ゼロ)\n入力: {file.FilePath}\n出力: {outputPath}"
                        : $"エンコード失敗:\n{error}";
                    file.ErrorMessage = msg;
                    Log.Error($"Encode failed: {msg}");
                    ShowError(msg);
                }
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
                Log.Error($"Compress exception: {ex}");
                ShowError($"圧縮中に例外:\n{ex.Message}");
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
        Log.Info("Compress RunVideoAsync finished");
    }

    public void Cancel() => _cts?.Cancel();

    private static void ShowError(string message)
    {
        try
        {
            System.Windows.Application.Current.Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Normal,
                new Action(() =>
                {
                    System.Windows.MessageBox.Show(
                        message,
                        AppStrings.AppName,
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error
                    );
                })
            );
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to show error popup: {ex.Message}");
        }
    }
}