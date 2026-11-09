using System.Globalization;
using System.Windows.Data;
using PesaControl.Core.Models;

namespace PesaControl.UI.Converters;

// Muestra el nombre de un rol en español ("Operator" → "Operador"); el valor guardado no cambia.
public class RoleNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => UserRoles.DisplayName(value as string);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
