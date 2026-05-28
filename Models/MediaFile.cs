using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using RShiftTools.Services;

namespace RShiftTools.Models;

public class MediaFile : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public required string FilePath { get; init; }
    public string FileName => Path.GetFileName(FilePath);

    private long? _fileSizeBytes;
    public string FileSizeText
    {
        get
        {
            if (_fileSizeBytes == null)
            {
                try
                {
                    _fileSizeBytes = new FileInfo(FilePath).Length;
                }
                catch
                {
                    _fileSizeBytes = 0;
                }
            }
            return FormatSize(_fileSizeBytes.Value);
        }
    }

    private ProcessStatus _status = ProcessStatus.Waiting;
    public ProcessStatus Status
    {
        get => _status;
        set
        {
            if (_status == value)
                return;
            _status = value;
            OnPropertyChanged();
        }
    }

    private double _progress;
    public double Progress
    {
        get => _progress;
        set
        {
            if (_progress == value)
                return;
            _progress = value;
            OnPropertyChanged();
        }
    }

    private string? _errorMessage;
    public string? ErrorMessage
    {
        get => _errorMessage;
        set
        {
            _errorMessage = value;
            OnPropertyChanged();
        }
    }

    private static string FormatSize(long bytes) => MediaFormats.FormatSize(bytes);
}

public enum ProcessStatus
{
    Waiting,
    Processing,
    Done,
    Error,
    Cancelled,
}
