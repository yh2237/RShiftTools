namespace RShiftTools.Services;

public interface IFfmpegService
{
    Task<(bool Success, string ErrorLog)> RunAsync(
        string arguments,
        double totalDurationSeconds,
        IProgress<RShiftTools.Models.FfmpegProgress>? onProgress = null,
        CancellationToken cancellationToken = default
    );
    Task<(bool Success, string ErrorLog)> RunAsync(
        IEnumerable<string> arguments,
        double totalDurationSeconds,
        IProgress<RShiftTools.Models.FfmpegProgress>? onProgress = null,
        CancellationToken cancellationToken = default
    );
    Task<(bool Success, string ErrorLog)> RunWithHardwareFallbackAsync(
        IEnumerable<string> arguments,
        double totalDurationSeconds,
        IProgress<RShiftTools.Models.FfmpegProgress>? onProgress = null,
        CancellationToken cancellationToken = default
    );
}
