using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RShiftTools.Services;

namespace RShiftTools.ViewModels;

public class MainViewModel : BaseViewModel
{
    public string AppTitle => AppStrings.AppName;

    private string _currentPath = "";
    public string CurrentPath
    {
        get => _currentPath;
        set
        {
            _currentPath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanGoUp));
            OnPropertyChanged(nameof(IsDrivesMode));
            RefreshFiles();
        }
    }

    public bool CanGoUp => !string.IsNullOrEmpty(_currentPath) && Directory.GetParent(_currentPath) != null;
    public bool IsDrivesMode => string.IsNullOrEmpty(_currentPath);

    private ImageSource? _upIcon;
    public ImageSource? UpIcon => _upIcon ??= GetUpIcon();

    private static ImageSource? GetUpIcon()
    {
        var explorer = Environment.GetFolderPath(Environment.SpecialFolder.Windows) + "\\explorer.exe";
        var icon = IconHelper.GetShellStockIcon(explorer, -101);
        if (icon != null)
            return icon;
        return IconHelper.GetSystemIcon(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), true);
    }

    public ObservableCollection<FileItem> Files { get; } = [];

    private readonly List<string> _selectedPaths = [];
    public int SelectedCount => _selectedPaths.Count;
    public bool HasSelection => _selectedPaths.Count > 0;

    public MainViewModel()
    {
        GoHome();
    }

    public void GoHome()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (Directory.Exists(home))
            CurrentPath = home;
        else
            CurrentPath = "C:\\";
    }

    public void GoUp()
    {
        try
        {
            var parent = Directory.GetParent(_currentPath);
            if (parent != null)
                CurrentPath = parent.FullName;
        }
        catch { }
    }

    public void Browse()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "フォルダを選択",
            InitialDirectory = _currentPath,
        };
        if (dlg.ShowDialog() == true)
            CurrentPath = dlg.FolderName;
    }

    public void SetSelectedPaths(System.Collections.IList selectedItems)
    {
        _selectedPaths.Clear();
        foreach (FileItem item in selectedItems)
        {
            if (!item.IsDirectory)
                _selectedPaths.Add(item.FullPath);
        }
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(HasSelection));
    }

    public List<string> GetSelectedPaths() => [.. _selectedPaths];

    public void RefreshFiles()
    {
        Files.Clear();

        if (IsDrivesMode)
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady)
                    continue;
                Files.Add(new FileItem
                {
                    Name = $"{drive.Name} ({drive.VolumeLabel})",
                    FullPath = drive.RootDirectory.FullName,
                    IsDirectory = true,
                    SizeText = FormatSize(drive.TotalSize - drive.AvailableFreeSpace) + " / " + FormatSize(drive.TotalSize),
                    Modified = DateTime.MinValue,
                });
            }
            return;
        }

        if (!Directory.Exists(_currentPath))
            return;

        try
        {
            foreach (var dir in Directory.GetDirectories(_currentPath))
            {
                var info = new DirectoryInfo(dir);
                Files.Add(new FileItem
                {
                    Name = info.Name,
                    FullPath = info.FullName,
                    IsDirectory = true,
                    SizeText = "",
                    Modified = info.LastWriteTime,
                });
            }

            foreach (var file in Directory.GetFiles(_currentPath))
            {
                var info = new FileInfo(file);
                Files.Add(new FileItem
                {
                    Name = info.Name,
                    FullPath = info.FullName,
                    IsDirectory = false,
                    SizeText = FormatSize(info.Length),
                    Modified = info.LastWriteTime,
                });
            }
        }
        catch { }
    }

    private static string FormatSize(long bytes) => MediaFormats.FormatSize(bytes);
}

public class FileItem
{
    public string Name { get; init; } = "";
    public string FullPath { get; init; } = "";
    public bool IsDirectory { get; init; }
    public string SizeText { get; init; } = "";
    public DateTime Modified { get; init; }
    public string TypeLabel => IsDirectory ? "フォルダ" : "ファイル";

    private ImageSource? _icon;
    public ImageSource? IconSource
    {
        get
        {
            if (_icon == null)
                _icon = IconHelper.GetSystemIcon(FullPath, IsDirectory);
            return _icon;
        }
    }
}

internal static class IconHelper
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        out SHFILEINFO psfi,
        uint cbFileInfo,
        uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    private const uint SHGFI_ICON = 0x100;
    private const uint SHGFI_SMALLICON = 0x1;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x10;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;

    public static ImageSource? GetSystemIcon(string path, bool isDirectory)
    {
        try
        {
            var flags = SHGFI_ICON | SHGFI_SMALLICON;
            var attrs = isDirectory ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;

            if (!File.Exists(path) && !Directory.Exists(path))
                flags |= SHGFI_USEFILEATTRIBUTES;

            var info = new SHFILEINFO();
            var result = SHGetFileInfo(path, attrs, out info, (uint)Marshal.SizeOf(info), flags);

            if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero)
                return null;

            var imageSource = HIconToImageSource(info.hIcon);
            DestroyIcon(info.hIcon);
            return imageSource;
        }
        catch
        {
            return null;
        }
    }

    private static ImageSource HIconToImageSource(IntPtr hIcon)
    {
        return Imaging.CreateBitmapSourceFromHIcon(
            hIcon,
            System.Windows.Int32Rect.Empty,
            BitmapSizeOptions.FromEmptyOptions());
    }

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern uint ExtractIconEx(string lpszFile, int nIconIndex, [Out] IntPtr[]? phiconLarge, [Out] IntPtr[]? phiconSmall, uint nIcons);

    public static ImageSource? GetShellStockIcon(string dllPath, int iconIndex)
    {
        try
        {
            var large = new IntPtr[1];
            var small = new IntPtr[1];
            var count = ExtractIconEx(dllPath, iconIndex, large, small, 1);
            if (count > 0 && small[0] != IntPtr.Zero)
            {
                var img = HIconToImageSource(small[0]);
                foreach (var h in large) if (h != IntPtr.Zero) DestroyIcon(h);
                foreach (var h in small) if (h != IntPtr.Zero) DestroyIcon(h);
                return img;
            }
            if (count > 0 && large[0] != IntPtr.Zero)
            {
                var img = HIconToImageSource(large[0]);
                foreach (var h in large) if (h != IntPtr.Zero) DestroyIcon(h);
                foreach (var h in small) if (h != IntPtr.Zero) DestroyIcon(h);
                return img;
            }
        }
        catch { }
        return null;
    }
}
