using System.Linq;
using System.Windows;
using PesaControl.Core.Models;
using PesaControl.Core.Services;
using PesaControl.Data.Data;
using PesaControl.Data.Repositories;
using PesaControl.Data.Services;
using PesaControl.UI.Services;
using PesaControl.UI.ViewModels;

namespace PesaControl.UI;

public partial class DashboardWindow : Window
{
    private readonly DashboardViewModel _dashboardVm;

    public DashboardWindow(User currentUser)
    {
        InitializeComponent();

        var dbService = new DatabaseService();
        var weighingService = new WeighingService(new SerialProtocol(), new CommunicationConfig());
        var exportImport = new ExportImportService(dbService);

        var weighingVm = new WeighingViewModel(weighingService, dbService);
        var barrierVm = new BarrierViewModel(dbService);
        var trafficLightVm = new TrafficLightViewModel(dbService);
        var registrosVm = new RegistrosViewModel(dbService);
        var usersVm = new UsersViewModel(dbService);
        var controlsVm = new ControlsViewModel(barrierVm, trafficLightVm);
        var settingsVm = new SettingsViewModel(dbService, exportImport);
        var catalogsVm = new CatalogsViewModel(dbService);

        _dashboardVm = new DashboardViewModel(currentUser)
        {
            WeighingVM = weighingVm,
            RegistrosVM = registrosVm,
            ControlsVM = controlsVm,
            UsersVM = usersVm,
            SettingsVM = settingsVm,
            CatalogsVM = catalogsVm,
            ReportsPlaceholder = new PlaceholderViewModel
            {
                Title = "Reportes",
                Message = "Módulo en desarrollo — próximamente disponible"
            },
            CurrentViewModel = weighingVm
        };

        DataContext = _dashboardVm;

        _ = InitializeCameraAsync(dbService, weighingVm);
        _ = _dashboardVm.LoadModuleConfigAsync(dbService);
    }

    private async Task InitializeCameraAsync(DatabaseService dbService, WeighingViewModel weighingVm)
    {
        var configCtx = dbService.CreateContext();
        var configRepo = new Repository<SystemConfig>(configCtx);
        var configs = await configRepo.GetAllAsync();
        var dict = configs.ToDictionary(c => c.Key, c => c.Value);
        var apiKey = dict.GetValueOrDefault("platerecognizer_api_key", "");

        var cameraService = new CameraService();
        IAnprService? anprService = string.IsNullOrWhiteSpace(apiKey)
            ? null
            : new PlateRecognizerAnprService(apiKey);

        weighingVm.SetCameraService(cameraService);

        var cameraVm = new CameraViewModel(cameraService, anprService, dbService);

        cameraVm.PlateDetected += (_, plate) =>
        {
            weighingVm.LicensePlate = plate;
        };

        _dashboardVm.CameraVM = cameraVm;
    }
}
