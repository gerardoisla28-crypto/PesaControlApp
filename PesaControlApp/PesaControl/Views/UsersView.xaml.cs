using System.ComponentModel;
using System.Windows.Controls;
using System.Windows;

namespace PesaControl.UI.Views;

public partial class UsersView : UserControl
{
    public UsersView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is INotifyPropertyChanged old) old.PropertyChanged -= OnViewModelPropertyChanged;
            if (e.NewValue is INotifyPropertyChanged current) current.PropertyChanged += OnViewModelPropertyChanged;
        };
    }

    private void PasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.UsersViewModel viewModel && sender is PasswordBox passwordBox)
            viewModel.EditPassword = passwordBox.Password;
    }

    private void ConfirmPasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.UsersViewModel viewModel && sender is PasswordBox passwordBox)
            viewModel.EditPasswordConfirm = passwordBox.Password;
    }

    // PasswordBox no admite binding: al abrir el editor el ViewModel vacía las contraseñas y hay que limpiar los campos.
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not ViewModels.UsersViewModel viewModel) return;
        if (e.PropertyName == nameof(ViewModels.UsersViewModel.EditPassword) && viewModel.EditPassword.Length == 0)
            PasswordInput.Clear();
        else if (e.PropertyName == nameof(ViewModels.UsersViewModel.EditPasswordConfirm) && viewModel.EditPasswordConfirm.Length == 0)
            ConfirmPasswordInput.Clear();
    }
}
