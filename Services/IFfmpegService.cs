namespace RShiftTools.Services;

public interface IFfmpegService
{
    Task<(bool Success, string ErrorLog)> RunAsync(
        string arguments,
        double totalDurationSeconds,
        IProgress<RShiftTools.Models.FfmpegProgress>? onProgress = null,
        CancellationToken cancellationToken = default);
    Task<(bool Success, string ErrorLog)> RunAsync(
        IEnumerable<string> arguments,
        double totalDurationSeconds,
        IProgress<RShiftTools.Models.FfmpegProgress>? onProgress = null,
        CancellationToken cancellationToken = default);
    static string ResolveOutputPath(string inputPath, string newExtension, bool overwrite) =>
        FfmpegService.ResolveOutputPath(inputPath, newExtension, overwrite);
}
