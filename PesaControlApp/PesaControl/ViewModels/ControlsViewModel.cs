using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO.Ports;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PesaControl.Core.Models;
using PesaControl.Data.Data;
using PesaControl.Data.Repositories;

namespace PesaControl.UI.ViewModels;

public class ArduinoDeviceOption
{
    public string Name { get; set; } = "";
    public BarrierConfig BarrierConfig { get; set; } = new();
    public TrafficLightConfig TrafficLightConfig { get; set; } = new();
    public string PortName => BarrierConfig.PortName;
}

public partial class ControlsViewModel : ObservableObject
{
    private readonly DatabaseService _dbService;
    private bool _isSyncing;

    public BarrierViewModel BarrierVM { get; }
    public TrafficLightViewModel TrafficLightVM { get; }

    [ObservableProperty] private ArduinoDeviceOption? _selectedDevice;
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private string _statusMessage = "Listo";

    public ObservableCollection<ArduinoDeviceOption> Devices { get; } = new();

    // ── Editor ──
    [ObservableProperty] private bool _isDeviceEditorOpen;
    [ObservableProperty] private string _editingName = "";
    [ObservableProperty] private string _editingPort = "COM3";
    [ObservableProperty] private int _editingBaudRate = 9600;
    private bool _isEditMode;
    private ArduinoDeviceOption? _editingDevice;

    public int[] BaudRates => [300, 600, 1200, 2400, 4800, 9600, 14400, 19200, 38400, 57600, 115200];
    public string[] AvailablePorts => SerialPort.GetPortNames();

