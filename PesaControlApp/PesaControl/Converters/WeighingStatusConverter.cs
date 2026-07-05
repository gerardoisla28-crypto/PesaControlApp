using System.Globalization;
using System.Windows.Data;
using PesaControl.Core.Models;

namespace PesaControl.UI.Converters;

public class WeighingStatusConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is WeighingStatus status)
        {
            return status switch
            {
                WeighingStatus.WeighingIn => "Peso de entrada",
                WeighingStatus.WeighingOut => "Peso de salida",
                WeighingStatus.Completed => "Completado",
                WeighingStatus.Cancelled => "Cancelado",
                _ => status.ToString()
            };
        }

        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
