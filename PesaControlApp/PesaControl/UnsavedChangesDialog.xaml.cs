using System.Windows;

namespace PesaControl.UI;

public enum UnsavedChangesChoice { Save, Discard, Cancel }

public partial class UnsavedChangesDialog : Window
{
    private UnsavedChangesChoice _choice = UnsavedChangesChoice.Cancel;

    private UnsavedChangesDialog(string description)
    {
        InitializeComponent();
        DetailText.Text = $"Hay cambios sin guardar en: {description}.";
    }

    // Cerrar la ventana con la X o con Esc equivale a Cancelar: nunca se pierde nada por accidente.
    public static UnsavedChangesChoice Ask(string description)
    {
        var dialog = new UnsavedChangesDialog(description)
        {
            Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                    ?? Application.Current.MainWindow
        };
        dialog.ShowDialog();
        return dialog._choice;
    }

    private void Save_Click(object sender, RoutedEventArgs e) => Finish(UnsavedChangesChoice.Save);

    private void Discard_Click(object sender, RoutedEventArgs e) => Finish(UnsavedChangesChoice.Discard);

    private void Cancel_Click(object sender, RoutedEventArgs e) => Finish(UnsavedChangesChoice.Cancel);

    private void Finish(UnsavedChangesChoice choice)
    {
        _choice = choice;
        Close();
    }
}
