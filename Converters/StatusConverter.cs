using System.Globalization;
using System.Windows.Data;
using RShiftTools.Models;
using RShiftTools.Services;

namespace RShiftTools.Converters;

public class StatusConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is ProcessStatus s
            ? s switch
            {
                ProcessStatus.Waiting => AppStrings.Status_Waiting,
                ProcessStatus.Processing => AppStrings.Status_Processing,
                ProcessStatus.Done => AppStrings.Status_Success,
                ProcessStatus.Error => AppStrings.Status_Error,
                ProcessStatus.Cancelled => AppStrings.Status_Cancelled,
                _ => "",
            }
            : "";

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture
    ) => System.Windows.Data.Binding.DoNothing;
}
