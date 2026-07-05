using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace PesaControl.UI.Converters;

public class BoolToBrushConverter : IValueConverter
{
    public Brush TrueBrush { get; set; } = new SolidColorBrush(Color.FromRgb(76, 175, 80));
    public Brush FalseBrush { get; set; } = new SolidColorBrush(Color.FromRgb(244, 67, 54));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? TrueBrush : FalseBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
