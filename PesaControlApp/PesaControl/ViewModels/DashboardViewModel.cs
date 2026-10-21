using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PesaControl.Core.Models;
using PesaControl.Data.Data;
using PesaControl.Data.Repositories;

namespace PesaControl.UI.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    [ObservableProperty]
    private object? _currentViewModel;

    [ObservableProperty]
    private string _currentSection = "Pesaje";

    public User CurrentUser { get; }
    public bool IsAdmin => UserRoles.CanManageUsers(CurrentUser.Role);

    public WeighingViewModel? WeighingVM { get; set; }
    public CameraViewModel? CameraVM { get; set; }
    public RegistrosViewModel? RegistrosVM { get; set; }
    public ControlsViewModel? ControlsVM { get; set; }
    public UsersViewModel? UsersVM { get; set; }
    public SettingsViewModel? SettingsVM { get; set; }
    public CatalogsViewModel? CatalogsVM { get; set; }
    public ReportesViewModel? ReportesVM { get; set; }

    [ObservableProperty] private bool _isControlModuleEnabled = true;

    public DashboardViewModel(User currentUser)
    {
        CurrentUser = currentUser;
    }

    public bool HasPendingChanges => CurrentViewModel is IUnsavedChanges { HasUnsavedChanges: true };

    // Si la pantalla actual tiene cambios sin guardar, pregunta antes de salir de ella o de la app.
    public async Task<bool> CanLeaveCurrentAsync()
    {
        if (CurrentViewModel is not IUnsavedChanges { HasUnsavedChanges: true } pending)
            return true;

        switch (UnsavedChangesDialog.Ask(pending.UnsavedChangesDescription))
        {
            case UnsavedChangesChoice.Save:
                return await pending.SaveChangesAsync();
            case UnsavedChangesChoice.Discard:
                pending.DiscardChanges();
                return true;
            default:
                return false;
        }
    }

    [RelayCommand]
    private async Task ShowWeighingAsync()
    {
        if (!await CanLeaveCurrentAsync()) return;
        CurrentViewModel = WeighingVM;
        WeighingVM?.RefreshCatalogsCommand.Execute(null);
        CurrentSection = "Pesaje";
    }

    [RelayCommand]
    private async Task ShowCamerasAsync()
    {
        if (!await CanLeaveCurrentAsync()) return;
        CurrentViewModel = CameraVM;
        CurrentSection = "Camaras";
    }

    [RelayCommand]
    private async Task ShowRegistrosAsync()
    {
        if (!await CanLeaveCurrentAsync()) return;
        CurrentViewModel = RegistrosVM;
        CurrentSection = "Registros";
    }

    [RelayCommand]
    private async Task ShowControlsAsync()
    {
        if (!await CanLeaveCurrentAsync()) return;
        if (IsControlModuleEnabled)
        {
            CurrentViewModel = ControlsVM;
            CurrentSection = "Control";
        }
    }

    [RelayCommand]
    private async Task ShowUsersAsync()
    {
        if (!await CanLeaveCurrentAsync()) return;
        if (!IsAdmin) return;
        CurrentViewModel = UsersVM;
        UsersVM?.LoadUsersCommand.Execute(null);
        CurrentSection = "Usuarios";
    }

    [RelayCommand]
    private async Task ShowSettingsAsync()
    {
        if (!await CanLeaveCurrentAsync()) return;
        if (!IsAdmin) return;
        CurrentViewModel = SettingsVM;
        SettingsVM?.LoadConfigCommand.Execute(null);
        CurrentSection = "Configuracion";
    }

    [RelayCommand]
    private async Task ShowCatalogsAsync()
    {
        if (!await CanLeaveCurrentAsync()) return;
        if (!IsAdmin) return;
        CurrentViewModel = CatalogsVM;
        CatalogsVM?.LoadProductsCommand.Execute(null);
        CatalogsVM?.LoadCompaniesCommand.Execute(null);
        CatalogsVM?.LoadOperatorsCommand.Execute(null);
        CatalogsVM?.LoadDriversCommand.Execute(null);
        CurrentSection = "Catalogos";
    }

    [RelayCommand]
    private async Task ShowReportsAsync()
    {
        if (!await CanLeaveCurrentAsync()) return;
        CurrentViewModel = ReportesVM;
        CurrentSection = "Reportes";
    }

    public async Task LoadModuleConfigAsync(DatabaseService dbService)
    {
        var ctx = dbService.CreateContext();
        var repo = new Repository<SystemConfig>(ctx);
        var configs = await repo.GetAllAsync();
        var dict = configs.ToDictionary(c => c.Key, c => c.Value);

        App.Current.Dispatcher.Invoke(() =>
        {
            IsControlModuleEnabled = dict.GetValueOrDefault("module_control_enabled", "true") == "true";
        });
    }
}
