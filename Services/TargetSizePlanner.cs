namespace RShiftTools.Services;

internal static class TargetSizePlanner
{
    public const string DecimalMegabytes = "MB（10進）";
    public const string BinaryMegabytes = "MiB（2進）";

    public static long GetTargetBytes(double size, string unit)
    {
        if (!double.IsFinite(size) || size <= 0)
            return 0;
        var multiplier = unit == BinaryMegabytes ? 1024d * 1024d : 1000d * 1000d;
        var bytes = size * multiplier;
        return bytes >= long.MaxValue ? long.MaxValue : (long)Math.Floor(bytes);
    }

    public static int CalculateInitialVideoBitrateKbps(
        long targetBytes,
        double durationSeconds,
        int audioBitrateKbps
    )
    {
        if (targetBytes <= 0 || durationSeconds <= 0)
            return 0;
        const double initialContainerReserve = 0.995;
        var totalBitrate = targetBytes * 8d * initialContainerReserve / durationSeconds / 1000d;
        return ClampBitrate(totalBitrate - Math.Max(0, audioBitrateKbps));
    }

    public static int CalculateAudioBitrateKbps(long targetBytes, double durationSeconds)
    {
        if (targetBytes <= 0 || durationSeconds <= 0)
            return 0;
        const double initialContainerReserve = 0.995;
        return ClampBitrate(targetBytes * 8d * initialContainerReserve / durationSeconds / 1000d);
    }

    public static int AdjustBitrateKbps(int currentBitrateKbps, long targetBytes, long actualBytes)
    {
        if (currentBitrateKbps <= 0 || targetBytes <= 0 || actualBytes <= 0)
            return 0;
        const double retrySafety = 0.99;
        var adjusted = currentBitrateKbps * (double)targetBytes / actualBytes * retrySafety;
        return ClampBitrate(adjusted);
    }

    public static int AdjustVideoBitrateKbps(
        int currentVideoBitrateKbps,
        int audioBitrateKbps,
        long targetBytes,
        long actualBytes
    )
    {
        if (currentVideoBitrateKbps <= 0 || targetBytes <= 0 || actualBytes <= 0)
            return 0;
        const double retrySafety = 0.99;
        var adjustedTotal = (currentVideoBitrateKbps + Math.Max(0, audioBitrateKbps))
            * (double)targetBytes / actualBytes * retrySafety;
        return ClampBitrate(adjustedTotal - Math.Max(0, audioBitrateKbps));
    }

    public static string FormatResult(long actualBytes, long targetBytes, int attempts)
    {
        var actualMb = actualBytes / 1_000_000d;
        var ratio = targetBytes > 0 ? actualBytes * 100d / targetBytes : 0;
        return $"{actualMb:F2} MB / 目標の{ratio:F1}% / {attempts}回";
    }

    private static int ClampBitrate(double value) =>
        !double.IsFinite(value) || value <= 0
            ? 0
            : (int)Math.Min(Math.Floor(value), int.MaxValue);
}
