using System.Globalization;
using System.Windows.Data;
using RShiftTools.Models;

namespace RShiftTools.Converters;

public class StatusConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is ProcessStatus s
            ? s switch
            {
                ProcessStatus.Waiting => "待機中",
                ProcessStatus.Processing => "処理中",
                ProcessStatus.Done => "完了",
                ProcessStatus.Error => "エラー",
                _ => "",
            }
            : "";

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture
    ) => throw new NotImplementedException();
}
