using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using RShiftTools.Services;
using RShiftTools.ViewModels;

namespace RShiftTools.Views;

public partial class CutDialog : Window
{
    private readonly CutViewModel _vm;
    private readonly DispatcherTimer _timer;
    private bool _isDraggingSlider = false;

    public CutDialog(List<string> files)
    {
        InitializeComponent();
        _vm = new CutViewModel(files[0], new DialogService());
        DataContext = _vm;

        _vm.PlayRequested += () => MediaPlayer.Play();
        _vm.PauseRequested += () => MediaPlayer.Pause();
        _vm.SeekRequested += seconds =>
        {
            MediaPlayer.Position = TimeSpan.FromSeconds(seconds);
            UpdateMarkers();
        };
        _vm.SpeedChanged += speed => MediaPlayer.SpeedRatio = speed;
        _vm.VolumeChanged += volume => MediaPlayer.Volume = volume;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        _timer.Tick += (_, _) =>
        {
            if (!_isDraggingSlider && _vm.IsPlaying)
            {
                _vm.CurrentSeconds = MediaPlayer.Position.TotalSeconds;
                UpdateMarkers();
            }
        };
        _timer.Start();

        MediaPlayer.Volume = _vm.Volume / 100.0;

        if (_vm.IsPreviewAvailable)
        {
            MediaPlayer.Source = new Uri(files[0], UriKind.Absolute);
            MediaPlayer.Pause();
        }
        else
        {
            MediaPlayer.Visibility = Visibility.Collapsed;
            PreviewUnavailableMsg.Visibility = Visibility.Visible;
        }
    }

    protected override async void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        await _vm.InitAsync();
    }

    private void MediaPlayer_MediaOpened(object sender, RoutedEventArgs e)
        => UpdateMarkers();

    private void MediaPlayer_MediaEnded(object sender, RoutedEventArgs e)
    {
        _vm.IsPlaying = false;
        MediaPlayer.Pause();
        MediaPlayer.Position = TimeSpan.Zero;
        _vm.CurrentSeconds = 0;
        UpdateMarkers();
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e) => _vm.TogglePlay();
    private void ToStart_Click(object sender, RoutedEventArgs e) => _vm.Seek(0);
    private void ToEnd_Click(object sender, RoutedEventArgs e) => _vm.Seek(_vm.TotalSeconds);

    private void SetIn_Click(object sender, RoutedEventArgs e)
    {
        _vm.SetInPoint();
        UpdateMarkers();
    }

    private void SetOut_Click(object sender, RoutedEventArgs e)
    {
        _vm.SetOutPoint();
        UpdateMarkers();
    }

    private void SeekSlider_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _isDraggingSlider = true;
        SeekSlider.CaptureMouse();
        SeekToPoint(sender as Slider, e.GetPosition((Slider)sender));
    }

    private void SeekSlider_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingSlider) return;
        SeekToPoint(sender as Slider, e.GetPosition((Slider)sender));
    }

    private void SeekSlider_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDraggingSlider) return;
        _isDraggingSlider = false;
        SeekSlider.ReleaseMouseCapture();
        SeekToPoint(sender as Slider, e.GetPosition((Slider)sender));
    }

    private void SeekSlider_MouseLeave(object sender, MouseEventArgs e)
    {

    }

    private void SeekToPoint(Slider? slider, Point point)
    {
        if (slider == null || slider.ActualWidth <= 0) return;
        var thumbWidth = 11.0;
        var trackWidth = slider.ActualWidth - thumbWidth;
        var ratio = Math.Max(0, Math.Min(1, (point.X - thumbWidth / 2) / trackWidth));
        var newValue = slider.Minimum + ratio * (slider.Maximum - slider.Minimum);
        _vm.CurrentSeconds = newValue;
        MediaPlayer.Position = TimeSpan.FromSeconds(newValue);
        UpdateMarkers();
    }

    private void TimeTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        var textBox = (TextBox)sender;
        textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        UpdateMarkers();
        Keyboard.ClearFocus();
    }

    private void CurrentTimeTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        var textBox = (TextBox)sender;
        textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        Keyboard.ClearFocus();
    }

    private void UpdateMarkers()
    {
        if (_vm.TotalSeconds <= 0) return;
        var w = SeekSlider.ActualWidth;
        if (w <= 0) return;
        Canvas.SetLeft(InMarker, _vm.InPoint / _vm.TotalSeconds * w);
        Canvas.SetLeft(OutMarker, _vm.OutPoint / _vm.TotalSeconds * w);
    }

    private async void RunButton_Click(object sender, RoutedEventArgs e)
        => await _vm.RunAsync();

    private void CancelButton_Click(object sender, RoutedEventArgs e)
        => _vm.Cancel();

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        MediaPlayer.Stop();
        MediaPlayer.Source = null;
        base.OnClosed(e);
    }
}