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

    [ObservableProperty] private BackupViewModel? _backupVM;
    [ObservableProperty] private int _selectedTabIndex;

    public SettingsViewModel(DatabaseService db, ExportImportService? exportImport = null)
    {
        _db = db;
        if (exportImport != null)
        {
            BackupVM = new BackupViewModel(exportImport);
        }
        else
        {
            BackupVM = new BackupViewModel(new ExportImportService(db));
        }
    }

    // Protocol selection
    [ObservableProperty] private string _selectedProtocolName = "Serial";
    public string[] ProtocolNames => ["Serial", "Modbus TCP", "Modbus RTU", "Sensoc", "Auto-Detect"];

    // Serial params
    [ObservableProperty] private string _serialPort = "COM1";
    [ObservableProperty] private int _serialBaudRate = 9600;
    [ObservableProperty] private int _serialParity;
    [ObservableProperty] private int _serialDataBits = 8;
    [ObservableProperty] private int _serialStopBits = 1;

    // TCP params
    [ObservableProperty] private string _ipAddress = "192.168.1.100";
    [ObservableProperty] private int _tcpPort = 502;

    // Modbus params
    [ObservableProperty] private int _modbusSlaveId = 1;
    [ObservableProperty] private int _modbusRegister;
    [ObservableProperty] private int _readIntervalMs = 200;

    [ObservableProperty] private string _statusMessage = "";

    public int[] BaudRates { get; } = [300, 600, 1200, 2400, 4800, 9600, 14400, 19200, 38400, 57600, 115200];
    public int[] ParityOptions { get; } = [0, 1, 2];
    public int[] DataBitsOptions { get; } = [5, 6, 7, 8];
    public int[] StopBitsOptions { get; } = [0, 1, 2];

    [RelayCommand]
    private async Task LoadConfigAsync()
    {
        var ctx = _db.CreateContext();
        var repo = new Repository<SystemConfig>(ctx);
        var configs = await repo.GetAllAsync();
        var dict = configs.ToDictionary(c => c.Key, c => c.Value);

        App.Current.Dispatcher.Invoke(() =>
        {
            SelectedProtocolName = dict.GetValueOrDefault("weighing_protocol") switch
            {
                "modbus_tcp" => "Modbus TCP",
                "modbus_rtu" => "Modbus RTU",
                "sensoc" => "Sensoc",
                "auto_detect" => "Auto-Detect",
                _ => "Serial"
            };

            SerialPort = dict.GetValueOrDefault("serial_port", "COM1");
            SerialBaudRate = int.TryParse(dict.GetValueOrDefault("serial_baudrate"), out var br) ? br : 9600;
            SerialParity = int.TryParse(dict.GetValueOrDefault("serial_parity"), out var p) ? p : 0;
            SerialDataBits = int.TryParse(dict.GetValueOrDefault("serial_databits"), out var db) ? db : 8;
            SerialStopBits = int.TryParse(dict.GetValueOrDefault("serial_stopbits"), out var sb) ? sb : 1;
            IpAddress = dict.GetValueOrDefault("ip_address", "192.168.1.100");
            TcpPort = int.TryParse(dict.GetValueOrDefault("tcp_port"), out var tp) ? tp : 502;
            ModbusSlaveId = int.TryParse(dict.GetValueOrDefault("modbus_slaveid"), out var si) ? si : 1;
            ModbusRegister = int.TryParse(dict.GetValueOrDefault("modbus_register"), out var ra) ? ra : 0;
            ReadIntervalMs = int.TryParse(dict.GetValueOrDefault("read_interval_ms"), out var ri) ? ri : 200;

            StatusMessage = "Configuración cargada";
        });
    }

    [RelayCommand]
    private async Task SaveConfigAsync()
    {
        try
        {
            var ctx = _db.CreateContext();
            var repo = new Repository<SystemConfig>(ctx);

            await UpsertAsync(repo, "weighing_protocol", SelectedProtocolName switch
            {
                "Modbus TCP" => "modbus_tcp",
                "Modbus RTU" => "modbus_rtu",
                "Sensoc" => "sensoc",
                "Auto-Detect" => "auto_detect",
                _ => "serial"
            });

            await UpsertAsync(repo, "serial_port", SerialPort);
            await UpsertAsync(repo, "serial_baudrate", SerialBaudRate.ToString());
            await UpsertAsync(repo, "serial_parity", SerialParity.ToString());
            await UpsertAsync(repo, "serial_databits", SerialDataBits.ToString());
            await UpsertAsync(repo, "serial_stopbits", SerialStopBits.ToString());
            await UpsertAsync(repo, "ip_address", IpAddress);
            await UpsertAsync(repo, "tcp_port", TcpPort.ToString());
            await UpsertAsync(repo, "modbus_slaveid", ModbusSlaveId.ToString());
            await UpsertAsync(repo, "modbus_register", ModbusRegister.ToString());
            await UpsertAsync(repo, "read_interval_ms", ReadIntervalMs.ToString());

            StatusMessage = "Configuración guardada correctamente";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error al guardar: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        StatusMessage = "Probando conexión...";

        try
        {
            var config = new CommunicationConfig
            {
                Type = SelectedProtocolName switch
                {
                    "Modbus TCP" => ProtocolType.ModbusTcp,
                    "Modbus RTU" => ProtocolType.ModbusRtu,
                    "Sensoc" => ProtocolType.Sensoc,
                    _ => ProtocolType.Serial
                },
                PortName = SerialPort,
                BaudRate = SerialBaudRate,
                Parity = SerialParity,
                DataBits = SerialDataBits,
                StopBits = SerialStopBits,
                IpAddress = IpAddress,
                TcpPort = TcpPort,
                SlaveId = (byte)ModbusSlaveId,
                RegisterAddress = (ushort)ModbusRegister,
                ReadIntervalMs = ReadIntervalMs
            };

            IWeighingProtocol protocol = SelectedProtocolName switch
            {
                "Modbus TCP" => new ModbusTcpProtocol(),
                "Modbus RTU" => new ModbusRtuProtocol(),
                "Sensoc" => new SensocProtocol(),
                "Auto-Detect" => new AutoDetectProtocol(),
                _ => new SerialProtocol()
            };

            var service = new WeighingService(protocol, config);
            await service.ConnectAsync();
            await service.DisconnectAsync();

            StatusMessage = "Conexión exitosa";
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
