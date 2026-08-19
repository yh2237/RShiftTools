using System.ComponentModel;
using System.Diagnostics;
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
    private bool _isDraggingSlider;
    private bool _mediaReady;
    private bool _playWhenReady;
    private DateTime _lastSeekTime;
    private readonly TimeSpan _seekThrottle = TimeSpan.FromMilliseconds(50);

    public CutDialog(List<string> files)
    {
        InitializeComponent();
        _vm = new CutViewModel(files[0], new DialogService());
        DataContext = _vm;

        if (!_vm.IsPreviewAvailable)
            PreviewUnavailableMsg.Visibility = Visibility.Visible;

        _vm.PlayRequested += () =>
        {
            if (!_mediaReady)
            {
                _playWhenReady = true;
                return;
            }
            MediaPlayer.Position = TimeSpan.FromSeconds(_vm.CurrentSeconds);
            MediaPlayer.Play();
        };
        _vm.PauseRequested += () =>
        {
            _playWhenReady = false;
            MediaPlayer.Pause();
        };
        _vm.SeekRequested += seconds =>
        {
            MediaPlayer.Position = TimeSpan.FromSeconds(seconds);
            UpdateMarkers();
        };
        _vm.VolumeChanged += vol => MediaPlayer.Volume = vol;

        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Normal, OnTimerTick, Dispatcher);
        _timer.Stop();

        _vm.SpeedChanged += speed =>
        {
            MediaPlayer.SpeedRatio = speed;
            _timer.Interval = TimeSpan.FromMilliseconds(100.0 / speed);
        };

        PreviewKeyDown += OnKeyDown;
        MouseLeftButtonUp += OnWindowMouseUp;
        Focusable = true;

        Loaded += (_, _) =>
        {
            _ = InitializePreviewAsync();
        };
    }

    private async Task InitializePreviewAsync()
    {
        try
        {
            await _vm.InitAsync();
            UpdateMarkers();

            if (_vm.IsPreviewAvailable)
            {
                _mediaReady = false;
                MediaPlayer.Source = new Uri(_vm.File.FilePath);
            }
        }
        catch (Exception ex)
        {
            _mediaReady = false;
            _playWhenReady = false;
            PreviewUnavailableMsg.Text = $"プレビュー読み込み失敗\n{ex.Message}";
            PreviewUnavailableMsg.Visibility = Visibility.Visible;
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (_isDraggingSlider) return;
        var step = 1.0 / 30.0;
        if (Keyboard.IsKeyDown(Key.LeftShift)) step = 1.0;

        switch (e.Key)
        {
            case Key.Left:
                SeekTo(Math.Max(0, _vm.CurrentSeconds - step));
                e.Handled = true;
                break;
            case Key.Right:
                SeekTo(Math.Min(_vm.TotalSeconds, _vm.CurrentSeconds + step));
                e.Handled = true;
                break;
            case Key.Space:
                _vm.TogglePlay();
                if (_vm.IsPlaying) _timer.Start(); else _timer.Stop();
                e.Handled = true;
                break;
            case Key.I:
                _vm.SetInPoint();
                UpdateMarkers();
                e.Handled = true;
                break;
            case Key.O:
                _vm.SetOutPoint();
                UpdateMarkers();
                e.Handled = true;
                break;
        }
    }

    private void SeekTo(double seconds)
    {
        _vm.Seek(seconds);
        MediaPlayer.Position = TimeSpan.FromSeconds(seconds);
        UpdateMarkers();
    }

    private void OnWindowMouseUp(object sender, MouseButtonEventArgs e)
    {
        _isDraggingSlider = false;
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (!_isDraggingSlider && MediaPlayer.NaturalDuration.HasTimeSpan)
            _vm.CurrentSeconds = MediaPlayer.Position.TotalSeconds;
    }

    private void UpdateMarkers()
    {
        var trackWidth = SeekSlider.ActualWidth;
        if (trackWidth <= 0 || _vm.TotalSeconds <= 0) return;

        var inPos = _vm.InPoint / _vm.TotalSeconds * trackWidth;
        var outPos = _vm.OutPoint / _vm.TotalSeconds * trackWidth;

        Canvas.SetLeft(InMarker, inPos);
        Canvas.SetLeft(OutMarker, outPos);
    }

    private void MediaPlayer_MediaOpened(object sender, RoutedEventArgs e)
    {
        _mediaReady = true;
        if (MediaPlayer.NaturalDuration.HasTimeSpan)
        {
            _vm.TotalSeconds = MediaPlayer.NaturalDuration.TimeSpan.TotalSeconds;
            _vm.OutPoint = _vm.TotalSeconds;
        }
        MediaPlayer.Position = TimeSpan.FromSeconds(_vm.CurrentSeconds);
        if (_playWhenReady)
        {
            _playWhenReady = false;
            MediaPlayer.Play();
            _timer.Start();
        }
    }

    private void MediaPlayer_MediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        _mediaReady = false;
        _playWhenReady = false;
        _vm.IsPlaying = false;
        _timer.Stop();
        PreviewUnavailableMsg.Text = $"プレビュー読み込み失敗\n{e.ErrorException?.Message}";
        PreviewUnavailableMsg.Visibility = Visibility.Visible;
    }

    private void MediaPlayer_MediaEnded(object sender, RoutedEventArgs e)
    {
        _vm.IsPlaying = false;
        _timer.Stop();
        _vm.CurrentSeconds = 0;
        MediaPlayer.Position = TimeSpan.Zero;
        UpdateMarkers();
    }

    private void SeekSlider_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _isDraggingSlider = true;
        _timer.Stop();
        MediaPlayer.Volume = 0;
        _lastSeekTime = DateTime.MinValue;

        var slider = (Slider)sender;
        var point = e.GetPosition(slider);
        var fraction = point.X / slider.ActualWidth;
        var seconds = fraction * _vm.TotalSeconds;
        SeekTo(Math.Max(0, Math.Min(seconds, _vm.TotalSeconds)));
    }

    private void SeekSlider_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingSlider || e.LeftButton != MouseButtonState.Pressed) return;

        var slider = (Slider)sender;
        var point = e.GetPosition(slider);
        var fraction = point.X / slider.ActualWidth;
        var seconds = Math.Max(0, Math.Min(fraction * _vm.TotalSeconds, _vm.TotalSeconds));

        _vm.CurrentSeconds = seconds;
        UpdateMarkers();

        var now = DateTime.UtcNow;
        if (now - _lastSeekTime > _seekThrottle)
        {
            _lastSeekTime = now;
            MediaPlayer.Position = TimeSpan.FromSeconds(seconds);
        }
    }

    private void SeekSlider_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        _isDraggingSlider = false;
        MediaPlayer.Position = TimeSpan.FromSeconds(_vm.CurrentSeconds);
        MediaPlayer.Volume = _vm.Volume / 100.0;
        UpdateMarkers();
        if (_vm.IsPlaying) _timer.Start();
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        _vm.TogglePlay();
        if (_vm.IsPlaying) _timer.Start(); else _timer.Stop();
    }

    private void ToStart_Click(object sender, RoutedEventArgs e) => SeekTo(_vm.InPoint);
    private void ToEnd_Click(object sender, RoutedEventArgs e) => SeekTo(_vm.OutPoint);
    private void FrameBack_Click(object sender, RoutedEventArgs e) => SeekTo(Math.Max(0, _vm.CurrentSeconds - 1.0 / 30.0));
    private void FrameForward_Click(object sender, RoutedEventArgs e) => SeekTo(Math.Min(_vm.TotalSeconds, _vm.CurrentSeconds + 1.0 / 30.0));
    private void SetIn_Click(object sender, RoutedEventArgs e) { _vm.SetInPoint(); UpdateMarkers(); }
    private void SetOut_Click(object sender, RoutedEventArgs e) { _vm.SetOutPoint(); UpdateMarkers(); }

    private void TimeTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            (sender as TextBox)?.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
    }

    private void CurrentTimeTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (ViewModels.CutViewModel.TryParseTime((sender as TextBox)?.Text ?? "", out var s))
                SeekTo(s);
        }
    }

    private async void RunButton_Click(object sender, RoutedEventArgs e)
    {
        try { await _vm.RunAsync(); }
        catch (Exception ex) { MessageBox.Show(ex.Message, AppStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => _vm.Cancel();

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_vm.LastOutputDir))
        {
            try { Process.Start("explorer.exe", _vm.LastOutputDir); }
            catch (Exception ex) { MessageBox.Show(ex.Message, AppStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Error); }
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_vm.IsRunning)
        {
            if (MessageBox.Show("処理中です。キャンセルして閉じますか？", AppStrings.AppName,
                MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK)
                _vm.Cancel();
            else
            {
                e.Cancel = true;
                return;
            }
        }
        _timer.Stop();
        base.OnClosing(e);
    }
}
