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
        LoginButton.IsEnabled = false;
        _ = InitializeDatabaseAsync();
    }

    private async Task InitializeDatabaseAsync()
    {
        try
        {
            await _dbService.InitializeAsync();
            if (!await _dbService.HasAdminAsync())
            {
                var setup = new InitialAdminWindow(_dbService) { Owner = this };
                if (setup.ShowDialog() != true || !setup.Completed)
                {
                    Close();
                    return;
                }
            }
            LoginButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo inicializar la base de datos: {ex.Message}", "PesaControl", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
        }
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
