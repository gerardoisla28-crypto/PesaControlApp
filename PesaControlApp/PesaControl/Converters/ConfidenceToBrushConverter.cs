using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace PesaControl.UI.Converters;

public class ConfidenceToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double confidence)
        {
            return confidence >= 0.8
                ? new SolidColorBrush(Color.FromRgb(76, 175, 80))
                : confidence >= 0.5
                    ? new SolidColorBrush(Color.FromRgb(255, 152, 0))
                    : new SolidColorBrush(Color.FromRgb(244, 67, 54));
        }
        return Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
