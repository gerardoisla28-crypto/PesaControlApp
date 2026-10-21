using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PesaControl.Core.Models;
using PesaControl.Data.Data;
using PesaControl.Data.Repositories;
using PesaControl.Core.Security;

namespace PesaControl.UI.ViewModels;

public partial class UsersViewModel : ObservableObject, IUnsavedChanges
{
    private readonly DatabaseService _db;
    private readonly User _currentUser;
    private readonly IPasswordHasher _passwordHasher = new Argon2idPasswordHasher();

    public ObservableCollection<User> Users { get; } = [];

    [ObservableProperty] private User? _selectedUser;
    [ObservableProperty] private bool _isEditorOpen;
    [ObservableProperty] private bool _isEditing;

    // Editor fields
    [ObservableProperty] private string _editUsername = "";
    [ObservableProperty] private string _editFullName = "";
    [ObservableProperty] private string _editEmail = "";
    [ObservableProperty] private string _editPassword = "";
    [ObservableProperty] private string _editPasswordConfirm = "";
    [ObservableProperty] private string _editRole = "Operator";
    [ObservableProperty] private bool _editIsActive = true;

    [ObservableProperty] private string _statusMessage = "";

    private readonly EditorTracker _editor;

    partial void OnIsEditorOpenChanged(bool value)
    {
        if (!value) _editor.End();
    }

    public bool HasUnsavedChanges => IsEditorOpen && _editor.IsDirty;
    public string UnsavedChangesDescription => "Usuario";

    public async Task<bool> SaveChangesAsync()
    {
        await SaveUserAsync();
        return !IsEditorOpen;
    }

    public void DiscardChanges() => CancelEdit();

    public List<string> Roles { get; }

    public UsersViewModel(DatabaseService db, User currentUser)
    {
        _db = db;
        _currentUser = currentUser;
        _editor = new EditorTracker(() => EditorTracker.Snap(EditUsername, EditFullName, EditEmail, EditRole, EditIsActive, EditPassword, EditPasswordConfirm));
        Roles = currentUser.Role == UserRoles.SuperAdmin
            ? [UserRoles.SuperAdmin, UserRoles.Admin, UserRoles.Operator, UserRoles.Viewer]
            : [UserRoles.Operator, UserRoles.Viewer];
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
        if (!UserRoles.CanManageUsers(_currentUser.Role)) return;
        SelectedUser = null;
        EditUsername = "";
        EditFullName = "";
        EditEmail = "";
        EditPassword = "";
        EditPasswordConfirm = "";
        EditRole = "Operator";
        EditIsActive = true;
        IsEditing = false;
        _editor.Begin();
        IsEditorOpen = true;
    }

    [RelayCommand]
    private void EditUser()
    {
        if (SelectedUser is null) return;
        if (!UserRoles.CanManageRole(_currentUser.Role, SelectedUser.Role))
        {
            StatusMessage = "No tiene permisos para administrar este usuario";
            return;
        }

        EditUsername = SelectedUser.Username;
        EditFullName = SelectedUser.FullName;
        EditEmail = SelectedUser.Email;
        EditPassword = "";
        EditPasswordConfirm = "";
        EditRole = SelectedUser.Role;
        EditIsActive = SelectedUser.IsActive;
        IsEditing = true;
        _editor.Begin();
        IsEditorOpen = true;
    }

    [RelayCommand]
    private async Task SaveUserAsync()
    {
        EditUsername = EditUsername.Trim();
        var usernameChanged = !IsEditing || SelectedUser is null || EditUsername != SelectedUser.Username;
        if (usernameChanged && !UsernamePolicy.IsValid(EditUsername))
        {
            StatusMessage = UsernamePolicy.Describe();
            return;
        }
        EditFullName = FullNamePolicy.Normalize(EditFullName);
        if (!FullNamePolicy.HasOnlyValidChars(EditFullName))
        {
            StatusMessage = FullNamePolicy.DescribeInvalidChars();
            return;
        }
        if (FullNamePolicy.IsTooLong(EditFullName))
        {
            StatusMessage = FullNamePolicy.DescribeTooLong();
            return;
        }
        if (!Roles.Contains(EditRole))
        {
            StatusMessage = "No tiene permisos para asignar ese rol";
            return;
        }
        if ((!IsEditing || !string.IsNullOrWhiteSpace(EditPassword)) && !PasswordPolicy.IsValid(EditRole, EditPassword))
        {
            StatusMessage = PasswordPolicy.Describe(EditRole);
            return;
        }
        if ((!IsEditing || !string.IsNullOrWhiteSpace(EditPassword)) && EditPassword != EditPasswordConfirm)
        {
            StatusMessage = "Las contraseñas no coinciden";
            return;
        }

        try
        {
            var ctx = _db.CreateContext();
            var repo = new Repository<User>(ctx);

            if (IsEditing && SelectedUser is not null)
            {
                if (!UserRoles.CanManageRole(_currentUser.Role, SelectedUser.Role))
                {
                    StatusMessage = "No tiene permisos para administrar este usuario";
                    return;
                }
                SelectedUser.Username = EditUsername;
                SelectedUser.FullName = EditFullName;
                SelectedUser.Email = EditEmail;
                SelectedUser.Role = EditRole;
                SelectedUser.IsActive = EditIsActive;

                if (!string.IsNullOrWhiteSpace(EditPassword))
                    SelectedUser.PasswordHash = _passwordHasher.Hash(EditPassword);

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
                    PasswordHash = _passwordHasher.Hash(EditPassword),
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
    private void ToggleEditor()
    {
        if (IsEditorOpen)
        {
            IsEditorOpen = false;
            SelectedUser = null;
        }
        else
        {
            if (SelectedUser is not null)
                EditUser();
            else
                NewUser();
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
        if (!UserRoles.CanManageRole(_currentUser.Role, SelectedUser.Role))
        {
            StatusMessage = "No tiene permisos para eliminar este usuario";
            return;
        }

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
        if (value is not null)
            StatusMessage = $"Seleccionado: {value.Username}";
    }

}
