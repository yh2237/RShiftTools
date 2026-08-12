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
    public bool IsSizeMode => !IsImageMode;

    public ObservableCollection<string> ResolutionModes { get; } =
        ["維持", "自動（目標サイズ優先）", "1080p", "720p", "480p"];
    private string _resolutionMode = "自動（目標サイズ優先）";
    public string ResolutionMode
    {
        get => _resolutionMode;
        set { _resolutionMode = value; OnPropertyChanged(); }
    }

    public ObservableCollection<string> VideoCodecs { get; } =
        [MediaEncodingProfile.AutoCodec, MediaEncodingProfile.H264Codec, MediaEncodingProfile.H265Codec, MediaEncodingProfile.Vp9Codec];
    private string _videoCodec = MediaEncodingProfile.AutoCodec;
    public string VideoCodec
    {
        get => _videoCodec;
        set { _videoCodec = value; OnPropertyChanged(); }
    }

    public ObservableCollection<string> FrameRateCaps { get; } =
        ["維持", "60 fps", "30 fps", "24 fps", "15 fps"];
    private string _frameRateCap = "維持";
    public string FrameRateCap
    {
        get => _frameRateCap;
        set { _frameRateCap = value; OnPropertyChanged(); }
    }

    public ObservableCollection<string> SubtitleModes { get; } = ["維持", "削除"];
    private string _subtitleMode = "維持";
    public string SubtitleMode
    {
        get => _subtitleMode;
        set { _subtitleMode = value; OnPropertyChanged(); }
    }

    public ObservableCollection<string> MetadataModes { get; } = ["維持", "削除"];
    private string _metadataMode = "維持";
    public string MetadataMode
    {
        get => _metadataMode;
        set { _metadataMode = value; OnPropertyChanged(); }
    }

    private bool _imageTargetEnabled;
    public bool ImageTargetEnabled
    {
        get => _imageTargetEnabled;
        set { _imageTargetEnabled = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanRun)); }
    }
    private double _imageTargetSizeKb = 500;
    public double ImageTargetSizeKb
    {
        get => _imageTargetSizeKb;
        set { _imageTargetSizeKb = Math.Max(1, value); OnPropertyChanged(); }
    }

    public ObservableCollection<string> SizeUnits { get; } =
        [TargetSizePlanner.DecimalMegabytes, TargetSizePlanner.BinaryMegabytes];
    private string _selectedSizeUnit = TargetSizePlanner.DecimalMegabytes;
    public string SelectedSizeUnit
    {
        get => _selectedSizeUnit;
        set
        {
            _selectedSizeUnit = value;
            OnPropertyChanged();
            UpdateEstimate();
        }
    }

    public ObservableCollection<string> AccuracyModes { get; } =
        ["高精度（最大3回）", "高速（1回）"];
    private string _accuracyMode = "高精度（最大3回）";
    public string AccuracyMode
    {
        get => _accuracyMode;
        set { _accuracyMode = value; OnPropertyChanged(); }
    }
    private int MaximumAttempts => AccuracyMode == "高速（1回）" ? 1 : 3;

    private bool _skipFilesAlreadyWithinTarget = true;
    public bool SkipFilesAlreadyWithinTarget
    {
        get => _skipFilesAlreadyWithinTarget;
        set { _skipFilesAlreadyWithinTarget = value; OnPropertyChanged(); }
    }

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
    ["低 (96kbps)", "中 (128kbps)", "高 (192kbps)", "コピー", "音声無し"];

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
            _imageQuality = Math.Max(ImageQualityMin, Math.Min(ImageQualityMax, value));
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
    public bool HasCompatibleInputTypes { get; }
    public bool CanRun => !_isRunning && HasCompatibleInputTypes && Files.Count > 0;

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
    private readonly Dictionary<string, MediaInfo> _videoInfoCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly IDialogService _dialogService;

    public CompressViewModel(List<string> filePaths, IDialogService dialogService)
    {
        _dialogService = dialogService;
        foreach (var path in filePaths)
            Files.Add(new MediaFile { FilePath = path, Details = "" });

        HasCompatibleInputTypes = filePaths
            .Select(MediaFormats.GetKind)
            .Distinct()
            .Count() <= 1;
        if (!HasCompatibleInputTypes)
            StatusText = AppStrings.Error_MixedMediaTypes;

        var firstExt = filePaths.Count > 0
            ? Path.GetExtension(filePaths[0]).ToLowerInvariant()
            : "";
        IsImageMode = MediaFormats.ImageExtensions.Contains(firstExt);
        IsAudioMode = MediaFormats.AudioExtensions.Contains(firstExt);
        if (IsAudioMode)
            _targetSizeMb = 5;
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
            _videoInfoCache[Files[0].FilePath] = info;
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
                var audioTargetBytes = TargetSizePlanner.GetTargetBytes(_targetSizeMb, SelectedSizeUnit);
                var audioBitrate = TargetSizePlanner.CalculateAudioBitrateKbps(audioTargetBytes, dur);
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

        var targetBytes = TargetSizePlanner.GetTargetBytes(_targetSizeMb, SelectedSizeUnit);
        var videoBitrate = TargetSizePlanner.CalculateInitialVideoBitrateKbps(
            targetBytes,
            duration,
            estimatedAudioBitrateKbps
        );

        if (videoBitrate <= 0)
        {
            EstimateText = "目標サイズが小さすぎます";
            return;
        }

        var audioDisplay =
            AudioQuality == "コピー"
                ? $"コピー（入力推定: {estimatedAudioBitrateKbps} kbps）"
                : AudioQuality == "音声無し"
                    ? "無し"
                    : $"{estimatedAudioBitrateKbps} kbps";

        EstimateText =
            $"推定映像ビットレート：{videoBitrate} kbps  /  音声：{audioDisplay}  /  目標以下を優先";
    }

    private int GetEstimatedAudioBitrateKbps(string filePath)
    {
        if (AudioQuality == "音声無し")
            return 0;

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

    private bool IsAudioCopy => AudioQuality == "\u30b3\u30d4\u30fc";
    private bool IsAudioDisabled => AudioQuality == "\u97f3\u58f0\u7121\u3057";

    public async Task RunAsync()
    {
        if (IsRunning)
            return;
        if (IsImageMode)
            await RunImageAsync();
        else if (IsAudioMode)
            await RunAudioAsync();
        else
            await RunVideoAsync();
    }

    private async Task RunImageAsync()
    {
        if (IsRunning)
            return;
        IsRunning = true;
        _cts?.Dispose();
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
                var imageTargetBytes = TargetSizePlanner.GetTargetBytes(
                    ImageTargetSizeKb,
                    TargetSizePlanner.DecimalMegabytes
                ) / 1000;
                if (ImageTargetEnabled && new FileInfo(file.FilePath).Length <= imageTargetBytes)
                {
                    file.Progress = 100;
                    file.Status = ProcessStatus.Done;
                    file.Details = "スキップ（入力がすでに目標以下）";
                    done++;
                    TotalProgress = (double)done / Files.Count * 100;
                    continue;
                }

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

                var success = false;
                var encodeCompleted = false;
                var error = "";
                var quality = _imageQuality;
                var attempts = ImageTargetEnabled ? MaximumAttempts : 1;
                var targetBytes = imageTargetBytes;
                var actualBytes = 0L;
                for (var attempt = 1; attempt <= attempts; attempt++)
                {
                    StatusText = $"処理中: {file.FileName}（{attempt}/{attempts}）";
                    var argsList = BuildImageArgs(file.FilePath, ext, outputPath, quality);
                    (success, error) = await App.Ffmpeg.RunAsync(argsList, 0, null, token);
                    if (!success)
                        break;
                    encodeCompleted = true;

                    actualBytes = new FileInfo(outputPath).Length;
                    file.Details = ImageTargetEnabled
                        ? TargetSizePlanner.FormatResult(actualBytes, targetBytes, attempt)
                        : MediaFormats.FormatSize(actualBytes);
                    if (!ImageTargetEnabled || actualBytes <= targetBytes)
                        break;

                    if (attempt < attempts)
                    {
                        quality = ext == ".webp"
                            ? Math.Max(1, quality - Math.Max(1, quality / 3))
                            : Math.Min(ext == ".png" ? 9 : 31, quality + Math.Max(1, (31 - quality) / 3));
                    }
                    else
                    {
                        success = false;
                        error = $"目標サイズを超過しました（{actualBytes / 1000d:F1} KB）。";
                    }
                }

                file.Progress = 100;
                if (!success && encodeCompleted && actualBytes > targetBytes)
                {
                    success = true;
                    file.Details += " / 目標サイズ超過（変換済み）";
                }
                file.Status = success ? ProcessStatus.Done : ProcessStatus.Error;
                if (success)
                    LastOutputDir = System.IO.Path.GetDirectoryName(outputPath);
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
        if (IsRunning)
            return;
        IsRunning = true;
        _cts?.Dispose();
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
                    var mediaInfo = await App.Ffprobe.GetMediaInfoAsync(file.FilePath, token);
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

                var targetBytes = TargetSizePlanner.GetTargetBytes(_targetSizeMb, SelectedSizeUnit);
                if (SkipFilesAlreadyWithinTarget && new FileInfo(file.FilePath).Length <= targetBytes)
                {
                    file.Status = ProcessStatus.Done;
                    file.Progress = 100;
                    file.Details = "スキップ（入力が目標以下）";
                    done++;
                    TotalProgress = (double)done / Files.Count * 100;
                    continue;
                }
                var targetBitrateKbps = TargetSizePlanner.CalculateAudioBitrateKbps(
                    targetBytes,
                    duration
                );
                var requestedBitrateKbps = GetEstimatedAudioBitrateKbps(file.FilePath);
                var audioBitrateKbps = IsAudioCopy
                    ? 0
                    : Math.Min(targetBitrateKbps, requestedBitrateKbps);
                if (!IsAudioCopy && audioBitrateKbps <= 0)
                {
                    file.Status = ProcessStatus.Error;
                    file.ErrorMessage = "目標サイズが小さすぎます";
                    continue;
                }

                var inputExt = Path.GetExtension(file.FilePath).ToLowerInvariant();
                if (IsAudioDisabled)
                {
                    file.Status = ProcessStatus.Error;
                    file.ErrorMessage = "音声無しは音声ファイルの圧縮には使用できません。";
                    continue;
                }
                var ext = IsAudioCopy ? inputExt : MediaEncodingProfile.GetCompressedAudioExtension(inputExt);
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

                var progress = new Progress<FfmpegProgress>(p =>
                {
                    file.Progress = p.Percent * 100;
                    TotalProgress = (done + p.Percent) / Files.Count * 100;
                });

                var success = false;
                var error = "";
                var actualBytes = 0L;
                var encodeCompleted = false;
                var attempts = 0;
                for (var attempt = 1; attempt <= MaximumAttempts; attempt++)
                {
                    attempts = attempt;
                    StatusText = $"処理中: {file.FileName}（{attempt}/{MaximumAttempts}）";
                    var argsList = new List<string> { "-i", file.FilePath, "-vn" };
                    argsList.AddRange(IsAudioCopy
                        ? ["-c:a", "copy"]
                        : MediaEncodingProfile.GetCompressedAudioArguments(ext, audioBitrateKbps));
                    argsList.Add(outputPath);

                    (success, error) = await App.Ffmpeg.RunAsync(
                        argsList,
                        duration,
                        progress,
                        token
                    );
                    if (!success)
                        break;
                    encodeCompleted = true;

                    actualBytes = new FileInfo(outputPath).Length;
                    file.Details = TargetSizePlanner.FormatResult(actualBytes, targetBytes, attempts);
                    if (actualBytes <= targetBytes)
                        break;

                    if (attempt < MaximumAttempts)
                    {
                        audioBitrateKbps = TargetSizePlanner.AdjustBitrateKbps(
                            audioBitrateKbps,
                            targetBytes,
                            actualBytes
                        );
                        if (audioBitrateKbps <= 0)
                        {
                            success = false;
                            error = "目標サイズに収まる音声ビットレートを計算できませんでした。";
                            break;
                        }
                    }
                    else
                    {
                        success = false;
                        error = $"目標サイズを超過しました（{actualBytes / 1_000_000d:F2} MB）。";
                    }
                }

                if (!success && encodeCompleted && actualBytes > targetBytes)
                {
                    success = true;
                    file.Details += " / 目標サイズ超過（変換済み）";
                }
                file.Status = success ? ProcessStatus.Done : ProcessStatus.Error;
                if (success)
                    LastOutputDir = Path.GetDirectoryName(outputPath);
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
        => BuildImageArgs(inputPath, ext, outputPath, _imageQuality);

    private List<string> BuildImageArgs(string inputPath, string ext, string outputPath, int quality)
    {
        return ext switch
        {
            ".png" => ["-i", inputPath, "-compression_level", Math.Clamp(quality, 0, 9).ToString(), outputPath],
            ".webp" => ["-i", inputPath, "-q:v", Math.Clamp(quality, 1, 100).ToString(), outputPath],
            ".avif" => ["-i", inputPath, "-crf", Math.Clamp(quality, 0, 63).ToString(), "-c:v", "libaom-av1", outputPath],
            _ =>
                ["-i", inputPath, "-q:v", Math.Clamp(quality, 2, 31).ToString(), outputPath],
        };
    }

    private static int GetFrameRateCap(string value) =>
        value switch
        {
            "60 fps" => 60,
            "30 fps" => 30,
            "24 fps" => 24,
            "15 fps" => 15,
            _ => 0,
        };

    private static string? AddArgument(List<string> args, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            args.AddRange(["-vf", value]);
        return value;
    }

    private async Task RunVideoAsync()
    {
        Log.Info("Compress RunVideoAsync started");
        if (IsRunning)
            return;
        IsRunning = true;
        _cts?.Dispose();
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
                _videoInfoCache.TryGetValue(file.FilePath, out var videoInfo);
                if (duration <= 0)
                {
                    Log.Info($"Getting media info for: {file.FilePath}");
                    var info2 = await App.Ffprobe.GetMediaInfoAsync(file.FilePath, token);
                    if (info2 != null)
                    {
                        videoInfo = info2;
                        _videoInfoCache[file.FilePath] = info2;
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
                var targetBytes = TargetSizePlanner.GetTargetBytes(_targetSizeMb, SelectedSizeUnit);
                if (SkipFilesAlreadyWithinTarget && new FileInfo(file.FilePath).Length <= targetBytes)
                {
                    file.Status = ProcessStatus.Done;
                    file.Progress = 100;
                    file.Details = "スキップ（入力が目標以下）";
                    done++;
                    TotalProgress = (double)done / Files.Count * 100;
                    continue;
                }
                var videoBitrateKbps = TargetSizePlanner.CalculateInitialVideoBitrateKbps(
                    targetBytes,
                    duration,
                    audioBitrateKbps
                );

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

                var videoCodec = MediaEncodingProfile.GetResizeVideoCodec(ext, _hwEncoder);
                videoCodec = MediaEncodingProfile.GetCompressionVideoCodec(ext, VideoCodec, _hwEncoder);

                StatusText = $"処理中: {file.FileName}";

                var audioArgs =
                    AudioQuality == "音声無し"
                        ? new[] { "-an" }
                        : AudioQuality == "コピー"
                            ? new[] { "-c:a", "copy" }
                            : MediaEncodingProfile.GetCompressedAudioArguments(
                                ext == ".webm" ? ".opus"
                                    : ext == ".avi" ? ".mp3"
                                    : ext == ".wmv" ? ".wma"
                                    : ".m4a",
                                audioBitrateKbps
                            ).ToArray();

                Log.Info($"Running ffmpeg: codec={videoCodec}, output={outputPath}");

                var progressFinal = new Progress<FfmpegProgress>(p =>
                {
                    file.Progress = p.Percent * 100;
                    TotalProgress = (done + p.Percent) / Files.Count * 100;
                });

                var success = false;
                var encodeCompleted = false;
                var error = "";
                var actualBytes = 0L;
                var attempts = 0;
                for (var attempt = 1; attempt <= MaximumAttempts; attempt++)
                {
                    attempts = attempt;
                    StatusText = $"処理中: {file.FileName}（{attempt}/{MaximumAttempts}）";
                var encodeArgsList = new List<string>
                {
                    "-i", file.FilePath,
                    "-map", "0:v:0",
                    "-map", "0:a?",
                    "-c:v", videoCodec,
                    "-b:v", videoBitrateKbps + "k",
                };
                if (SubtitleMode == "維持")
                {
                    encodeArgsList.AddRange(["-map", "0:s?", "-c:s", "copy"]);
                }
                else
                {
                    encodeArgsList.Add("-sn");
                }
                if (MetadataMode == "削除")
                    encodeArgsList.AddRange(["-map_metadata", "-1"]);
                else
                    encodeArgsList.AddRange(["-map_metadata", "0"]);

                var maxFrameRate = GetFrameRateCap(FrameRateCap);
                var compressionFilter = MediaEncodingProfile.BuildCompressionFilter(
                    videoInfo?.Width ?? 0,
                    videoInfo?.Height ?? 0,
                    ResolutionMode,
                    maxFrameRate,
                    videoBitrateKbps
                );
                AddArgument(encodeArgsList, compressionFilter);
                    encodeArgsList.AddRange(audioArgs);
                    encodeArgsList.Add(outputPath);

                    (success, error) = await App.Ffmpeg.RunWithHardwareFallbackAsync(
                        encodeArgsList,
                        duration,
                        progressFinal,
                        token
                    );
                    if (!success)
                        break;
                    encodeCompleted = true;

                    actualBytes = new FileInfo(outputPath).Length;
                    file.Details = TargetSizePlanner.FormatResult(actualBytes, targetBytes, attempts);
                    if (actualBytes <= targetBytes)
                        break;

                    if (attempt < MaximumAttempts)
                    {
                        videoBitrateKbps = TargetSizePlanner.AdjustVideoBitrateKbps(
                            videoBitrateKbps,
                            audioBitrateKbps,
                            targetBytes,
                            actualBytes
                        );
                        if (videoBitrateKbps <= 0)
                        {
                            success = false;
                            error = "目標サイズに収まる映像ビットレートを計算できませんでした。";
                            break;
                        }
                    }
                    else
                    {
                        success = false;
                        error = $"目標サイズを超過しました（{actualBytes / 1_000_000d:F2} MB）。";
                    }
                }

                if (!success && encodeCompleted && actualBytes > targetBytes)
                {
                    success = true;
                    file.Details += " / 目標サイズ超過（変換済み）";
                }
                file.Status = success ? ProcessStatus.Done : ProcessStatus.Error;
                if (success)
                    LastOutputDir = System.IO.Path.GetDirectoryName(outputPath);
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
