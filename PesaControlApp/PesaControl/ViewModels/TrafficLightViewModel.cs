using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PesaControl.Core.Models;
using PesaControl.Core.Services;
using PesaControl.Data.Data;
using PesaControl.Data.Repositories;
using PesaControl.UI.Services;

namespace PesaControl.UI.ViewModels;

public partial class TrafficLightViewModel : ObservableObject
{
    private readonly DatabaseService _dbService;
    private ITrafficLightService? _trafficLightService;
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

            _trafficLightService = CreateService(SelectedTrafficLight);
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
            _trafficLightService = null;
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