    public ControlsViewModel(DatabaseService dbService, BarrierViewModel barrierVm, TrafficLightViewModel trafficLightVm)
    {
        _dbService = dbService;
        BarrierVM = barrierVm;
        TrafficLightVM = trafficLightVm;

        BarrierVM.PropertyChanged += OnBarrierPropertyChanged;

        _ = LoadDevicesAsync();
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (SelectedDevice == null)
        {
            StatusMessage = "Seleccione un dispositivo";
            return;
        }

        try
        {
            StatusMessage = "Conectando...";
            await BarrierVM.DisconnectCommand.ExecuteAsync(null);

            BarrierVM.SelectedBarrier = SelectedDevice.BarrierConfig;
            TrafficLightVM.SelectedTrafficLight = SelectedDevice.TrafficLightConfig;

            await BarrierVM.ConnectCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        try
        {
            await TrafficLightVM.SetColorSafeAsync(TrafficLightColor.Off);
            await BarrierVM.DisconnectCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    // ── CRUD ──

    [RelayCommand]
    private void AddDevice()
    {
        _isEditMode = false;
        _editingDevice = null;
        EditingName = $"Arduino {Devices.Count + 1}";
        EditingPort = "COM3";
        EditingBaudRate = 9600;
        IsDeviceEditorOpen = true;
    }

    [RelayCommand]
    private void EditDevice()
    {
        if (SelectedDevice == null) return;
        _isEditMode = true;
        _editingDevice = SelectedDevice;
        EditingName = SelectedDevice.Name;
        EditingPort = SelectedDevice.PortName;
        EditingBaudRate = SelectedDevice.BarrierConfig.BaudRate;
        IsDeviceEditorOpen = true;
    }

    [RelayCommand]
    private async Task SaveDeviceAsync()
    {
        var ctx = _dbService.CreateContext();
        var barrierRepo = new Repository<BarrierConfig>(ctx);
        var tlRepo = new Repository<TrafficLightConfig>(ctx);

        if (_isEditMode && _editingDevice != null)
        {
            _editingDevice.Name = EditingName;
            _editingDevice.BarrierConfig.Name = $"{EditingName} (Barrera)";
            _editingDevice.BarrierConfig.PortName = EditingPort;
            _editingDevice.BarrierConfig.BaudRate = EditingBaudRate;
            _editingDevice.TrafficLightConfig.Name = $"{EditingName} (Semáforo)";
            _editingDevice.TrafficLightConfig.PortName = EditingPort;
            _editingDevice.TrafficLightConfig.BaudRate = EditingBaudRate;

            await barrierRepo.UpdateAsync(_editingDevice.BarrierConfig);
            await tlRepo.UpdateAsync(_editingDevice.TrafficLightConfig);
        }
        else
        {
            var barrier = new BarrierConfig
            {
                Name = $"{EditingName} (Barrera)",
                ConnectionType = DeviceConnectionType.Serial,
                PortName = EditingPort,
                BaudRate = EditingBaudRate,
                IsActive = true
            };
            var tl = new TrafficLightConfig
            {
                Name = $"{EditingName} (Semáforo)",
                ConnectionType = DeviceConnectionType.Serial,
                PortName = EditingPort,
                BaudRate = EditingBaudRate,
                IsActive = true
            };

            await barrierRepo.AddAsync(barrier);
            await tlRepo.AddAsync(tl);
        }

        IsDeviceEditorOpen = false;
        await LoadDevicesAsync();
        StatusMessage = "Dispositivo guardado";
    }

    [RelayCommand]
    private void CancelDeviceEditor()
    {
        IsDeviceEditorOpen = false;
    }

    [RelayCommand]
    private async Task DeleteDeviceAsync()
    {
        if (SelectedDevice == null) return;
        var ctx = _dbService.CreateContext();
        var barrierRepo = new Repository<BarrierConfig>(ctx);
        var tlRepo = new Repository<TrafficLightConfig>(ctx);

        await barrierRepo.DeleteAsync(SelectedDevice.BarrierConfig);
        await tlRepo.DeleteAsync(SelectedDevice.TrafficLightConfig);

        await LoadDevicesAsync();
        StatusMessage = "Dispositivo eliminado";
    }

    // ── Internal ──

    private async Task LoadDevicesAsync()
    {
        var ctx = _dbService.CreateContext();
        var barrierRepo = new Repository<BarrierConfig>(ctx);
        var tlRepo = new Repository<TrafficLightConfig>(ctx);

        // Clean up orphaned records from old separate configs
        var oldBarriers = (await barrierRepo.FindAsync(b => b.IsActive))
            .Where(b => !b.Name.Contains(" (Barrera)")).ToList();
        var oldTls = (await tlRepo.FindAsync(t => t.IsActive))
            .Where(t => !t.Name.Contains(" (Semáforo)")).ToList();

        foreach (var o in oldBarriers) await barrierRepo.DeleteAsync(o);
        foreach (var o in oldTls) await tlRepo.DeleteAsync(o);

        var barriers = (await barrierRepo.GetAllAsync()).Where(b => b.IsActive).ToList();
        var tls = (await tlRepo.GetAllAsync()).Where(t => t.IsActive).ToList();

        var pairs = from b in barriers
                    join t in tls
                    on b.Name.Replace(" (Barrera)", "").Trim()
                    equals t.Name.Replace(" (Semáforo)", "").Trim()
                    select new ArduinoDeviceOption
                    {
                        Name = b.Name.Replace(" (Barrera)", "").Replace(" (Semáforo)", "").Trim(),
                        BarrierConfig = b,
                        TrafficLightConfig = t
                    };

        App.Current.Dispatcher.Invoke(() =>
        {
            Devices.Clear();
            foreach (var p in pairs)
                Devices.Add(p);
        });
    }

    private async void OnBarrierPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(BarrierViewModel.IsConnected):
                IsConnected = BarrierVM.IsConnected;
                if (BarrierVM.IsConnected)
                {
                    if (!TrafficLightVM.IsConnected)
                        await TrafficLightVM.ConnectFirstOrDefaultAsync();
                    await SyncTrafficLightAsync();
                    StatusMessage = "Conectado";
                }
                else
                {
                    await TrafficLightVM.SetColorSafeAsync(TrafficLightColor.Off);
                    if (TrafficLightVM.IsConnected)
                        await TrafficLightVM.DisconnectCommand.ExecuteAsync(null);
                    StatusMessage = "Desconectado";
                }
                break;

            case nameof(BarrierViewModel.IsOpen):
                await SyncTrafficLightAsync();
                break;
        }
    }

    private async Task SyncTrafficLightAsync()
    {
        if (_isSyncing) return;
        _isSyncing = true;

        try
        {
            if (BarrierVM.IsConnected)
            {
                var color = BarrierVM.IsOpen
                    ? TrafficLightColor.Green
                    : TrafficLightColor.Red;
                await TrafficLightVM.SetColorSafeAsync(color);
            }
        }
        finally
        {
            _isSyncing = false;
        }
    }
}
