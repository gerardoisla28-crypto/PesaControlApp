using System.Collections.ObjectModel;
using System.IO.Ports;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PesaControl.Core.Models;
using PesaControl.Core.Services;
using PesaControl.Data.Data;
using PesaControl.Data.Repositories;
using PesaControl.Data.Services;

namespace PesaControl.UI.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly DatabaseService _db;
    private readonly ScaleManager? _scaleManager;
    private readonly WeighingViewModel? _weighingVm;

    [ObservableProperty] private BackupViewModel? _backupVM;
    [ObservableProperty] private int _selectedTabIndex;

    public SettingsViewModel(DatabaseService db, ScaleManager? scaleManager = null, WeighingViewModel? weighingVm = null, ExportImportService? exportImport = null)
    {
        _db = db;
        _scaleManager = scaleManager;
        _weighingVm = weighingVm;
        if (exportImport != null)
        {
            BackupVM = new BackupViewModel(exportImport);
        }
        else
        {
            BackupVM = new BackupViewModel(new ExportImportService(db));
        }
    }

    // ── Básculas ──────────────────────────────────────────

    public ObservableCollection<ScaleConfig> ScaleConfigs { get; } = [];

    [ObservableProperty] private ScaleConfig? _selectedScale;

    [ObservableProperty] private string _scaleStatusMessage = "";

    // Editing scale (popup fields)
    [ObservableProperty] private bool _isScaleEditorOpen;
    [ObservableProperty] private ScaleConfig? _editingScale;

    [RelayCommand]
    private async Task LoadScalesAsync()
    {
        var ctx = _db.CreateContext();
        var repo = new Repository<ScaleConfig>(ctx);
        var items = await repo.GetAllAsync();

        App.Current.Dispatcher.Invoke(() =>
        {
            ScaleConfigs.Clear();
            foreach (var s in items.OrderBy(x => x.SortOrder))
                ScaleConfigs.Add(s);
        });
    }

    [RelayCommand]
    private void AddScale()
    {
        EditingScale = new ScaleConfig
        {
            Name = $"Báscula {ScaleConfigs.Count + 1}",
            IsActive = true,
            SortOrder = ScaleConfigs.Count
        };
        SelectedProtocolName = "Serial";
        IsScaleEditorOpen = true;
    }

    [RelayCommand]
    private void EditScale()
    {
        if (SelectedScale is null) return;
        EditingScale = new ScaleConfig
        {
            Id = SelectedScale.Id,
            Name = SelectedScale.Name,
            IsActive = SelectedScale.IsActive,
            SortOrder = SelectedScale.SortOrder,
            ProtocolType = SelectedScale.ProtocolType,
            PortName = SelectedScale.PortName,
            BaudRate = SelectedScale.BaudRate,
            Parity = SelectedScale.Parity,
            DataBits = SelectedScale.DataBits,
            StopBits = SelectedScale.StopBits,
            IpAddress = SelectedScale.IpAddress,
            TcpPort = SelectedScale.TcpPort,
            SlaveId = SelectedScale.SlaveId,
            RegisterAddress = SelectedScale.RegisterAddress,
            ReadIntervalMs = SelectedScale.ReadIntervalMs
        };
        SelectedProtocolName = SelectedScale.ProtocolType switch
        {
            ProtocolType.ModbusTcp => "Modbus TCP",
            ProtocolType.ModbusRtu => "Modbus RTU",
            ProtocolType.Sensoc => "Sensoc",
            _ => "Serial"
        };
        IsScaleEditorOpen = true;
    }

    [RelayCommand]
    private async Task SaveScaleAsync()
    {
        if (EditingScale is null) return;

        var saved = EditingScale;
        var ctx = _db.CreateContext();
        var repo = new Repository<ScaleConfig>(ctx);

        if (saved.Id == 0)
        {
            await repo.AddAsync(saved);
        }
        else
        {
            await repo.UpdateAsync(saved);
        }

        IsScaleEditorOpen = false;
        EditingScale = null;
        await LoadScalesAsync();

        if (_scaleManager != null)
        {
            if (saved.Id == 0)
                await _scaleManager.AddScaleAsync(saved);
            else
                await _scaleManager.UpdateScaleConfigAsync(saved);
        }

        _weighingVm?.RefreshScaleNames();
        ScaleStatusMessage = "Báscula guardada";
    }

    [RelayCommand]
    private void CancelScaleEditor()
    {
        IsScaleEditorOpen = false;
        EditingScale = null;
    }

    [RelayCommand]
    private async Task DeleteScaleAsync()
    {
        if (SelectedScale is null) return;
        var ctx = _db.CreateContext();
        var repo = new Repository<ScaleConfig>(ctx);
        await repo.DeleteAsync(SelectedScale);
        await LoadScalesAsync();
        ScaleStatusMessage = "Báscula eliminada";
    }

    // ── Mode de operación ─────────────────────────────────

    [ObservableProperty] private string _selectedModeName = "Local";
    public string[] ModeNames => ["Local", "Servidor", "Cliente"];

    [ObservableProperty] private string _serverPort = "5000";
    [ObservableProperty] private string _serverUrl = "http://192.168.1.100:5000";

    // ── Protocol params (legacy - for editing single scale) ──
    [ObservableProperty] private string _selectedProtocolName = "Serial";
    public string[] ProtocolNames => ["Serial", "Modbus TCP", "Modbus RTU", "Sensoc"];

    [ObservableProperty] private int _serialBaudRate = 9600;
    [ObservableProperty] private int _serialParity;
    [ObservableProperty] private int _serialDataBits = 8;
    [ObservableProperty] private int _serialStopBits = 1;

    [ObservableProperty] private int _tcpPort = 502;

    [ObservableProperty] private int _modbusSlaveId = 1;
    [ObservableProperty] private int _modbusRegister;
    [ObservableProperty] private int _readIntervalMs = 200;

    public int[] BaudRates { get; } = [300, 600, 1200, 2400, 4800, 9600, 14400, 19200, 38400, 57600, 115200];
    public int[] ParityOptions { get; } = [0, 1, 2];
    public int[] DataBitsOptions { get; } = [5, 6, 7, 8];
    public int[] StopBitsOptions { get; } = [0, 1, 2];

    [ObservableProperty] private string _statusMessage = "";

    [RelayCommand]
    private async Task LoadConfigAsync()
    {
        var ctx = _db.CreateContext();
        var repo = new Repository<SystemConfig>(ctx);
        var configs = await repo.GetAllAsync();
        var dict = configs.ToDictionary(c => c.Key, c => c.Value);

        App.Current.Dispatcher.Invoke(() =>
        {
            SelectedModeName = dict.GetValueOrDefault("operation_mode") switch
            {
                "server" => "Servidor",
                "client" => "Cliente",
                _ => "Local"
            };

            ServerPort = dict.GetValueOrDefault("server_port", "5000");
            ServerUrl = dict.GetValueOrDefault("server_url", "http://192.168.1.100:5000");

            StatusMessage = "Configuración cargada";
        });

        await LoadScalesAsync();
    }

    [RelayCommand]
    private async Task SaveConfigAsync()
    {
        try
        {
            var ctx = _db.CreateContext();
            var repo = new Repository<SystemConfig>(ctx);

            await UpsertAsync(repo, "operation_mode", SelectedModeName switch
            {
                "Servidor" => "server",
                "Cliente" => "client",
                _ => "local"
            });

            await UpsertAsync(repo, "server_port", ServerPort);
            await UpsertAsync(repo, "server_url", ServerUrl);

            StatusMessage = "Configuración guardada correctamente";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error al guardar: {ex.Message}";
        }
    }

    public bool IsSerialProtocol => SelectedProtocolName is "Serial" or "Modbus RTU" or "Sensoc";
    public bool IsTcpProtocol => SelectedProtocolName == "Modbus TCP";

    partial void OnSelectedProtocolNameChanged(string value)
    {
        OnPropertyChanged(nameof(IsSerialProtocol));
        OnPropertyChanged(nameof(IsTcpProtocol));
        if (EditingScale is not null)
            EditingScale.ProtocolType = value switch
            {
                "Modbus TCP" => ProtocolType.ModbusTcp,
                "Modbus RTU" => ProtocolType.ModbusRtu,
                "Sensoc" => ProtocolType.Sensoc,
                _ => ProtocolType.Serial
            };
    }

    public string[] AvailablePorts => SerialPort.GetPortNames();

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        StatusMessage = "Probando conexión...";

        try
        {
            var protocolName = SelectedProtocolName;

            var config = new CommunicationConfig
            {
                Type = protocolName switch
                {
                    "Modbus TCP" => ProtocolType.ModbusTcp,
                    "Modbus RTU" => ProtocolType.ModbusRtu,
                    "Sensoc" => ProtocolType.Sensoc,
                    _ => ProtocolType.Serial
                },
                PortName = EditingScale?.PortName ?? "COM1",
                BaudRate = EditingScale?.BaudRate ?? SerialBaudRate,
                Parity = EditingScale?.Parity ?? SerialParity,
                DataBits = EditingScale?.DataBits ?? SerialDataBits,
                StopBits = EditingScale?.StopBits ?? SerialStopBits,
                IpAddress = EditingScale?.IpAddress ?? "192.168.1.100",
                TcpPort = TcpPort,
                SlaveId = (byte)ModbusSlaveId,
                RegisterAddress = (ushort)ModbusRegister,
                ReadIntervalMs = ReadIntervalMs
            };

            IWeighingProtocol protocol = protocolName switch
            {
                "Modbus TCP" => new ModbusTcpProtocol(),
                "Modbus RTU" => new ModbusRtuProtocol(),
                "Sensoc" => new SensocProtocol(),
                _ => new SerialProtocol()
            };

            var service = new WeighingService(protocol, config);
            await service.ConnectAsync();

            if (protocolName == "Sensoc")
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                WeightReading? reading = null;
                void handler(object? s, WeightReading r) { reading = r; }
                protocol.WeightChanged += handler;
                try
                {
                    await Task.Delay(3000, cts.Token);
                }
                catch (TaskCanceledException) { }
                protocol.WeightChanged -= handler;

                if (reading?.Value != 0)
                    StatusMessage = $"Conexión exitosa — peso: {reading.Value} kg";
                else
                    StatusMessage = "Conectado, esperando datos de la báscula...";
            }
            else
            {
                await Task.Delay(500);
                await service.RequestWeightAsync();
                var testReading = service.LastReading;

                if (testReading?.Value != 0)
                    StatusMessage = $"Conexión exitosa — peso: {testReading.Value} kg";
                else
                    StatusMessage = "Conexión exitosa";
            }

            await service.DisconnectAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error de conexión: {ex.Message}";
        }
    }

    private static async Task UpsertAsync(IRepository<SystemConfig> repo, string key, string value)
    {
        var existing = await repo.FindAsync(c => c.Key == key);
        var entry = existing.FirstOrDefault();

        if (entry is not null)
        {
            entry.Value = value;
            await repo.UpdateAsync(entry);
        }
        else
        {
            await repo.AddAsync(new SystemConfig { Key = key, Value = value });
        }
    }
}
