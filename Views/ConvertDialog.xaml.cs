
using System.Windows;
using RShiftTools.Services;
using RShiftTools.ViewModels;

namespace RShiftTools.Views;

public partial class ConvertDialog : Window
{
    private readonly ConvertViewModel _vm;

    public ConvertDialog(List<string> files)
    {
        InitializeComponent();
        _vm = new ConvertViewModel(files, new DialogService());
        DataContext = _vm;
    }

    private async void RunButton_Click(object sender, RoutedEventArgs e)
        => await _vm.RunAsync();

    private void CancelButton_Click(object sender, RoutedEventArgs e)
        => _vm.Cancel();
}