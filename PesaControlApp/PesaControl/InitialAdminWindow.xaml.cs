using System.Windows;
using System.Windows.Controls;
using PesaControl.Core.Models;
using PesaControl.Core.Security;
using PesaControl.Data.Data;

namespace PesaControl.UI;

public partial class InitialAdminWindow : Window
{
    private readonly DatabaseService _database;
    public bool Completed { get; private set; }
    public string? CreatedUsername { get; private set; }

    public InitialAdminWindow(DatabaseService database)
    {
        InitializeComponent();
        _database = database;
        PasswordLabel.Text = $"Contraseña (mínimo {PasswordPolicy.MinLength(UserRoles.Admin)} caracteres) *";

        UsernameBox.TextChanged += (_, _) => UsernameError.Text = "";
        FullNameBox.TextChanged += (_, _) => FullNameError.Text = "";
        PasswordBox.PasswordChanged += (_, _) => PasswordError.Text = "";
        ConfirmPasswordBox.PasswordChanged += (_, _) => ConfirmPasswordError.Text = "";
    }

    private async void Create_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = "";
        var username = UsernameBox.Text.Trim();
        var fullName = FullNamePolicy.Normalize(FullNameBox.Text);

        UsernameError.Text = UsernamePolicy.IsValid(username) ? "" : UsernamePolicy.Describe();
        FullNameError.Text = !FullNamePolicy.HasNameAndSurname(fullName) ? "Ingrese nombre y apellido."
            : !FullNamePolicy.HasOnlyValidChars(fullName) ? FullNamePolicy.DescribeInvalidChars()
            : FullNamePolicy.IsTooLong(fullName) ? FullNamePolicy.DescribeTooLong() : "";
        PasswordError.Text = PasswordPolicy.IsValid(UserRoles.Admin, PasswordBox.Password)
            ? "" : PasswordPolicy.Describe(UserRoles.Admin);
        ConfirmPasswordError.Text = PasswordBox.Password == ConfirmPasswordBox.Password
            ? "" : "Las contraseñas no coinciden.";

        Control? firstInvalid =
            UsernameError.Text != "" ? UsernameBox :
            FullNameError.Text != "" ? FullNameBox :
            PasswordError.Text != "" ? PasswordBox :
            ConfirmPasswordError.Text != "" ? ConfirmPasswordBox : null;
        if (firstInvalid is not null)
        {
            firstInvalid.Focus();
            return;
        }

        try
        {
            await _database.CreateInitialAdminAsync(username, PasswordBox.Password, fullName);
            Completed = true;
            CreatedUsername = username;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
        }
    }
}
