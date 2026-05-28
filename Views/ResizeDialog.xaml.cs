using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using RShiftTools.Services;
using RShiftTools.ViewModels;

namespace RShiftTools.Views;

public partial class ResizeDialog : Window
{
    private readonly ResizeViewModel _vm;

    public ResizeDialog(List<string> files)
    {
        InitializeComponent();
        _vm = new ResizeViewModel(files, new DialogService());
        DataContext = _vm;
    }

    protected override async void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        try
        {
            await _vm.InitAsync();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                ex.Message,
                RShiftTools.Services.AppStrings.AppName,
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );
        }
    }

    private async void RunButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _vm.RunAsync();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                ex.Message,
                RShiftTools.Services.AppStrings.AppName,
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => _vm.Cancel();

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_vm.LastOutputDir))
        {
            try { Process.Start("explorer.exe", _vm.LastOutputDir); }
            catch (Exception ex) { System.Windows.MessageBox.Show(ex.Message, AppStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Error); }
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_vm.IsRunning)
        {
            var result = System.Windows.MessageBox.Show(
                "処理中です。キャンセルして閉じますか？",
                AppStrings.AppName,
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question
            );
            if (result == MessageBoxResult.OK)
                _vm.Cancel();
            else
                e.Cancel = true;
        }
        base.OnClosing(e);
    }

    private static readonly Regex _digitsOnly = new(@"^\d+$");

    private void NumericTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !_digitsOnly.IsMatch(e.Text);
    }
}
