using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System.IO;
using PesaControl.Core.Models;
using PesaControl.Core.Services;
using PesaControl.Data.Data;
using PesaControl.Data.Repositories;
using PesaControl.UI.Services;

namespace PesaControl.UI.ViewModels;

public partial class WeighingViewModel : ObservableObject
{
    private readonly WeighingService _weighingService;
    private readonly IWeighingRepository _weighingRepo;
    private readonly DatabaseService _dbService;
    private readonly IRepository<Product> _productRepo;
    private readonly IRepository<Company> _companyRepo;
    private readonly IRepository<Operator> _operatorRepo;
    private readonly IRepository<Driver> _driverRepo;
    private ICameraService? _cameraService;

    private WeighingRecord? _currentRecord;
    private WeighingRecord? _lastCompletedRecord;
    private CommunicationConfig _currentConfig = new();

    public WeighingViewModel(WeighingService weighingService, DatabaseService dbService, ICameraService? cameraService = null)
    {
        _weighingService = weighingService;
        _dbService = dbService;
        var ctx = _dbService.CreateContext();
        _weighingRepo = new WeighingRepository(ctx);
        _productRepo = new Repository<Product>(ctx);
        _companyRepo = new Repository<Company>(ctx);
        _operatorRepo = new Repository<Operator>(ctx);
        _driverRepo = new Repository<Driver>(ctx);
        _cameraService = cameraService;

        OperatorsView = CollectionViewSource.GetDefaultView(AllOperators);
        OperatorsView.Filter = FilterOperator;
        DriversView = CollectionViewSource.GetDefaultView(AllDrivers);
        DriversView.Filter = FilterDriver;

        WireEvents();
        _ = LoadCatalogsAsync();
        _ = LoadActiveWeighingsAsync();
        _ = LoadOperatorsAsync();
        _ = LoadDriversAsync();
    }

    public ObservableCollection<Operator> AllOperators { get; } = [];
    public ICollectionView OperatorsView { get; }
    public ObservableCollection<Driver> AllDrivers { get; } = [];
    public ICollectionView DriversView { get; }

    [ObservableProperty] private bool _isOperatorDropdownOpen;
    [ObservableProperty] private bool _isDriverDropdownOpen;

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

    private async Task LoadOperatorsAsync()
    {
        var items = await _operatorRepo.GetAllAsync();
        App.Current.Dispatcher.Invoke(() =>
        {
            AllOperators.Clear();
            foreach (var o in items) AllOperators.Add(o);
            OperatorsView.Refresh();
        });
    }

    private async Task LoadDriversAsync()
    {
        var items = await _driverRepo.GetAllAsync();
        App.Current.Dispatcher.Invoke(() =>
        {
            AllDrivers.Clear();
            foreach (var d in items) AllDrivers.Add(d);
            DriversView.Refresh();
        });
    }

    [ObservableProperty] private string _weightDisplay = "0.0kg";
    [ObservableProperty] private bool _isStable;
    [ObservableProperty] private string _connectionStatus = "Desconectado";
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private string _statusMessage = "Listo";
    [ObservableProperty] private string _lastReadingTime = "";
    [ObservableProperty] private bool _isReading;
    [ObservableProperty] private string _rawData = "";
    [ObservableProperty] private string _licensePlate = "";
    [ObservableProperty] private string _operatorName = "";
    [ObservableProperty] private string _driverName = "";
    [ObservableProperty] private string _plateCountry = "CL";
    [ObservableProperty] private Product? _selectedProduct;
    [ObservableProperty] private Company? _selectedCompany;
    [ObservableProperty] private WeighingRecord? _selectedRecord;
    [ObservableProperty] private bool _isWeighingIn;

    public ObservableCollection<Product> Products { get; } = new();
    public ObservableCollection<Company> Companies { get; } = new();
    public ObservableCollection<WeighingRecord> ActiveWeighings { get; } = new();
    public ObservableCollection<string> Countries { get; } = new()
    {
        "CL", "AR", "BR", "PY", "UY", "BO", "PE", "CO", "MX", "EC"
    };

