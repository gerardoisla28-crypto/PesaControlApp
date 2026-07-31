using System.Windows;
using PesaControl.Data.Data;

namespace PesaControl.UI;

public partial class InitialAdminWindow : Window
{
    private readonly DatabaseService _database;
    public bool Completed { get; private set; }

    public InitialAdminWindow(DatabaseService database)
    {
        InitializeComponent();
        _database = database;
    }

    private async void Create_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _database.CreateInitialAdminAsync(UsernameBox.Text.Trim(), PasswordBox.Password, FullNameBox.Text.Trim());
            Completed = true;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
            PasswordBox.Clear();
        }
    }
}
