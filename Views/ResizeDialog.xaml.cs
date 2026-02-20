using System.Windows;
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
        await _vm.InitAsync();
    }

    private async void RunButton_Click(object sender, RoutedEventArgs e) => await _vm.RunAsync();

    private void CancelButton_Click(object sender, RoutedEventArgs e) => _vm.Cancel();
}
