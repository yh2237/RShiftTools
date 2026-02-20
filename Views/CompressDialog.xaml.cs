using System.Windows;
using RShiftTools.Services;
using RShiftTools.ViewModels;

namespace RShiftTools.Views;

public partial class CompressDialog : Window
{
    private readonly CompressViewModel _vm;

    public CompressDialog(List<string> files)
    {
        InitializeComponent();
        _vm = new CompressViewModel(files, new DialogService());
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
