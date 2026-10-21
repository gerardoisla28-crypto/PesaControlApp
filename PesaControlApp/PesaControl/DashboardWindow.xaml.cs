using System.Linq;
using System.Windows;
using Microsoft.EntityFrameworkCore;
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
    private readonly ScaleManager _scaleManager;

    // La base de datos es realmente asíncrona (PostgreSQL): esperar sus consultas con .GetResult() en el
    // hilo de la interfaz provoca un bloqueo, así que se cargan aquí antes de crear la ventana.
    public static async Task<DashboardWindow> CreateAsync(User currentUser)
    {
        var dbService = new DatabaseService();

        var scaleManager = new ScaleManager();
        var scaleConfigs = await LoadScaleConfigsAsync(dbService);
        await scaleManager.InitializeAsync(scaleConfigs);

        using var configCtx = dbService.CreateContext();
        var sysConfigs = await new Repository<SystemConfig>(configCtx).GetAllAsync();

        return new DashboardWindow(currentUser, dbService, scaleManager, sysConfigs);
    }

    private DashboardWindow(User currentUser, DatabaseService dbService, ScaleManager scaleManager, List<SystemConfig> sysConfigs)
    {
        InitializeComponent();

        _scaleManager = scaleManager;

        // Choose data service (local for now; remote mode will be added later)
        IWeighingDataService dataService = new LocalWeighingDataService(dbService);
        var exportImport = new ExportImportService(dbService);

        var weighingVm = new WeighingViewModel(_scaleManager, dataService, dbService);
        var barrierVm = new BarrierViewModel(dbService);
        var trafficLightVm = new TrafficLightViewModel(dbService);

        // Detect if barrier and traffic light share the same serial port (single Arduino)
        TryWireSharedArduinoService(dbService, barrierVm, trafficLightVm);

        var registrosVm = new RegistrosViewModel(dbService);
        var usersVm = new UsersViewModel(dbService, currentUser);
        var controlsVm = new ControlsViewModel(dbService, barrierVm, trafficLightVm);
        var settingsVm = new SettingsViewModel(dbService, _scaleManager, weighingVm, exportImport);
        var catalogsVm = new CatalogsViewModel(dbService);

        var sysDict = sysConfigs.ToDictionary(c => c.Key, c => c.Value);
        var apiKey = sysDict.GetValueOrDefault("platerecognizer_api_key", "");
        IAnprService? anprService = string.IsNullOrWhiteSpace(apiKey)
            ? null
            : new PlateRecognizerAnprService(apiKey);

        var dvrService = new DahuaDvrService();

        _dashboardVm = new DashboardViewModel(currentUser)
        {
            WeighingVM = weighingVm,
            RegistrosVM = registrosVm,
            ControlsVM = controlsVm,
            UsersVM = usersVm,
            SettingsVM = settingsVm,
            CatalogsVM = catalogsVm,
            ReportesVM = new ReportesViewModel(dbService),
            CurrentViewModel = weighingVm
        };

        DataContext = _dashboardVm;

        _ = InitializeCameraAsync(dbService, weighingVm, controlsVm, anprService, dvrService);
        _ = _dashboardVm.LoadModuleConfigAsync(dbService);
    }

    private static async Task<List<ScaleConfig>> LoadScaleConfigsAsync(DatabaseService dbService)
    {
        var ctx = dbService.CreateContext();
        var repo = new Repository<ScaleConfig>(ctx);
        return (await repo.GetAllAsync()).Where(c => c.IsActive).OrderBy(c => c.SortOrder).ToList();
    }

    private static void TryWireSharedArduinoService(DatabaseService dbService, BarrierViewModel barrierVm, TrafficLightViewModel trafficLightVm)
    {
        try
        {
            using var ctx = dbService.CreateContext();
            var barrierConfig = ctx.BarrierConfigs.IgnoreQueryFilters().FirstOrDefault(b => b.IsActive);
            var tlConfig = ctx.TrafficLightConfigs.IgnoreQueryFilters().FirstOrDefault(t => t.IsActive);

            if (barrierConfig == null || tlConfig == null)
                return;

            bool bothSerial = barrierConfig.ConnectionType == DeviceConnectionType.Serial
                           && tlConfig.ConnectionType == DeviceConnectionType.Serial;

            bool samePort = string.Equals(barrierConfig.PortName, tlConfig.PortName, StringComparison.OrdinalIgnoreCase)
                         && barrierConfig.BaudRate == tlConfig.BaudRate;

            if (bothSerial && samePort)
            {
                var arduinoService = new ArduinoAccessService();
                arduinoService.SetBarrierConfig(barrierConfig);
                arduinoService.SetTrafficLightConfig(tlConfig);

                barrierVm.SetExternalService(arduinoService);
                trafficLightVm.SetExternalService(arduinoService);
            }
        }
        catch
        {
            // Silently ignore — will use separate services as fallback
        }
    }

    private async Task InitializeCameraAsync(DatabaseService dbService, WeighingViewModel weighingVm, ControlsViewModel controlsVm, IAnprService? anprService, IDvrService dvrService)
    {
        var cameraService = new CameraService();
        weighingVm.SetCameraService(cameraService);

        var cameraVm = new CameraViewModel(cameraService, dvrService, anprService, dbService);

        cameraVm.PlateDetected += (_, plate) =>
        {
            weighingVm.LicensePlate = plate;
        };

        _dashboardVm.CameraVM = cameraVm;
        weighingVm.CameraVM = cameraVm;
        weighingVm.ControlsVM = controlsVm;
    }
}
