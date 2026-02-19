namespace RShiftTools.Models;
using System.IO;
public class MediaFile
{
    public string FilePath { get; init; } = "";
    public string FileName => Path.GetFileName(FilePath);
    public string FileSizeText => FormatSize(new FileInfo(FilePath).Length);
    public ProcessStatus Status { get; set; } = ProcessStatus.Waiting;
    public double Progress { get; set; } = 0;
    public string? ErrorMessage { get; set; }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024.0:F1} MB",
        _ => $"{bytes / 1024.0 / 1024.0 / 1024.0:F1} GB",
    };
}

public enum ProcessStatus { Waiting, Processing, Done, Error }