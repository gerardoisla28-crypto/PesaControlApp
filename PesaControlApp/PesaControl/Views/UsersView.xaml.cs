using System.Windows.Controls;
using System.Windows;

namespace PesaControl.UI.Views;

public partial class UsersView : UserControl
{
    public UsersView()
    {
        InitializeComponent();
    }

    private void PasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.UsersViewModel viewModel && sender is PasswordBox passwordBox)
            viewModel.EditPassword = passwordBox.Password;
    }
}
