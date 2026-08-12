using System.Collections.ObjectModel;
using System.IO;
using RShiftTools.Models;
using RShiftTools.Services;

namespace RShiftTools.ViewModels;

public class AudioEditViewModel : BaseViewModel
{
    public ObservableCollection<MediaFile> Files { get; } = [];
    public ObservableCollection<string> BitDepths { get; } =
        [AudioEditProfile.Keep, AudioEditProfile.Bit16, AudioEditProfile.Bit24, AudioEditProfile.Bit32Float];
    public ObservableCollection<string> SampleRates { get; } =
        [AudioEditProfile.Keep, "44.1 kHz", "48 kHz", "88.2 kHz", "96 kHz", "176.4 kHz", "192 kHz"];
    public ObservableCollection<string> ChannelModes { get; } =
        [AudioEditProfile.Keep, "Mono", "Stereo"];
    public ObservableCollection<string> DitherModes { get; } =
        [AudioEditProfile.DitherAuto, AudioEditProfile.DitherNone, AudioEditProfile.DitherTriangular, AudioEditProfile.DitherTriangularHighPass];
    public ObservableCollection<string> OutputFormats { get; } = ["WAV", "FLAC"];

    private readonly IDialogService _dialogService;
    private readonly Dictionary<string, MediaInfo> _mediaInfo = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _cts;

    public bool HasAudioFilesOnly { get; }

