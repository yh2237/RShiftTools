using System.Collections.ObjectModel;
using System.IO;
using RShiftTools.Models;
using RShiftTools.Services;

namespace RShiftTools.ViewModels;

public class CompressViewModel : BaseViewModel
{
    public ObservableCollection<MediaFile> Files { get; } = [];
    private double _targetSizeMb = 50;
    public double TargetSizeMb
    {
        get => _targetSizeMb;
        set { _targetSizeMb = value; OnPropertyChanged(); UpdateEstimate(); }
    }
    public ObservableCollection<string> AudioQualities { get; } = ["低 (96kbps)", "中 (128kbps)", "高 (192kbps)", "コピー"];

    public ObservableCollection<string> HwEncoders { get; } = ["自動 (CPU)", "NVIDIA (nvenc)", "AMD (amf)", "Intel (qsv)"];
    private string _hwEncoder = UserSettings.HwEncoder;
    public string HwEncoder
    {
        get => _hwEncoder;
        set { _hwEncoder = value; OnPropertyChanged(); UserSettings.HwEncoder = value; }
    }

    private string _audioQuality = "中 (128kbps)";
    public string AudioQuality
    {
        get => _audioQuality;
        set { _audioQuality = value; OnPropertyChanged(); UpdateEstimate(); }
    }

    private string _estimateText = "";
    public string EstimateText
    {
        get => _estimateText;
        set { _estimateText = value; OnPropertyChanged(); }
    }

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
        set { _isRunning = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanRun)); }
    }
    public bool CanRun => !_isRunning && Files.Count > 0;

    private CancellationTokenSource? _cts;

    private readonly Dictionary<string, double> _durationCache = [];
    private readonly IDialogService _dialogService;

    public CompressViewModel(List<string> filePaths, IDialogService dialogService)
    {
        _dialogService = dialogService;
        foreach (var path in filePaths)
            Files.Add(new MediaFile { FilePath = path });
    }

    public async Task InitAsync()
    {
        var info = await App.Ffprobe.GetMediaInfoAsync(Files[0].FilePath);
        if (info != null)
        {
            _durationCache[Files[0].FilePath] = info.DurationSeconds;
            UpdateEstimate();
        }
    }

    private void UpdateEstimate()
    {
        if (!_durationCache.TryGetValue(Files[0].FilePath, out var duration) || duration <= 0)
        {
            EstimateText = "推定ビットレート：計算中...";
            return;
        }

        var audioBitrate = AudioQuality switch
        {
            "低 (96kbps)" => 96,
            "高 (192kbps)" => 192,
            _ => 128,
        };

        var targetBits = _targetSizeMb * 8 * 1024 * 1024;
        var videoBitrate = (int)(targetBits / duration / 1000) - audioBitrate;

        if (videoBitrate <= 0)
        {
            EstimateText = "目標サイズが小さすぎます";
            return;
        }

        EstimateText = $"推定映像ビットレート：{videoBitrate} kbps  /  音声：{audioBitrate} kbps";
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
            StatusText = $"処理中: {file.FileName} (Pass 1/2)";

            var passLogFile = Path.Combine(
                Path.GetDirectoryName(file.FilePath)!,
                Path.GetFileNameWithoutExtension(file.FilePath) + "_2pass")
                .Replace('\\', '/');

            try
            {
                var duration = _durationCache.GetValueOrDefault(file.FilePath, 0);
                if (duration <= 0)
                {
                    var info2 = await App.Ffprobe.GetMediaInfoAsync(file.FilePath);
                    if (info2 != null)
                    {
                        duration = info2.DurationSeconds;
                        _durationCache[file.FilePath] = duration;
                    }
                }
                if (duration <= 0)
                {
                    file.Status = ProcessStatus.Error;
                    file.ErrorMessage = "動画の長さを取得できませんでした";
                    continue;
                }

                var audioBitrateKbps = AudioQuality switch
                {
                    "低 (96kbps)" => 96,
                    "高 (192kbps)" => 192,
                    _ => 128,
                };
                var targetBits = _targetSizeMb * 8 * 1024 * 1024;
                var videoBitrateKbps = (int)(targetBits / duration / 1000) - audioBitrateKbps;

                if (videoBitrateKbps <= 0)
                {
                    file.Status = ProcessStatus.Error;
                    file.ErrorMessage = "目標サイズが小さすぎます";
                    continue;
                }

                var ext = Path.GetExtension(file.FilePath).ToLowerInvariant();

                var outputPath = await System.Windows.Application.Current.Dispatcher.InvokeAsync(
                    () => _dialogService.AskOutputPath(file.FilePath, ext));

                if (outputPath == null)
                {
                    file.Status = ProcessStatus.Error;
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

                var pass1ArgsList = new List<string>
                {
                    "-y", "-i", file.FilePath,
                    "-c:v", videoCodec, "-b:v", videoBitrateKbps + "k",
                    "-pass", "1", "-passlogfile", passLogFile,
                    "-an", "-f", "null", "NUL"
                };
                var progress1 = new Progress<FfmpegProgress>(p =>
                {
                    file.Progress = p.Percent * 50;
                    TotalProgress = (done + p.Percent * 0.5) / Files.Count * 100;
                });

                var (success1, error1) = await App.Ffmpeg.RunAsync(pass1ArgsList, duration, progress1, token);

                if (!success1)
                {
                    file.Status = ProcessStatus.Error;
                    file.ErrorMessage = $"Pass 1 失敗:\n{error1}";
                    continue;
                }

                StatusText = $"処理中: {file.FileName} (Pass 2/2)";
                var audioArgs = AudioQuality == "コピー"
                    ? new[] { "-c:a", "copy" }
                    : new[] { "-b:a", $"{audioBitrateKbps}k" };

                var pass2ArgsList = new List<string>
                {
                    "-y", "-i", file.FilePath,
                    "-c:v", videoCodec, "-b:v", videoBitrateKbps + "k",
                    "-pass", "2", "-passlogfile", passLogFile
                };
                pass2ArgsList.AddRange(audioArgs);
                pass2ArgsList.Add(outputPath);

                var progress2 = new Progress<FfmpegProgress>(p =>
                {
                    file.Progress = 50 + p.Percent * 50;
                    TotalProgress = (done + 0.5 + p.Percent * 0.5) / Files.Count * 100;
                });

                var (success2, error2) = await App.Ffmpeg.RunAsync(pass2ArgsList, duration, progress2, token);
                file.Status = success2 ? ProcessStatus.Done : ProcessStatus.Error;
                if (!success2)
                    file.ErrorMessage = $"Pass 2 失敗:\n{error2}";
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
            finally
            {
                try { File.Delete(passLogFile + "-0.log"); } catch { }
                try { File.Delete(passLogFile + "-0.log.mbtree"); } catch { }
            }

            done++;
            TotalProgress = (double)done / Files.Count * 100;
        }

        StatusText = string.Format(AppStrings.Status_CompleteFormat,
            Files.Count(f => f.Status == ProcessStatus.Done),
            Files.Count(f => f.Status == ProcessStatus.Error));
        IsRunning = false;
    }

    public void Cancel() => _cts?.Cancel();
}