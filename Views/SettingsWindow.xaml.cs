using System.Windows;
using RShiftTools.ViewModels;

namespace RShiftTools.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _vm;

    public SettingsWindow()
    {
        InitializeComponent();
        _vm = new SettingsViewModel();
        DataContext = _vm;
    }

    private void Apply_Click(object sender, RoutedEventArgs e) =>
        _vm.ApplyContextMenu();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