    private string _selectedBitDepth = AudioEditProfile.Bit16;
    public string SelectedBitDepth
    {
        get => _selectedBitDepth;
        set
        {
            _selectedBitDepth = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanRun));
            OnPropertyChanged(nameof(ValidationText));
        }
    }

    private string _selectedSampleRate = AudioEditProfile.Keep;
    public string SelectedSampleRate
    {
        get => _selectedSampleRate;
        set { _selectedSampleRate = value; OnPropertyChanged(); }
    }

    private string _selectedChannels = AudioEditProfile.Keep;
    public string SelectedChannels
    {
        get => _selectedChannels;
        set { _selectedChannels = value; OnPropertyChanged(); }
    }

    private string _selectedDither = AudioEditProfile.DitherAuto;
    public string SelectedDither
    {
        get => _selectedDither;
        set { _selectedDither = value; OnPropertyChanged(); }
    }

    private string _selectedOutputFormat = "WAV";
    public string SelectedOutputFormat
    {
        get => _selectedOutputFormat;
        set
        {
            _selectedOutputFormat = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanRun));
            OnPropertyChanged(nameof(ValidationText));
        }
    }

    public string ValidationText =>
        !HasAudioFilesOnly ? AppStrings.Error_AudioFilesOnly
        : IsFlacSelectionInvalid
            ? "FLACでは32-bit以上またはfloatを維持できません。24-bit以下を選択してください。"
            : "";

    private bool IsFlacSelectionInvalid =>
        SelectedOutputFormat == "FLAC"
        && (
            SelectedBitDepth == AudioEditProfile.Bit32Float
            || SelectedBitDepth == AudioEditProfile.Keep
            && _mediaInfo.Values.Any(info =>
                info.AudioBitDepth > 24
                || info.AudioSampleFormat.StartsWith("flt", StringComparison.OrdinalIgnoreCase)
            )
        );

    private double _totalProgress;
    public double TotalProgress
    {
        get => _totalProgress;
        set { _totalProgress = value; OnPropertyChanged(); }
    }

    private string _statusText = AppStrings.Status_Waiting;
    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnPropertyChanged(); }
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

    public bool CanRun =>
        !IsRunning
        && HasAudioFilesOnly
        && Files.Count > 0
        && !IsFlacSelectionInvalid;

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
    public bool HasOutput => !string.IsNullOrEmpty(LastOutputDir);

    public AudioEditViewModel(List<string> filePaths, IDialogService dialogService)
    {
        _dialogService = dialogService;
        foreach (var path in filePaths)
            Files.Add(new MediaFile { FilePath = path });
        HasAudioFilesOnly = filePaths.Count > 0
            && filePaths.All(path => MediaFormats.GetKind(path) == MediaFormats.MediaKind.Audio);
        if (!HasAudioFilesOnly)
            StatusText = AppStrings.Error_AudioFilesOnly;
    }

    public async Task InitAsync()
    {
        if (!HasAudioFilesOnly)
            return;

        foreach (var file in Files)
        {
            try
            {
                var info = await App.Ffprobe.GetMediaInfoAsync(file.FilePath);
                if (info == null || info.Type != MediaType.Audio)
                {
                    file.Details = "音声情報を取得できませんでした";
                    continue;
                }
                _mediaInfo[file.FilePath] = info;
                file.Details = AudioEditProfile.FormatSourceDetails(info);
            }
            catch (Exception ex)
            {
                file.Details = "音声情報を取得できませんでした";
                file.ErrorMessage = ex.Message;
            }
        }
        OnPropertyChanged(nameof(CanRun));
        OnPropertyChanged(nameof(ValidationText));
    }

    public async Task RunAsync()
    {
        if (!CanRun)
            return;

        IsRunning = true;
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        var done = 0;

        try
        {
            string? outputDirectory = null;
            if (Files.Count > 1)
            {
                var suggestedDirectory = Path.GetDirectoryName(Files[0].FilePath) ?? "";
                outputDirectory = await System.Windows.Application.Current.Dispatcher.InvokeAsync(
                    () => _dialogService.AskOutputFolder(suggestedDirectory)
                );
                if (outputDirectory == null)
                {
                    foreach (var file in Files)
                    {
                        file.Status = ProcessStatus.Cancelled;
                        file.ErrorMessage = AppStrings.Error_Cancelled;
                    }
                    StatusText = AppStrings.Status_Waiting;
                    return;
                }
            }

            foreach (var file in Files)
            {
                if (token.IsCancellationRequested)
                    break;

                file.Status = ProcessStatus.Processing;
                file.Progress = 0;
                file.ErrorMessage = null;
                StatusText = $"処理中: {file.FileName}";

                try
                {
                    if (!_mediaInfo.TryGetValue(file.FilePath, out var info))
                    {
                        info = await App.Ffprobe.GetMediaInfoAsync(file.FilePath, token);
                        if (info == null)
                            throw new InvalidOperationException("音声情報を取得できませんでした。");
                        _mediaInfo[file.FilePath] = info;
                        file.Details = AudioEditProfile.FormatSourceDetails(info);
                    }

                    var extension = AudioEditProfile.GetOutputExtension(SelectedOutputFormat);
                    string? outputPath = outputDirectory != null
                        ? BuildUniqueOutputPath(outputDirectory, file.FilePath, extension)
                        : await System.Windows.Application.Current.Dispatcher.InvokeAsync(
                            () => _dialogService.AskOutputPath(file.FilePath, extension)
                        );
                    if (outputPath == null)
                    {
                        file.Status = ProcessStatus.Cancelled;
                        file.ErrorMessage = AppStrings.Error_Cancelled;
                        done++;
                        continue;
                    }

                    var arguments = AudioEditProfile.BuildArguments(
                        file.FilePath,
                        outputPath,
                        info,
                        SelectedOutputFormat,
                        SelectedBitDepth,
                        SelectedSampleRate,
                        SelectedChannels,
                        SelectedDither
                    );
                    var progress = new Progress<FfmpegProgress>(value =>
                    {
                        file.Progress = value.Percent * 100;
                        TotalProgress = (done + value.Percent) / Files.Count * 100;
                    });

                    var (success, error) = await App.Ffmpeg.RunAsync(
                        arguments,
                        info.DurationSeconds,
                        progress,
                        token
                    );
                    file.Progress = success ? 100 : file.Progress;
                    file.Status = success ? ProcessStatus.Done : ProcessStatus.Error;
                    if (success)
                        LastOutputDir = Path.GetDirectoryName(outputPath);
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
                    Log.Error($"Audio edit failed: {file.FilePath}: {ex}");
                }

                done++;
                TotalProgress = (double)done / Files.Count * 100;
            }

            StatusText = string.Format(
                AppStrings.Status_CompleteFormat,
                Files.Count(file => file.Status == ProcessStatus.Done),
                Files.Count(file => file.Status == ProcessStatus.Error)
            );
        }
        finally
        {
            IsRunning = false;
        }
    }

    public void Cancel() => _cts?.Cancel();
}
