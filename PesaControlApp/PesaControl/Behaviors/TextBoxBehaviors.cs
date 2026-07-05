using System.Windows;
using System.Windows.Controls;
using PesaControl.Core.Services;

namespace PesaControl.UI.Behaviors;

public static class TextBoxBehaviors
{
    public static readonly DependencyProperty PlateCountryProperty =
        DependencyProperty.RegisterAttached(
            "PlateCountry",
            typeof(string),
            typeof(TextBoxBehaviors),
            new PropertyMetadata(null, OnPlateCountryChanged));

    public static void SetPlateCountry(TextBox element, string value) =>
        element.SetValue(PlateCountryProperty, value);

    public static string GetPlateCountry(TextBox element) =>
        (string)element.GetValue(PlateCountryProperty);

    private static void OnPlateCountryChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextBox tb)
        {
            tb.TextChanged -= OnTextChanged;
            tb.TextChanged += OnTextChanged;

            if (e.NewValue is string country && !string.IsNullOrEmpty(country))
                FormatTextBox(tb, country);
        }
    }

    private static void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        var tb = (TextBox)sender;
        var country = GetPlateCountry(tb);
        if (string.IsNullOrEmpty(country)) return;

        FormatTextBox(tb, country);
    }

    private static void FormatTextBox(TextBox tb, string country)
    {
        var caret = tb.CaretIndex;
        var text = tb.Text;

        var formatted = PlateFormatter.Format(text, country);
        if (formatted != text)
        {
            var diff = formatted.Length - text.Length;
            tb.Text = formatted;
            tb.CaretIndex = caret + diff;
        }
    }
}