    [RelayCommand]
    private async Task ConnectAsync()
    {
        StatusMessage = "Conectando...";
        try
        {
            var ctx = _dbService.CreateContext();
            _currentConfig = await LoadCommunicationConfigAsync(new Repository<SystemConfig>(ctx));

            IWeighingProtocol protocol = _currentConfig.Type switch
            {
                ProtocolType.ModbusTcp => new ModbusTcpProtocol(),
                ProtocolType.ModbusRtu => new ModbusRtuProtocol(),
                ProtocolType.Sensoc => new SensocProtocol(),
                _ => new SerialProtocol()
            };

            await _weighingService.SetProtocol(protocol, _currentConfig);
            await _weighingService.ConnectAsync();
            StatusMessage = "Conectado correctamente";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error de conexión: {ex.Message}";
        }
    }

    private static async Task<CommunicationConfig> LoadCommunicationConfigAsync(IRepository<SystemConfig> repo)
    {
        var configs = await repo.GetAllAsync();
        var dict = configs.ToDictionary(c => c.Key, c => c.Value);

        return new CommunicationConfig
        {
            Type = dict.GetValueOrDefault("weighing_protocol") switch
            {
                "modbus_tcp" => ProtocolType.ModbusTcp,
                "modbus_rtu" => ProtocolType.ModbusRtu,
                "sensoc" => ProtocolType.Sensoc,
                _ => ProtocolType.Serial
            },
            PortName = dict.GetValueOrDefault("serial_port", "COM1"),
            BaudRate = int.TryParse(dict.GetValueOrDefault("serial_baudrate"), out var br) ? br : 9600,
            Parity = int.TryParse(dict.GetValueOrDefault("serial_parity"), out var p) ? p : 0,
            DataBits = int.TryParse(dict.GetValueOrDefault("serial_databits"), out var db) ? db : 8,
            StopBits = int.TryParse(dict.GetValueOrDefault("serial_stopbits"), out var sb) ? sb : 1,
            IpAddress = dict.GetValueOrDefault("ip_address", "192.168.1.100"),
            TcpPort = int.TryParse(dict.GetValueOrDefault("tcp_port"), out var tp) ? tp : 502,
            SlaveId = byte.TryParse(dict.GetValueOrDefault("modbus_slaveid"), out var si) ? si : (byte)1,
            RegisterAddress = ushort.TryParse(dict.GetValueOrDefault("modbus_register"), out var ra) ? ra : (ushort)0,
            ReadIntervalMs = int.TryParse(dict.GetValueOrDefault("read_interval_ms"), out var ri) ? ri : 200
        };
    }

