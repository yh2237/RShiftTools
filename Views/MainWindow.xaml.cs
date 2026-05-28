using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using RShiftTools.Services;
using RShiftTools.ViewModels;

namespace RShiftTools.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel();
        DataContext = _vm;
    }

    private void FileListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _vm.SetSelectedPaths(FileListView.SelectedItems);
    }

    private void FileListView_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (FileListView.SelectedItem is FileItem item && item.IsDirectory)
        {
            _vm.CurrentPath = item.FullPath;
        }
    }

    private void GoUp_Click(object sender, RoutedEventArgs e) => _vm.GoUp();

    private void GoHome_Click(object sender, RoutedEventArgs e) => _vm.GoHome();

    private void BrowseFolder_Click(object sender, RoutedEventArgs e) => _vm.Browse();

    private void OpenMode(string mode, List<string> files)
    {
        if (files.Count == 0)
        {
            MessageBox.Show("ファイルが選択されていません。", AppStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Window dialog = mode switch
        {
            "convert" => new ConvertDialog(files),
            "resize" => new ResizeDialog(files),
            "cut" => new CutDialog(files),
            "compress" => new CompressDialog(files),
            _ => throw new InvalidOperationException($"Unknown mode: {mode}"),
        };

        dialog.Owner = this;
        dialog.Show();
    }

    private void ToolConvert_Click(object sender, RoutedEventArgs e) => OpenMode("convert", _vm.GetSelectedPaths());
    private void ToolResize_Click(object sender, RoutedEventArgs e) => OpenMode("resize", _vm.GetSelectedPaths());
    private void ToolCut_Click(object sender, RoutedEventArgs e) => OpenMode("cut", _vm.GetSelectedPaths());
    private void ToolCompress_Click(object sender, RoutedEventArgs e) => OpenMode("compress", _vm.GetSelectedPaths());

    private void CtxConvert_Click(object sender, RoutedEventArgs e) => OpenMode("convert", _vm.GetSelectedPaths());
    private void CtxResize_Click(object sender, RoutedEventArgs e) => OpenMode("resize", _vm.GetSelectedPaths());
    private void CtxCut_Click(object sender, RoutedEventArgs e) => OpenMode("cut", _vm.GetSelectedPaths());
    private void CtxCompress_Click(object sender, RoutedEventArgs e) => OpenMode("compress", _vm.GetSelectedPaths());

    private void Window_DragEnter(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            return;

        var dropped = (string[])e.Data.GetData(DataFormats.FileDrop);
        var files = dropped.Where(File.Exists).ToList();
        if (files.Count == 0)
            return;

        var dlg = new ModeSelectDialog { Owner = this };
        if (dlg.ShowDialog() != true || dlg.SelectedMode is null)
            return;

        OpenMode(dlg.SelectedMode, files);
    }

    private void MenuOpenFile_Click(object sender, RoutedEventArgs e)
    {
        var files = PickFiles();
        if (files is null)
            return;

        var dlg = new ModeSelectDialog { Owner = this };
        if (dlg.ShowDialog() != true || dlg.SelectedMode is null)
            return;

        OpenMode(dlg.SelectedMode, files);
    }

    private void MenuExit_Click(object sender, RoutedEventArgs e) => Close();

    private void MenuSettings_Click(object sender, RoutedEventArgs e)
    {
        var win = new SettingsWindow { Owner = this };
        win.ShowDialog();
    }

    private void MenuLicense_Click(object sender, RoutedEventArgs e)
    {
        var licensePath = Path.Combine(AppContext.BaseDirectory, "ffmpeg-license.txt");
        if (File.Exists(licensePath))
        {
            try
            {
                Process.Start(new ProcessStartInfo(licensePath) { UseShellExecute = true });
            }
            catch
            {
                var text = File.ReadAllText(licensePath);
                MessageBox.Show(text, "ffmpegライセンス(LGPL)", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        else
        {
            MessageBox.Show(
                "ffmpegライセンスファイルが見つかりません。\n\nffmpegはLGPLライセンスで提供されています。\n詳細: https://ffmpeg.org/legal.html",
                "ライセンス情報",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private void MenuGitHub_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(
            new ProcessStartInfo("https://github.com/yh2237/RShiftTools") { UseShellExecute = true }
        );
    }

    private void MenuAbout_Click(object sender, RoutedEventArgs e)
    {
        var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        var version = ver != null ? $"{ver.Major}.{ver.Minor}.{ver.Build}" : "1.0.0";
        MessageBox.Show(
            $"RShiftTools v{version}\n\nGitHub: https://github.com/yh2237/RShiftTools",
            "バージョン情報",
            MessageBoxButton.OK,
            MessageBoxImage.Information
        );
    }

    private List<string>? PickFiles()
    {
        var dlg = new OpenFileDialog
        {
            Title = "ファイルを選択",
            Filter = "メディアファイル|*.mp4;*.mkv;*.avi;*.mov;*.webm;*.flv;*.wmv;*.m4v;*.mp3;*.aac;*.wav;*.flac;*.ogg;*.m4a;*.opus;*.wma;*.jpg;*.jpeg;*.png;*.gif;*.webp;*.bmp;*.tiff;*.avif|すべてのファイル|*.*",
            Multiselect = true,
        };

        if (dlg.ShowDialog(this) != true)
            return null;

        var files = dlg.FileNames.Where(File.Exists).ToList();
        return files.Count > 0 ? files : null;
    }
}
