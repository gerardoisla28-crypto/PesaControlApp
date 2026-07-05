using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PesaControl.Core.Models;
using PesaControl.Data.Data;
using PesaControl.Data.Repositories;

namespace PesaControl.UI.ViewModels;

public partial class UsersViewModel : ObservableObject
{
    private readonly DatabaseService _db;

    public ObservableCollection<User> Users { get; } = [];

    [ObservableProperty] private User? _selectedUser;
    [ObservableProperty] private bool _isEditorOpen;
    [ObservableProperty] private bool _isEditing;

    // Editor fields
    [ObservableProperty] private string _editUsername = "";
    [ObservableProperty] private string _editFullName = "";
    [ObservableProperty] private string _editEmail = "";
    [ObservableProperty] private string _editPassword = "";
    [ObservableProperty] private string _editRole = "Operator";
    [ObservableProperty] private bool _editIsActive = true;

    [ObservableProperty] private string _statusMessage = "";

    public List<string> Roles { get; } = ["Admin", "Operator", "Viewer"];

    public UsersViewModel(DatabaseService db)
    {
        _db = db;
    }

    [RelayCommand]
    private async Task LoadUsersAsync()
    {
        try
        {
            StatusMessage = "Cargando usuarios...";
            var ctx = _db.CreateContext();
            var repo = new Repository<User>(ctx);
            var users = await repo.GetAllAsync();

            Users.Clear();
            foreach (var u in users.OrderBy(u => u.Username))
                Users.Add(u);

            StatusMessage = $"{Users.Count} usuario(s) cargado(s)";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error al cargar: {ex.Message}";
        }
    }

    [RelayCommand]
    private void NewUser()
    {
        SelectedUser = null;
        EditUsername = "";
        EditFullName = "";
        EditEmail = "";
        EditPassword = "";
        EditRole = "Operator";
        EditIsActive = true;
        IsEditing = false;
        IsEditorOpen = true;
    }

    [RelayCommand]
    private void EditUser()
    {
        if (SelectedUser is null) return;

        EditUsername = SelectedUser.Username;
        EditFullName = SelectedUser.FullName;
        EditEmail = SelectedUser.Email;
        EditPassword = "";
        EditRole = SelectedUser.Role;
        EditIsActive = SelectedUser.IsActive;
        IsEditing = true;
        IsEditorOpen = true;
    }

    [RelayCommand]
    private async Task SaveUserAsync()
    {
        if (string.IsNullOrWhiteSpace(EditUsername))
        {
            StatusMessage = "El nombre de usuario es obligatorio";
            return;
        }

        try
        {
            var ctx = _db.CreateContext();
            var repo = new Repository<User>(ctx);

            if (IsEditing && SelectedUser is not null)
            {
                SelectedUser.Username = EditUsername;
                SelectedUser.FullName = EditFullName;
                SelectedUser.Email = EditEmail;
                SelectedUser.Role = EditRole;
                SelectedUser.IsActive = EditIsActive;

                if (!string.IsNullOrWhiteSpace(EditPassword))
                    SelectedUser.Password = EditPassword;

                await repo.UpdateAsync(SelectedUser);
                StatusMessage = "Usuario actualizado correctamente";
            }
            else
            {
                var existing = await repo.FindAsync(u => u.Username == EditUsername);
                if (existing.Count > 0)
                {
                    StatusMessage = "Ya existe un usuario con ese nombre";
                    return;
                }

                var user = new User
                {
                    Username = EditUsername,
                    FullName = EditFullName,
                    Email = EditEmail,
                    Password = string.IsNullOrWhiteSpace(EditPassword) ? "1234" : EditPassword,
                    Role = EditRole,
                    IsActive = EditIsActive,
                    CreatedAt = DateTime.UtcNow
                };

                await repo.AddAsync(user);
                Users.Add(user);
                StatusMessage = "Usuario creado correctamente";
            }

            IsEditorOpen = false;
            SelectedUser = null;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error al guardar: {ex.Message}";
        }
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditorOpen = false;
        SelectedUser = null;
    }

    [RelayCommand]
    private async Task DeleteUserAsync()
    {
        if (SelectedUser is null) return;

        try
        {
            var ctx = _db.CreateContext();
            var repo = new Repository<User>(ctx);

            // Re-fetch to ensure tracking
            var user = await repo.GetByIdAsync(SelectedUser.Id);
            if (user is not null)
            {
                await repo.DeleteAsync(user);
                Users.Remove(SelectedUser);
                StatusMessage = "Usuario eliminado";
            }

            IsEditorOpen = false;
            SelectedUser = null;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error al eliminar: {ex.Message}";
        }
    }

    partial void OnSelectedUserChanged(User? value)
    {
        if (value is not null && !IsEditorOpen)
        {
            EditUserCommand.Execute(null);
        }
    }
}
