using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PesaControl.Core.Models;
using PesaControl.Core.Services;
using PesaControl.Data.Data;
using PesaControl.Data.Repositories;
using PesaControl.UI.Services;

namespace PesaControl.UI.ViewModels;

public partial class TrafficLightViewModel : ObservableObject, IUnsavedChanges
{
    private readonly DatabaseService _dbService;
    private ITrafficLightService? _trafficLightService;
    private ITrafficLightService? _externalService;
    private readonly IRepository<TrafficLightConfig> _trafficLightRepo;

    [ObservableProperty] private TrafficLightConfig? _selectedTrafficLight;
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private TrafficLightColor _currentColor = TrafficLightColor.Off;
    [ObservableProperty] private string _statusMessage = "Listo";

    public ObservableCollection<TrafficLightConfig> TrafficLights { get; } = new();

    public TrafficLightViewModel(DatabaseService dbService)
    {
        _dbService = dbService;
        var ctx = dbService.CreateContext();
        _trafficLightRepo = new Repository<TrafficLightConfig>(ctx);

        _ = LoadTrafficLightsAsync();
    }

    public void SetExternalService(ITrafficLightService? service)
    {
        _externalService = service;
    }

    // ── Cambios sin guardar ────────────────────────────────

    private EditorTracker? _editorTracker;
    private EditorTracker Editor => _editorTracker ??= new EditorTracker(() => System.Text.Json.JsonSerializer.Serialize(EditingTrafficLight));

    partial void OnIsTrafficLightEditorOpenChanged(bool value) { if (!value) Editor.End(); }

    public bool HasUnsavedChanges => IsTrafficLightEditorOpen && Editor.IsDirty;
    public string UnsavedChangesDescription => "Semáforo";

    public async Task<bool> SaveChangesAsync()
    {
        await SaveTrafficLightAsync();
        return !IsTrafficLightEditorOpen;
    }

    public void DiscardChanges() => CancelTrafficLightEditor();

    // ── Config editor ──────────────────────────────────

    [ObservableProperty] private bool _isTrafficLightEditorOpen;
    [ObservableProperty] private TrafficLightConfig? _editingTrafficLight;

    public string[] ConnectionTypeNames => ["Serial", "Modbus RTU", "Modbus TCP"];
    public int[] BaudRates => [300, 600, 1200, 2400, 4800, 9600, 14400, 19200, 38400, 57600, 115200];

    [RelayCommand]
    private void AddTrafficLight()
    {
        EditingTrafficLight = new TrafficLightConfig
        {
            Name = $"Semáforo {TrafficLights.Count + 1}",
            IsActive = true,
            ConnectionType = DeviceConnectionType.Serial,
            PortName = "COM3",
            BaudRate = 9600
        };
        Editor.Begin();
        IsTrafficLightEditorOpen = true;
    }

    [RelayCommand]
    private void EditTrafficLight()
    {
        if (SelectedTrafficLight is null) return;
        EditingTrafficLight = new TrafficLightConfig
        {
            Id = SelectedTrafficLight.Id,
            Name = SelectedTrafficLight.Name,
            IsActive = SelectedTrafficLight.IsActive,
            ConnectionType = SelectedTrafficLight.ConnectionType,
            PortName = SelectedTrafficLight.PortName,
            BaudRate = SelectedTrafficLight.BaudRate,
            SlaveId = SelectedTrafficLight.SlaveId,
            RegisterAddress = SelectedTrafficLight.RegisterAddress,
            RedValue = SelectedTrafficLight.RedValue,
            YellowValue = SelectedTrafficLight.YellowValue,
            GreenValue = SelectedTrafficLight.GreenValue,
            OffValue = SelectedTrafficLight.OffValue,
            IpAddress = SelectedTrafficLight.IpAddress,
            TcpPort = SelectedTrafficLight.TcpPort
        };
        Editor.Begin();
        IsTrafficLightEditorOpen = true;
    }

    [RelayCommand]
    private async Task SaveTrafficLightAsync()
    {
        if (EditingTrafficLight is null) return;
        var saved = EditingTrafficLight;
        var ctx = _dbService.CreateContext();
        var repo = new Repository<TrafficLightConfig>(ctx);
        if (saved.Id == 0)
            await repo.AddAsync(saved);
        else
            await repo.UpdateAsync(saved);
        IsTrafficLightEditorOpen = false;
        EditingTrafficLight = null;
        await LoadTrafficLightsAsync();
        StatusMessage = "Semáforo guardado";
    }

    [RelayCommand]
    private void CancelTrafficLightEditor()
    {
        IsTrafficLightEditorOpen = false;
        EditingTrafficLight = null;
    }

    [RelayCommand]
    private async Task DeleteTrafficLightAsync()
    {
        if (SelectedTrafficLight is null) return;
        var ctx = _dbService.CreateContext();
        var repo = new Repository<TrafficLightConfig>(ctx);
        await repo.DeleteAsync(SelectedTrafficLight);
        await LoadTrafficLightsAsync();
        StatusMessage = "Semáforo eliminado";
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (SelectedTrafficLight == null)
        {
            StatusMessage = "Seleccione un semáforo";
            return;
        }

        try
        {
            StatusMessage = "Conectando...";
            await DisconnectAsync();

            _trafficLightService = _externalService ?? CreateService(SelectedTrafficLight);
            _trafficLightService.ConnectionStatusChanged += OnConnectionStatusChanged;
            _trafficLightService.ErrorOccurred += (_, err) =>
                App.Current.Dispatcher.Invoke(() => StatusMessage = $"Error: {err}");

            await _trafficLightService.ConnectAsync(SelectedTrafficLight);
            IsConnected = true;

            StatusMessage = $"Conectado a {SelectedTrafficLight.Name}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        if (_trafficLightService != null)
        {
            _trafficLightService.ConnectionStatusChanged -= OnConnectionStatusChanged;
            await _trafficLightService.DisconnectAsync();
            if (_externalService == null)
            {
                _trafficLightService = null;
            }
        }

        IsConnected = false;
        CurrentColor = TrafficLightColor.Off;
        StatusMessage = "Desconectado";
    }

    /// <summary>
    /// Sets traffic light color safely (no-op if not connected).
    /// Silently ignores errors — for automatic follow mode.
    /// </summary>
    public async Task SetColorSafeAsync(TrafficLightColor color)
    {
        if (_trafficLightService == null || !IsConnected)
            return;

        try
        {
            await _trafficLightService.SetColorAsync(color);
            CurrentColor = color;
        }
        catch
        {
            // Silently ignore — hardware may be unavailable
        }
    }

    public async Task ConnectFirstOrDefaultAsync()
    {
        var configs = await _trafficLightRepo.FindAsync(t => t.IsActive);
        var first = configs.FirstOrDefault();
        if (first is null) return;

        SelectedTrafficLight = first;
        await ConnectAsync();
    }

    private void OnConnectionStatusChanged(object? sender, bool connected)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            IsConnected = connected;
            if (!connected) CurrentColor = TrafficLightColor.Off;
        });
    }

    private static ITrafficLightService CreateService(TrafficLightConfig config)
    {
        return config.ConnectionType switch
        {
            DeviceConnectionType.ModbusRTU or DeviceConnectionType.ModbusTCP => new ModbusTrafficLightService(),
            _ => new TrafficLightService()
        };
    }

    private async Task LoadTrafficLightsAsync()
    {
        var lights = await _trafficLightRepo.FindAsync(t => t.IsActive);
        App.Current.Dispatcher.Invoke(() =>
        {
            TrafficLights.Clear();
            foreach (var t in lights) TrafficLights.Add(t);
        });
    }
}
