using System.Windows;
using System.Windows.Controls;

namespace RShiftTools.Views;

public partial class ModeSelectDialog : Window
{
    public string? SelectedMode { get; private set; }

    public ModeSelectDialog()
    {
        InitializeComponent();
    }

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        foreach (RadioButton rb in ModePanel.Children)
        {
            if (rb.IsChecked == true)
            {
                SelectedMode = rb.Tag as string;
                break;
            }
        }
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
