using System.Windows;
using PesaControl.Core.Models;

namespace PesaControl.UI;

public partial class ImportResultDialog : Window
{
    private ImportResultDialog(ExchangeImportResult result)
    {
        InitializeComponent();

        if (!result.Success)
        {
            TitleText.Text = "No se pudo importar el archivo";
            TitleText.Foreground = (System.Windows.Media.Brush)FindResource("AccentRedBrush");
            SummaryText.Text = result.Message;
            return;
        }

        TitleText.Text = result.Conflicts.Count > 0 ? "Importación terminada con conflictos" : "Importación terminada";
        SummaryText.Text = result.Message;
        if (result.Conflicts.Count > 0)
        {
            ConflictsList.ItemsSource = result.Conflicts;
            ConflictsPanel.Visibility = Visibility.Visible;
        }
    }

    public static void Show(ExchangeImportResult result)
    {
        var dialog = new ImportResultDialog(result)
        {
            Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                    ?? Application.Current.MainWindow
        };
        dialog.ShowDialog();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
