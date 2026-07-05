using System.Windows;
using PesaControl.UI.Services;
using PesaControl.Core.Models;
using PesaControl.Data.Data;

namespace PesaControl.UI;

public partial class MainWindow : Window
{
    private readonly AuthService _authService;
    private readonly DatabaseService _dbService;

    public MainWindow()
    {
        InitializeComponent();
        _dbService = new DatabaseService();
        _authService = new AuthService(_dbService);
        _ = InitializeDatabaseAsync();
    }

    private async Task InitializeDatabaseAsync()
    {
        await _dbService.InitializeAsync();
    }

    private async void Login_Click(object sender, RoutedEventArgs e)
    {
        User? user = await _authService.LoginAsync(
            txtUser.Text,
            txtPass.Password
        );

        if (user != null)
        {
            var dashboard = new DashboardWindow(user);
            dashboard.Show();
            Close();
        }
        else
        {
            MessageBox.Show("Usuario o contraseña incorrectos");
        }
    }
}
