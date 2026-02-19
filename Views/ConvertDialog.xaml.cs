using System.Globalization;
using System.Windows;
using System.Windows.Data;
using RShiftTools.Models;
using RShiftTools.ViewModels;

namespace RShiftTools.Views;

public partial class ConvertDialog : Window
{
    private readonly ConvertViewModel _vm;

    public ConvertDialog(List<string> files)
    {
        InitializeComponent();
        _vm = new ConvertViewModel(files);
        DataContext = _vm;
    }

    private async void RunButton_Click(object sender, RoutedEventArgs e)
    {
        await _vm.RunAsync();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _vm.Cancel();
    }
}

public class StatusConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is ProcessStatus s ? s switch
        {
            ProcessStatus.Waiting    => "待機中",
            ProcessStatus.Processing => "処理中",
            ProcessStatus.Done       => "完了",
            ProcessStatus.Error      => "エラー",
            _                        => ""
        } : "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}