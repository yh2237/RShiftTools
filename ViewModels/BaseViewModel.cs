using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace RShiftTools.ViewModels;

public abstract class BaseViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    protected bool SetProperty<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null
    )
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected static string BuildUniqueOutputPath(string outputDir, string inputPath, string ext)
    {
        var name = Path.GetFileNameWithoutExtension(inputPath);
        var normalizedExt = ext.StartsWith('.') ? ext : $".{ext}";
        var candidate = Path.Combine(outputDir, name + "_out" + normalizedExt);
        if (!File.Exists(candidate))
            return candidate;

        for (var i = 2; i < 1000; i++)
        {
            candidate = Path.Combine(outputDir, $"{name}_out_{i}{normalizedExt}");
            if (!File.Exists(candidate))
                return candidate;
        }
        return Path.Combine(outputDir, $"{name}_out_{Guid.NewGuid():N}{normalizedExt}");
    }
}
