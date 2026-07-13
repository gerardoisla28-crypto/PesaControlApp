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
    public bool IsAdmin => CurrentUser.Role == "Admin";

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

    [RelayCommand]
    private void ShowWeighing()
    {
        CurrentViewModel = WeighingVM;
        WeighingVM?.RefreshCatalogsCommand.Execute(null);
        CurrentSection = "Pesaje";
    }

    [RelayCommand]
    private void ShowCameras()
    {
        CurrentViewModel = CameraVM;
        CurrentSection = "Camaras";
    }

    [RelayCommand]
    private void ShowRegistros()
    {
        CurrentViewModel = RegistrosVM;
        CurrentSection = "Registros";
    }

    [RelayCommand]
    private void ShowControls()
    {
        if (IsControlModuleEnabled)
        {
            CurrentViewModel = ControlsVM;
            CurrentSection = "Control";
        }
    }

    [RelayCommand]
    private void ShowUsers()
    {
        if (!IsAdmin) return;
        CurrentViewModel = UsersVM;
        UsersVM?.LoadUsersCommand.Execute(null);
        CurrentSection = "Usuarios";
    }

    [RelayCommand]
    private void ShowSettings()
    {
        if (!IsAdmin) return;
        CurrentViewModel = SettingsVM;
        SettingsVM?.LoadConfigCommand.Execute(null);
        CurrentSection = "Configuracion";
    }

    [RelayCommand]
    private void ShowCatalogs()
    {
        if (!IsAdmin) return;
        CurrentViewModel = CatalogsVM;
        CatalogsVM?.LoadProductsCommand.Execute(null);
        CatalogsVM?.LoadCompaniesCommand.Execute(null);
        CatalogsVM?.LoadOperatorsCommand.Execute(null);
        CatalogsVM?.LoadDriversCommand.Execute(null);
        CurrentSection = "Catalogos";
    }

    [RelayCommand]
    private void ShowReports()
    {
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