    private async Task<string> LoadReceivingCompanyAsync()
    {
        var ctx = _dbService.CreateContext();
        var configs = await new Repository<SystemConfig>(ctx).GetAllAsync();
        return configs.FirstOrDefault(c => c.Key == "receiving_company")?.Value ?? "PesaControl";
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        try
        {
            StatusMessage = "Desconectando...";
            await _weighingService.DisconnectAsync();
            StatusMessage = "Desconectado correctamente";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error al desconectar: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task TareAsync()
    {
        await _weighingService.TareAsync();
    }

    [RelayCommand]
    private async Task StartWeighInAsync()
    {
        if (string.IsNullOrWhiteSpace(LicensePlate) || string.IsNullOrWhiteSpace(OperatorName))
        {
            StatusMessage = "Complete patente y operador";
            return;
        }

        _currentRecord = new WeighingRecord
        {
            TicketNumber = await _weighingRepo.GetNextTicketNumberAsync(),
            WeighInTime = DateTime.Now,
            LicensePlate = LicensePlate.Trim().ToUpper(),
            PlateCountry = PlateCountry,
            OperatorName = OperatorName.Trim(),
            DriverName = DriverName.Trim(),
            ProductId = SelectedProduct?.Id,
            CompanyId = SelectedCompany?.Id,
            Status = WeighingStatus.WeighingIn,
            GrossWeight = _weighingService.CurrentWeight
        };

        await _weighingRepo.AddAsync(_currentRecord);
        IsWeighingIn = true;
        StatusMessage = $"Pesada de entrada: {_currentRecord.TicketNumber}";
        await LoadActiveWeighingsAsync();
        WeakReferenceMessenger.Default.Send(new WeighingDataChangedMessage());
    }

    [RelayCommand]
    private async Task CompleteWeighOutAsync()
    {
        try
        {
            WeighingRecord? record = null;

            if (SelectedRecord != null)
                record = SelectedRecord;
            else if (!string.IsNullOrWhiteSpace(LicensePlate))
                record = await _weighingRepo.GetActiveWeighingByPlateAsync(LicensePlate.Trim().ToUpper());

            if (record == null)
            {
                StatusMessage = "Seleccione un registro activo de la lista";
                return;
            }

            record.WeighOutTime = DateTime.Now;
            record.TareWeight = _weighingService.CurrentWeight;

            if (Math.Abs(record.GrossWeight - record.TareWeight) < 0.001m)
            {
                StatusMessage = "Advertencia: peso de salida igual al de entrada. "
                    + "Verifique que el vehículo haya salido de la báscula.";
            }

            record.NetWeight = Math.Abs(record.GrossWeight - record.TareWeight);
            record.Status = WeighingStatus.Completed;

            await _weighingRepo.UpdateAsync(record);

            _lastCompletedRecord = record;

            if (record == _currentRecord)
                _currentRecord = null;

            IsWeighingIn = false;
            StatusMessage = $"Pesaje completado: {record.TicketNumber}";
            ClearForm();
            await LoadActiveWeighingsAsync();
            WeakReferenceMessenger.Default.Send(new WeighingDataChangedMessage());

            _ = CaptureAndSavePhotoWithTimeoutAsync(record, "exit");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error al finalizar: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task CancelWeighingAsync()
    {
        if (_currentRecord != null)
        {
            _currentRecord.Status = WeighingStatus.Cancelled;
            await _weighingRepo.UpdateAsync(_currentRecord);
            _currentRecord = null;
            IsWeighingIn = false;
            StatusMessage = "Pesaje cancelado";
            await LoadActiveWeighingsAsync();
        }
    }

    [RelayCommand]
    private async Task PrintPreviewAsync()
    {
        WeighingRecord record;

        if (_currentRecord != null)
        {
            var ctx = _dbService.CreateContext();
            var full = await new WeighingRepository(ctx).GetByIdAsync(_currentRecord.Id);
            if (full == null) return;
            record = full;
        }
        else if (SelectedRecord != null)
        {
            var ctx = _dbService.CreateContext();
            var full = await new WeighingRepository(ctx).GetByIdAsync(SelectedRecord.Id);
            if (full == null) return;
            record = full;
        }
        else if (_lastCompletedRecord != null)
        {
            var ctx = _dbService.CreateContext();
            var full = await new WeighingRepository(ctx).GetByIdAsync(_lastCompletedRecord.Id);
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
                GrossWeight = 0,
                TareWeight = 0,
                NetWeight = 0,
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

    private void ClearForm()
    {
        LicensePlate = "";
        OperatorName = "";
        DriverName = "";
        SelectedProduct = null;
        SelectedCompany = null;
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

            await _weighingRepo.UpdateAsync(record);
        }
        catch (OperationCanceledException)
        {
            // Timeout — ignorar
        }
        catch
        {
            // Error de captura — ignorar
        }
    }

    private void WireEvents()
    {
        _weighingService.WeightUpdated += (s, w) =>
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                WeightDisplay = $"{w:F1}kg";
                IsReading = true;
                LastReadingTime = DateTime.Now.ToString("HH:mm:ss.fff");
                StatusMessage = $"Peso: {w:F1}kg";
                RawData = _weighingService.LastReading?.RawData ?? "";
            });
        };

        _weighingService.StabilityChanged += (s, stable) =>
        {
            App.Current.Dispatcher.Invoke(() => IsStable = stable);
        };

        _weighingService.ConnectionChanged += (s, connected) =>
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                IsConnected = connected;
                ConnectionStatus = connected ? "Conectado" : "Desconectado";
                if (!connected)
                    StatusMessage = "Desconectado de la báscula";
            });
        };

        _weighingService.ErrorOccurred += (s, err) =>
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                StatusMessage = $"⚠ {err}";
            });
        };
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
        var products = await _productRepo.GetAllAsync();
        App.Current.Dispatcher.Invoke(() =>
        {
            Products.Clear();
            foreach (var p in products) Products.Add(p);
        });

        var companies = await _companyRepo.GetAllAsync();
        App.Current.Dispatcher.Invoke(() =>
        {
            Companies.Clear();
            foreach (var c in companies) Companies.Add(c);
        });
    }

    private async Task LoadActiveWeighingsAsync()
    {
        var records = await _weighingRepo.GetTodayWeighingsAsync();
        App.Current.Dispatcher.Invoke(() =>
        {
            ActiveWeighings.Clear();
            foreach (var w in records) ActiveWeighings.Add(w);
        });
    }
}
