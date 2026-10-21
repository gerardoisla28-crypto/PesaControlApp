using System.Windows;
using PesaControl.Core.Security;
using PesaControl.Data.Data;

namespace PesaControl.UI;

public partial class StationCodeWindow : Window
{
    private readonly DatabaseService _database;
    public bool Completed { get; private set; }

    public StationCodeWindow(DatabaseService database)
    {
        InitializeComponent();
        _database = database;
        IntroText.Text = "Identifica este puesto en los números de ticket. " +
                         $"Con el código STGO, el primer ticket del mes será STGO-{DateTime.Now:yyyy-MM}-0001.";
        ExamplesText.Text = $"Ejemplos: {StationCodePolicy.Example}.";
        CodeBox.TextChanged += (_, _) =>
        {
            CodeError.Text = "";
            CodeCounter.Text = $"{CodeBox.Text.Length}/{StationCodePolicy.MaxLength}";
        };
        CodeCounter.Text = $"0/{StationCodePolicy.MaxLength}";
        Loaded += (_, _) => CodeBox.Focus();
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        var code = StationCodePolicy.Normalize(CodeBox.Text);
        if (!StationCodePolicy.IsValid(code))
        {
            CodeError.Text = StationCodePolicy.Describe();
            CodeBox.Focus();
            return;
        }

        try
        {
            await _database.SetStationCodeAsync(code);
            Completed = true;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            CodeError.Text = ex.Message;
        }
    }
}
