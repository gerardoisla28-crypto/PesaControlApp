using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using PesaControl.Core.Models;

namespace PesaControl.UI.Converters;

public class TrafficLightColorToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is TrafficLightColor color)
        {
            return color switch
            {
                TrafficLightColor.Red => new SolidColorBrush(Color.FromRgb(244, 67, 54)),
                TrafficLightColor.Yellow => new SolidColorBrush(Color.FromRgb(255, 152, 0)),
                TrafficLightColor.Green => new SolidColorBrush(Color.FromRgb(76, 175, 80)),
                TrafficLightColor.Off => new SolidColorBrush(Color.FromRgb(85, 85, 85)),
                _ => new SolidColorBrush(Color.FromRgb(85, 85, 85))
            };
        }
        return Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
