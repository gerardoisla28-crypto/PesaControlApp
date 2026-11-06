using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using PesaControl.Core.Models;
using PesaControl.Core.Services;
using PesaControl.Data.Data;
using PesaControl.Data.Repositories;
using PesaControl.UI.Services;

namespace PesaControl.UI.ViewModels;

public partial class WeighingViewModel : ObservableObject
{
    private readonly ScaleManager _scaleManager;
    private readonly IWeighingDataService _dataService;
    private readonly DatabaseService? _dbService;
    private ICameraService? _cameraService;

    private WeighingRecord? _currentRecord;
    private WeighingRecord? _lastCompletedRecord;

    public WeighingViewModel(ScaleManager scaleManager, IWeighingDataService dataService, DatabaseService? dbService = null)
    {
        _scaleManager = scaleManager;
        _dataService = dataService;
        _dbService = dbService;

        OperatorsView = CollectionViewSource.GetDefaultView(AllOperators);
        OperatorsView.Filter = FilterOperator;
        DriversView = CollectionViewSource.GetDefaultView(AllDrivers);
        DriversView.Filter = FilterDriver;

        WireScaleEvents();
        _ = LoadCatalogsAsync();
        _ = LoadActiveWeighingsAsync();
        _ = LoadOperatorsAsync();
        _ = LoadDriversAsync();
        _ = InitializeScalesAsync();
    }

    // ── Scale displays ─────────────────────────────────────

    public ObservableCollection<ScaleDisplay> ScaleDisplays { get; } = [];
    public ObservableCollection<string> AvailableScaleNames { get; } = [];
    public bool IsMultiScale => ScaleDisplays.Count > 1;

    private async Task InitializeScalesAsync()
    {
        SyncScaleDisplays();

        var entryScale = ScaleDisplays.FirstOrDefault(d => d.Name == EntryScaleName);
        if (entryScale != null)
            EntryWeightText = WeightFormat.FormatKg(entryScale.Weight, DecimalsOf(entryScale.Id));

        var exitScale = ScaleDisplays.FirstOrDefault(d => d.Name == ExitScaleName);
        if (exitScale != null)
            ExitWeightText = WeightFormat.FormatKg(exitScale.Weight, DecimalsOf(exitScale.Id));

        OnPropertyChanged(nameof(IsMultiScale));
    }

    /// <summary>Se llama al agregar, editar o eliminar una báscula: ajusta los recuadros de la pantalla principal.</summary>
    private int DecimalsOf(int scaleId) => _scaleManager.GetById(scaleId)?.WeightDecimals ?? 0;

    public void RefreshScaleNames() => SyncScaleDisplays();

    // Id 0 = recuadro de reserva: la pantalla principal nunca queda sin recuadro de peso.
    private const int PlaceholderScaleId = 0;

    private void SyncScaleDisplays()
    {
        RunOnUi(() =>
        {
            var scales = _scaleManager.Scales;

            // Quitar los recuadros de básculas que ya no existen, y el de reserva si ya hay básculas reales.
            foreach (var d in ScaleDisplays.ToList())
            {
                var obsolete = d.Id == PlaceholderScaleId
                    ? scales.Count > 0
                    : !scales.Any(s => s.Config.Id == d.Id);
                if (obsolete)
                    ScaleDisplays.Remove(d);
            }

            // Agregar los nuevos y refrescar el nombre de los existentes.
            foreach (var scale in scales)
            {
                var display = ScaleDisplays.FirstOrDefault(d => d.Id == scale.Config.Id);
                if (display is null)
                {
                    ScaleDisplays.Add(new ScaleDisplay(scale.Config.Id, scale.Name)
                    {
                        Decimals = scale.WeightDecimals,
                        Weight = scale.CurrentWeight,
                        IsStable = scale.IsStable,
                        IsConnected = scale.IsConnected,
                        StatusText = scale.IsConnected ? "Conectado" : "Desconectado"
                    });
                }
                else
                {
                    display.Name = scale.Config.Name;
                }
            }

            if (ScaleDisplays.Count == 0)
                ScaleDisplays.Add(new ScaleDisplay(PlaceholderScaleId, "Báscula 1"));

            // Solo se reconstruye la lista de nombres si cambió, para no perder la selección de entrada/salida.
            var names = scales.Select(s => s.Name).ToList();
            if (!AvailableScaleNames.SequenceEqual(names))
            {
                AvailableScaleNames.Clear();
                foreach (var name in names)
                    AvailableScaleNames.Add(name);
            }

            if (!AvailableScaleNames.Contains(EntryScaleName))
                EntryScaleName = AvailableScaleNames.FirstOrDefault() ?? "";
            if (!AvailableScaleNames.Contains(ExitScaleName))
                ExitScaleName = AvailableScaleNames.Count > 1 ? AvailableScaleNames[1] : AvailableScaleNames.FirstOrDefault() ?? "";

            OnPropertyChanged(nameof(IsMultiScale));
        });
    }

