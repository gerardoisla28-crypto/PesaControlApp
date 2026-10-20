using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PesaControl.UI.Services;
using PesaControl.Core.Models;
using PesaControl.Data.Data;

namespace PesaControl.UI;

public partial class MainWindow : Window
{
    private readonly AuthService _authService;

    public MainWindow(DatabaseService dbService, string? createdAdminUsername = null)
    {
        InitializeComponent();
        _authService = new AuthService(dbService);

        if (createdAdminUsername is not null)
        {
            InfoText.Text = $"Admin creado. Inicie sesión con el usuario {createdAdminUsername}.";
            InfoText.Visibility = Visibility.Visible;
            txtUser.Text = createdAdminUsername;
        }

        txtUser.TextChanged += (_, _) => { UserError.Text = ""; ErrorText.Text = ""; };
        txtPass.PasswordChanged += (_, _) => { PassError.Text = ""; ErrorText.Text = ""; };
        Loaded += (_, _) => (string.IsNullOrEmpty(txtUser.Text) ? (UIElement)txtUser : txtPass).Focus();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }

    // Sin barra de título, la ventana se arrastra desde el fondo de la tarjeta (no desde los campos).
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (e.OriginalSource is Border or TextBlock or StackPanel or Grid)
            DragMove();
    }

    private async void Login_Click(object sender, RoutedEventArgs e)
    {
        var username = txtUser.Text.Trim();
        UserError.Text = username.Length == 0 ? "Ingrese su usuario." : "";
        PassError.Text = txtPass.Password.Length == 0 ? "Ingrese su contraseña." : "";
        if (UserError.Text != "" || PassError.Text != "")
        {
            (UserError.Text != "" ? (UIElement)txtUser : txtPass).Focus();
            return;
        }

        LoginButton.IsEnabled = false;
        try
        {
            User? user = await _authService.LoginAsync(username, txtPass.Password);

            if (user != null)
            {
                var dashboard = await DashboardWindow.CreateAsync(user);
                dashboard.Show();
                Close();
            }
            else
            {
                // Vaciar la contraseña dispara PasswordChanged, que borra el error; por eso va antes.
                txtPass.Clear();
                InfoText.Visibility = Visibility.Collapsed;
                ErrorText.Text = "Usuario o contraseña incorrectos.";
                txtPass.Focus();
            }
        }
        catch (Exception)
        {
            ErrorText.Text = "No se pudo conectar con la base de datos.";
        }
        finally
        {
            LoginButton.IsEnabled = true;
        }
    }
}
