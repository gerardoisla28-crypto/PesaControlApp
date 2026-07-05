using System.Globalization;
using System.Windows.Data;

namespace PesaControl.UI.Converters;

public class BoolToStringConverter : IValueConverter
{
    public string TrueString { get; set; } = "Sí";
    public string FalseString { get; set; } = "No";

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? TrueString : FalseString;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