    // ── Captured weights (solo lectura, se llenan al pesar) ──

    [ObservableProperty] private string _entryWeightText = "0 kg";
    [ObservableProperty] private string _exitWeightText = "0 kg";

    private bool _exitCaptured;

    // ── Scale selectors ────────────────────────────────────

    [ObservableProperty] private string _entryScaleName = "";
    [ObservableProperty] private string _exitScaleName = "";

    // ── Status display ─────────────────────────────────────

    [ObservableProperty] private string _weightDisplay = "0kg";
    [ObservableProperty] private string _connectionStatus = "Desconectado";
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _isStable;
    [ObservableProperty] private string _statusMessage = "Listo";
    [ObservableProperty] private bool _isWeighingIn;

    // Los pesos llegan desde el hilo del puerto serie, que puede seguir activo mientras la app se cierra:
    // si ya no hay aplicación o está cerrándose, se ignora el evento en vez de fallar con un puntero nulo.
    private static void RunOnUi(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted) return;

        if (dispatcher.CheckAccess())
            action();
        else
            dispatcher.Invoke(action);
    }

    // ── Scale events ──────────────────────────────────────

    private void WireScaleEvents()
    {
        foreach (var scale in _scaleManager.Scales)
        {
            var svc = scale.Service;
            svc.WeightUpdated += (_, w) => OnScaleWeightUpdated(scale);
            svc.StabilityChanged += (_, stable) => OnScaleStabilityChanged(scale, stable);
            svc.ConnectionChanged += (_, connected) => OnScaleConnectionChanged(scale, connected);
            svc.ErrorOccurred += (_, err) => OnScaleError(scale, err);
        }

        _scaleManager.ScaleAdded += (_, scale) =>
        {
            var svc = scale.Service;
            svc.WeightUpdated += (_, w) => OnScaleWeightUpdated(scale);
            svc.StabilityChanged += (_, stable) => OnScaleStabilityChanged(scale, stable);
            svc.ConnectionChanged += (_, connected) => OnScaleConnectionChanged(scale, connected);
            svc.ErrorOccurred += (_, err) => OnScaleError(scale, err);
        };
    }

    private void OnScaleWeightUpdated(ScaleInstance scale)
    {
        RunOnUi(() =>
        {
            var display = ScaleDisplays.FirstOrDefault(d => d.Id == scale.Config.Id);
            if (display != null)
            {
                display.Decimals = scale.WeightDecimals;
                display.Weight = scale.CurrentWeight;
            }

            WeightDisplay = WeightFormat.Format(scale.CurrentWeight, scale.WeightDecimals) + "kg";
            IsReading = true;
            LastReadingTime = DateTime.Now.ToString("HH:mm:ss.fff");
            IsConnected = _scaleManager.Scales.Any(s => s.IsConnected);
            ConnectionStatus = IsConnected ? "Conectado" : "Desconectado";

            if (!IsWeighingIn && scale.Name == EntryScaleName)
                EntryWeightText = WeightFormat.FormatKg(scale.CurrentWeight, scale.WeightDecimals);

            if (!_exitCaptured && scale.Name == ExitScaleName)
                ExitWeightText = WeightFormat.FormatKg(scale.CurrentWeight, scale.WeightDecimals);
        });
    }

    private void OnScaleStabilityChanged(ScaleInstance scale, bool stable)
    {
        RunOnUi(() =>
        {
            var display = ScaleDisplays.FirstOrDefault(d => d.Id == scale.Config.Id);
            if (display != null)
                display.IsStable = stable;
            IsStable = stable;
        });
    }

    private void OnScaleConnectionChanged(ScaleInstance scale, bool connected)
    {
        RunOnUi(() =>
        {
            var display = ScaleDisplays.FirstOrDefault(d => d.Id == scale.Config.Id);
            if (display != null)
            {
                display.IsConnected = connected;
                display.StatusText = connected ? "Conectado" : "Desconectado";
            }

            IsConnected = _scaleManager.Scales.Any(s => s.IsConnected);
            ConnectionStatus = IsConnected ? "Conectado" : "Desconectado";
            if (!connected)
                StatusMessage = $"Báscula {scale.Name} desconectada";
        });
    }

    private void OnScaleError(ScaleInstance scale, string error)
    {
        RunOnUi(() =>
        {
            StatusMessage = $"{scale.Name}: {error}";
        });
    }

    // ── Operator / Driver dropdowns ───────────────────────

    public ObservableCollection<Operator> AllOperators { get; } = [];
    public ICollectionView OperatorsView { get; }
    public ObservableCollection<Driver> AllDrivers { get; } = [];
    public ICollectionView DriversView { get; }

    [ObservableProperty] private bool _isOperatorDropdownOpen;
    [ObservableProperty] private bool _isDriverDropdownOpen;
    [ObservableProperty] private string _operatorName = "";
    [ObservableProperty] private string _driverName = "";

    partial void OnSelectedCompanyChanged(Company? value)
    {
        OperatorsView.Refresh();
        DriversView.Refresh();
    }

    partial void OnOperatorNameChanged(string value)
    {
        OperatorsView.Refresh();
        if (!string.IsNullOrWhiteSpace(value))
        {
            bool exacto = AllOperators.Any(o => o.FullName.Equals(value, StringComparison.OrdinalIgnoreCase));
            if (!exacto) IsOperatorDropdownOpen = true;
        }
    }

    partial void OnDriverNameChanged(string value)
    {
        DriversView.Refresh();
        if (!string.IsNullOrWhiteSpace(value))
        {
            bool exacto = AllDrivers.Any(d => d.FullName.Equals(value, StringComparison.OrdinalIgnoreCase));
            if (!exacto) IsDriverDropdownOpen = true;
        }
    }

    private bool FilterOperator(object obj)
    {
        if (obj is not Operator o) return false;
        if (SelectedCompany is not null && o.CompanyId != SelectedCompany.Id) return false;
        if (!string.IsNullOrEmpty(OperatorName) && !o.FullName.Contains(OperatorName, StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    private bool FilterDriver(object obj)
    {
        if (obj is not Driver d) return false;
        if (SelectedCompany is not null && d.CompanyId != SelectedCompany.Id) return false;
        if (!string.IsNullOrEmpty(DriverName) && !d.FullName.Contains(DriverName, StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    // ── Form fields ────────────────────────────────────────

    [ObservableProperty] private string _licensePlate = "";
    [ObservableProperty] private string _plateCountry = "CL";
    [ObservableProperty] private Product? _selectedProduct;
    [ObservableProperty] private Company? _selectedCompany;
    [ObservableProperty] private WeighingRecord? _selectedRecord;

    partial void OnSelectedRecordChanged(WeighingRecord? value)
    {
        if (value == null) return;

        LicensePlate = value.LicensePlate;
        PlateCountry = value.PlateCountry;
        OperatorName = value.OperatorName;
        DriverName = value.DriverName;
        SelectedProduct = Products.FirstOrDefault(p => p.Id == value.ProductId);
        SelectedCompany = Companies.FirstOrDefault(c => c.Id == value.CompanyId);
        _currentRecord = value.Status == WeighingStatus.WeighingIn ? value : null;
        IsWeighingIn = value.Status == WeighingStatus.WeighingIn;
        EntryWeightText = WeightFormat.FormatKg(value.GrossWeight, value.WeightDecimals);
        if (value.TareWeight > 0)
            ExitWeightText = WeightFormat.FormatKg(value.TareWeight, value.WeightDecimals);
        StatusMessage = $"Cargado: {value.TicketNumber} - {value.LicensePlate}";
    }

    public ObservableCollection<Product> Products { get; } = [];
    public ObservableCollection<Company> Companies { get; } = [];
    public ObservableCollection<WeighingRecord> ActiveWeighings { get; } = [];
    public ObservableCollection<string> Countries { get; } =
    [
        "CL", "AR", "BR", "PY", "UY", "BO", "PE", "CO", "MX", "EC"
    ];

    // ── Sidebar VMs (set externally by DashboardWindow) ────

    public CameraViewModel? CameraVM { get; set; }
    public ControlsViewModel? ControlsVM { get; set; }

    // ── Display helpers ────────────────────────────────────

    [ObservableProperty] private string _lastReadingTime = "";
    [ObservableProperty] private bool _isReading;
    [ObservableProperty] private string _rawData = "";

    // ── Connect / Disconnect ───────────────────────────────

    [RelayCommand]
    private async Task ConnectAsync()
    {
        StatusMessage = "Conectando...";
        try
        {
            await _scaleManager.ConnectAllAsync();

            var connected = _scaleManager.Scales.Count(s => s.IsConnected);
            var total = _scaleManager.Scales.Count;

            if (connected == 0)
                StatusMessage = "Error: no se pudo conectar. Verifique puerto COM, cable y configuración.";
            else if (connected < total)
                StatusMessage = $"{connected}/{total} básculas conectadas";
            else
                StatusMessage = "Conectado correctamente";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error de conexión: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        try
        {
            StatusMessage = "Desconectando...";
            await _scaleManager.DisconnectAllAsync();
            StatusMessage = "Desconectado correctamente";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error al desconectar: {ex.Message}";
        }
    }

    // ── Weigh-in ───────────────────────────────────────────

    [RelayCommand]
    private async Task StartWeighInAsync()
    {
        if (string.IsNullOrWhiteSpace(LicensePlate) || string.IsNullOrWhiteSpace(OperatorName))
        {
            StatusMessage = "Complete patente y operador";
            return;
        }

        var scale = _scaleManager.GetByName(EntryScaleName);
        if (scale == null || !scale.IsConnected)
        {
            StatusMessage = $"Báscula {EntryScaleName} no disponible";
            return;
        }

        var grossWeight = scale.CurrentWeight;

        _exitCaptured = false;
        var currentExitScale = _scaleManager.GetByName(ExitScaleName);
        if (currentExitScale != null)
            ExitWeightText = WeightFormat.FormatKg(currentExitScale.CurrentWeight, currentExitScale.WeightDecimals);

        if (grossWeight == 0)
        {
            StatusMessage = "Advertencia: peso de entrada en 0. Verifique la báscula.";
        }

        _currentRecord = new WeighingRecord
        {
            WeighInTime = DateTime.Now,
            LicensePlate = LicensePlate.Trim().ToUpper(),
            PlateCountry = PlateCountry,
            OperatorName = OperatorName.Trim(),
            DriverName = DriverName.Trim(),
            ProductId = SelectedProduct?.Id,
            CompanyId = SelectedCompany?.Id,
            Status = WeighingStatus.WeighingIn,
            GrossWeight = grossWeight,
            WeightDecimals = scale.WeightDecimals,
            EntryScaleName = EntryScaleName
        };

        _currentRecord = await _dataService.CreateAsync(_currentRecord);

        EntryWeightText = WeightFormat.FormatKg(grossWeight, scale.WeightDecimals);
        IsWeighingIn = true;
        StatusMessage = $"Pesada de entrada: {_currentRecord.TicketNumber}";
        await LoadActiveWeighingsAsync();
        WeakReferenceMessenger.Default.Send(new WeighingDataChangedMessage());
    }

    // ── Weigh-out ──────────────────────────────────────────

    [RelayCommand]
    private async Task CompleteWeighOutAsync()
    {
        try
        {
            WeighingRecord? record = null;

            if (SelectedRecord != null)
                record = SelectedRecord;
            else if (!string.IsNullOrWhiteSpace(LicensePlate))
                record = await _dataService.GetActiveByPlateAsync(LicensePlate.Trim().ToUpper());

            if (record == null)
            {
                StatusMessage = "Seleccione un registro activo de la lista";
                return;
            }

            var scale = _scaleManager.GetByName(ExitScaleName);
            if (scale == null || !scale.IsConnected)
            {
                StatusMessage = $"Báscula {ExitScaleName} no disponible";
                return;
            }

            var tareWeight = scale.CurrentWeight;

            if (Math.Abs(record.GrossWeight - tareWeight) < 0.001m)
            {
                StatusMessage = "Advertencia: peso de salida igual al de entrada. "
                    + "Verifique que el vehículo haya salido de la báscula.";
            }

            record.WeighOutTime = DateTime.Now;
            record.TareWeight = tareWeight;
            record.WeightDecimals = Math.Max(record.WeightDecimals, scale.WeightDecimals);
            record.ExitScaleName = ExitScaleName;
            record.IsExitWeightManual = false;
            record.NetWeight = Math.Abs(record.GrossWeight - record.TareWeight);
            record.Status = WeighingStatus.Completed;

            await _dataService.UpdateAsync(record);

            ExitWeightText = WeightFormat.FormatKg(tareWeight, scale.WeightDecimals);
            _exitCaptured = true;
            _lastCompletedRecord = record;

            NewWeighing();
            StatusMessage = $"Pesaje completado: {record.TicketNumber}";
            await LoadActiveWeighingsAsync();
            WeakReferenceMessenger.Default.Send(new WeighingDataChangedMessage());

            _ = CaptureAndSavePhotoWithTimeoutAsync(record, "exit");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error al finalizar: {ex.Message}";
        }
    }

    // ── Cancel ─────────────────────────────────────────────

    [RelayCommand]
    private async Task CancelWeighingAsync()
    {
        if (_currentRecord != null)
        {
            _currentRecord.Status = WeighingStatus.Cancelled;
            await _dataService.UpdateAsync(_currentRecord);
            _currentRecord = null;
            IsWeighingIn = false;
            _exitCaptured = false;
            StatusMessage = "Pesaje cancelado";
            await LoadActiveWeighingsAsync();
        }
    }

    // ── Print ──────────────────────────────────────────────

    [RelayCommand]
    private async Task PrintPreviewAsync()
    {
        WeighingRecord record;

        if (_currentRecord != null)
        {
            var full = await _dataService.GetByIdAsync(_currentRecord.Id);
            if (full == null) return;
            record = full;
        }
        else if (SelectedRecord != null)
        {
            var full = await _dataService.GetByIdAsync(SelectedRecord.Id);
            if (full == null) return;
            record = full;
        }
        else if (_lastCompletedRecord != null)
        {
            var full = await _dataService.GetByIdAsync(_lastCompletedRecord.Id);
            if (full == null) return;
            record = full;
        }
        else
        {
            record = new WeighingRecord
            {
                TicketNumber = "--- VISTA PREVIA ---",
                WeighInTime = DateTime.Now,
                LicensePlate = string.IsNullOrWhiteSpace(LicensePlate) ? "---" : LicensePlate.Trim().ToUpper(),
                PlateCountry = string.IsNullOrWhiteSpace(PlateCountry) ? "CL" : PlateCountry,
                OperatorName = string.IsNullOrWhiteSpace(OperatorName) ? "---" : OperatorName.Trim(),
                DriverName = string.IsNullOrWhiteSpace(DriverName) ? "---" : DriverName.Trim(),
                Product = SelectedProduct,
                Company = SelectedCompany,
                WeightDecimals = _currentRecord?.WeightDecimals ?? 0,
                GrossWeight = _currentRecord?.GrossWeight ?? 0,
                TareWeight = _currentRecord?.TareWeight ?? 0,
                NetWeight = Math.Abs((_currentRecord?.GrossWeight ?? 0) - (_currentRecord?.TareWeight ?? 0)),
                Status = WeighingStatus.Completed
            };
        }

        var header = await LoadReceivingCompanyAsync();
        var template = new TicketTemplate { Header = header };

        var dlg = new PrintDialog();
        if (dlg.ShowDialog() != true) return;

        var printer = new WindowsPrinter(dlg.PrintQueue.Name);
        var success = await printer.PrintAsync(record, template);
        StatusMessage = success ? "Ticket enviado a impresora" : "Error al imprimir";
    }

    // ── Helpers ────────────────────────────────────────────

    [RelayCommand]
    private void NewWeighing()
    {
        LicensePlate = "";
        PlateCountry = "CL";
        OperatorName = "";
        DriverName = "";
        SelectedProduct = null;
        SelectedCompany = null;
        _currentRecord = null;
        IsWeighingIn = false;
        SelectedRecord = null;
        _exitCaptured = false;

        var entryScale = _scaleManager.GetByName(EntryScaleName);
        var exitScale = _scaleManager.GetByName(ExitScaleName);

        EntryWeightText = entryScale != null ? WeightFormat.FormatKg(entryScale.CurrentWeight, entryScale.WeightDecimals) : "0 kg";
        ExitWeightText = exitScale != null ? WeightFormat.FormatKg(exitScale.CurrentWeight, exitScale.WeightDecimals) : "0 kg";

        StatusMessage = "Listo";
    }

    public void SetCameraService(ICameraService cameraService)
    {
        _cameraService = cameraService;
    }

    private async Task CaptureAndSavePhotoWithTimeoutAsync(WeighingRecord record, string suffix)
    {
        try
        {
            if (_cameraService == null || !_cameraService.IsPreviewRunning) return;

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var captureTask = Task.Run(() => _cameraService.CaptureStillAsync(), cts.Token);

            var jpeg = await captureTask;
            if (jpeg == null || jpeg.Length == 0) return;

            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PesaControl", "Photos");
            Directory.CreateDirectory(dir);

            var fileName = $"{record.TicketNumber}-{suffix}.jpg";
            var filePath = Path.Combine(dir, fileName);
            await File.WriteAllBytesAsync(filePath, jpeg);

            if (suffix == "exit")
                record.PhotoPathExit = filePath;
            else
                record.PhotoPath = filePath;

            await _dataService.UpdateAsync(record);
        }
        catch (OperationCanceledException) { }
        catch { }
    }

    private async Task<string> LoadReceivingCompanyAsync()
    {
        if (_dbService == null) return "PesaControl";
        using var ctx = _dbService.CreateContext();
        var repo = new Repository<SystemConfig>(ctx);
        var configs = await repo.GetAllAsync();
        return configs.FirstOrDefault(c => c.Key == "receiving_company")?.Value ?? "PesaControl";
    }

    [RelayCommand]
    private async Task RefreshCatalogsAsync()
    {
        await LoadCatalogsAsync();
        await LoadOperatorsAsync();
        await LoadDriversAsync();
    }

    private async Task LoadCatalogsAsync()
    {
        var products = await _dataService.GetProductsAsync();
        RunOnUi(() =>
        {
            Products.Clear();
            foreach (var p in products) Products.Add(p);
        });

        var companies = await _dataService.GetCompaniesAsync();
        RunOnUi(() =>
        {
            Companies.Clear();
            foreach (var c in companies) Companies.Add(c);
        });
    }

    private async Task LoadOperatorsAsync()
    {
        var items = await _dataService.GetOperatorsAsync();
        RunOnUi(() =>
        {
            AllOperators.Clear();
            foreach (var o in items) AllOperators.Add(o);
            OperatorsView.Refresh();
        });
    }

    private async Task LoadDriversAsync()
    {
        var items = await _dataService.GetDriversAsync();
        RunOnUi(() =>
        {
            AllDrivers.Clear();
            foreach (var d in items) AllDrivers.Add(d);
            DriversView.Refresh();
        });
    }

    private async Task LoadActiveWeighingsAsync()
    {
        var records = await _dataService.GetTodayAsync();
        RunOnUi(() =>
        {
            ActiveWeighings.Clear();
            foreach (var w in records) ActiveWeighings.Add(w);
        });
    }
}
