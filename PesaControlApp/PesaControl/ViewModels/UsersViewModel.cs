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
    [ObservableProperty] private string _editPassword = "";
    [ObservableProperty] private string _editPasswordConfirm = "";
    [ObservableProperty] private string _editRole = "Operator";
    [ObservableProperty] private bool _editIsActive = true;

    [ObservableProperty] private string _statusMessage = "";

    // Per-field validation errors shown under each editor field
    [ObservableProperty] private string _usernameError = "";
    [ObservableProperty] private string _fullNameError = "";
    [ObservableProperty] private string _roleError = "";
    [ObservableProperty] private string _passwordError = "";
    [ObservableProperty] private string _passwordConfirmError = "";

    private void ClearFieldErrors()
    {
        UsernameError = "";
        FullNameError = "";
        RoleError = "";
        PasswordError = "";
        PasswordConfirmError = "";
    }

    // A field's error disappears as soon as the user edits that field
    partial void OnEditUsernameChanged(string value) => UsernameError = "";
    partial void OnEditFullNameChanged(string value) => FullNameError = "";
    partial void OnEditRoleChanged(string value) => RoleError = "";
    partial void OnEditPasswordChanged(string value) => PasswordError = "";
    partial void OnEditPasswordConfirmChanged(string value) => PasswordConfirmError = "";

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
        _editor = new EditorTracker(() => EditorTracker.Snap(EditUsername, EditFullName, EditRole, EditIsActive, EditPassword, EditPasswordConfirm));
        Roles = currentUser.Role == UserRoles.SuperAdmin
            ? [UserRoles.SuperAdmin, UserRoles.Admin, UserRoles.Operator]
            : [UserRoles.Operator];
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

    // Asks what to do with an open editor that has changes before another one replaces it
    private async Task<bool> ConfirmReplaceEditorAsync()
    {
        if (!HasUnsavedChanges) return true;
        switch (UnsavedChangesDialog.Ask(UnsavedChangesDescription))
        {
            case UnsavedChangesChoice.Save:
                return await SaveChangesAsync();
            case UnsavedChangesChoice.Discard:
                return true;
            default:
                return false;
        }
    }

    [RelayCommand]
    private async Task NewUserAsync()
    {
        if (!UserRoles.CanManageUsers(_currentUser.Role)) return;
        if (!await ConfirmReplaceEditorAsync()) return;
        SelectedUser = null;
        EditUsername = "";
        EditFullName = "";
        EditPassword = "";
        EditPasswordConfirm = "";
        EditRole = "Operator";
        EditIsActive = true;
        ClearFieldErrors();
        IsEditing = false;
        _editor.Begin();
        IsEditorOpen = true;
    }

    [RelayCommand]
    private async Task EditUserAsync(User? user)
    {
        var target = user ?? SelectedUser;
        if (target is null) return;
        if (!UserRoles.CanManageRole(_currentUser.Role, target.Role))
        {
            StatusMessage = "No tiene permisos para administrar este usuario";
            return;
        }
        if (!await ConfirmReplaceEditorAsync()) return;
        SelectedUser = target;

        EditUsername = SelectedUser.Username;
        EditFullName = SelectedUser.FullName;
        EditPassword = "";
        EditPasswordConfirm = "";
        EditRole = SelectedUser.Role;
        EditIsActive = SelectedUser.IsActive;
        ClearFieldErrors();
        IsEditing = true;
        _editor.Begin();
        IsEditorOpen = true;
    }

    [RelayCommand]
    private async Task SaveUserAsync()
    {
        ClearFieldErrors();
        StatusMessage = "";

        EditUsername = EditUsername.Trim();
        var usernameChanged = !IsEditing || SelectedUser is null || EditUsername != SelectedUser.Username;
        if (usernameChanged && !UsernamePolicy.IsValid(EditUsername))
            UsernameError = UsernamePolicy.Describe();

        EditFullName = FullNamePolicy.Normalize(EditFullName);
        if (!FullNamePolicy.HasNameAndSurname(EditFullName))
            FullNameError = "Ingrese nombre y apellido.";
        else if (!FullNamePolicy.HasOnlyValidChars(EditFullName))
            FullNameError = FullNamePolicy.DescribeInvalidChars();
        else if (FullNamePolicy.IsTooLong(EditFullName))
            FullNameError = FullNamePolicy.DescribeTooLong();

        if (!Roles.Contains(EditRole))
            RoleError = "No tiene permisos para asignar ese rol";

        var checkPassword = !IsEditing || !string.IsNullOrWhiteSpace(EditPassword);
        if (checkPassword && !PasswordPolicy.IsValid(EditRole, EditPassword))
            PasswordError = PasswordPolicy.Describe(EditRole);
        if (checkPassword && EditPassword != EditPasswordConfirm)
            PasswordConfirmError = "Las contraseñas no coinciden";

        if (UsernameError != "" || FullNameError != "" || RoleError != ""
            || PasswordError != "" || PasswordConfirmError != "")
            return;

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
                if (usernameChanged)
                {
                    var taken = await repo.FindAsync(u => u.Username == EditUsername && u.Id != SelectedUser.Id);
                    if (taken.Count > 0)
                    {
                        UsernameError = "Ya existe un usuario con este nombre de usuario";
                        return;
                    }
                }
                SelectedUser.Username = EditUsername;
                SelectedUser.FullName = EditFullName;
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
                    UsernameError = "Ya existe un usuario con este nombre de usuario";
                    return;
                }

                var user = new User
                {
                    Username = EditUsername,
                    FullName = EditFullName,
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
    private async Task ToggleEditorAsync()
    {
        if (IsEditorOpen)
        {
            IsEditorOpen = false;
            SelectedUser = null;
        }
        else
        {
            if (SelectedUser is not null)
                await EditUserAsync(null);
            else
                await NewUserAsync();
        }
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditorOpen = false;
        SelectedUser = null;
    }

    [RelayCommand]
    private async Task DeleteUserAsync(User? target)
    {
        if (target is not null) SelectedUser = target;
        if (SelectedUser is null) return;
        if (!UserRoles.CanManageRole(_currentUser.Role, SelectedUser.Role))
        {
            StatusMessage = "No tiene permisos para eliminar este usuario";
            return;
        }

        var confirm = System.Windows.MessageBox.Show(
            $"¿Eliminar al usuario \"{SelectedUser.Username}\"?",
            "Eliminar usuario",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);
        if (confirm != System.Windows.MessageBoxResult.Yes) return;

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
