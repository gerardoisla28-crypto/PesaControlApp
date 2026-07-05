using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PesaControl.Core.Models;
using PesaControl.Core.Services;
using PesaControl.Data.Data;
using PesaControl.Data.Repositories;
using PesaControl.UI.Services;

namespace PesaControl.UI.ViewModels;

public partial class BarrierViewModel : ObservableObject
{
    private readonly DatabaseService _dbService;
    private IBarrierService? _barrierService;
    private readonly IRepository<BarrierConfig> _barrierRepo;

    [ObservableProperty] private BarrierConfig? _selectedBarrier;
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private string _statusMessage = "Listo";

    public ObservableCollection<BarrierConfig> Barriers { get; } = new();

    public BarrierViewModel(DatabaseService dbService)
    {
        _dbService = dbService;
        var ctx = dbService.CreateContext();
        _barrierRepo = new Repository<BarrierConfig>(ctx);

        _ = LoadBarriersAsync();
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (SelectedBarrier == null)
        {
            StatusMessage = "Seleccione una barrera";
            return;
        }

        try
        {
            StatusMessage = "Conectando...";
            await DisconnectAsync();

            _barrierService = CreateService(SelectedBarrier);
            _barrierService.ConnectionStatusChanged += OnConnectionStatusChanged;
            _barrierService.ErrorOccurred += (_, err) =>
                App.Current.Dispatcher.Invoke(() => StatusMessage = $"Error: {err}");

            await _barrierService.ConnectAsync(SelectedBarrier);

            var status = await _barrierService.GetStatusAsync();
            IsOpen = status;

            StatusMessage = $"Conectado a {SelectedBarrier.Name}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        if (_barrierService != null)
        {
            _barrierService.ConnectionStatusChanged -= OnConnectionStatusChanged;
            await _barrierService.DisconnectAsync();
            _barrierService = null;
        }

        IsConnected = false;
        IsOpen = false;
        StatusMessage = "Desconectado";
    }

    [RelayCommand]
    private async Task OpenAsync()
    {
        if (_barrierService == null || !IsConnected)
        {
            StatusMessage = "Barrera no conectada";
            return;
        }

        try
        {
            await _barrierService.OpenAsync();
            IsOpen = true;
            StatusMessage = "Barrera abierta";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task CloseAsync()
    {
        if (_barrierService == null || !IsConnected)
        {
            StatusMessage = "Barrera no conectada";
            return;
        }

        try
        {
            await _barrierService.CloseAsync();
            IsOpen = false;
            StatusMessage = "Barrera cerrada";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RefreshStatusAsync()
    {
        if (_barrierService == null || !IsConnected)
        {
            StatusMessage = "Barrera no conectada";
            return;
        }

        try
        {
            var status = await _barrierService.GetStatusAsync();
            IsOpen = status;
            StatusMessage = status ? "Barrera abierta" : "Barrera cerrada";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    private void OnConnectionStatusChanged(object? sender, bool connected)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            IsConnected = connected;
            if (!connected) IsOpen = false;
        });
    }

    private static IBarrierService CreateService(BarrierConfig config)
    {
        return config.ConnectionType switch
        {
            DeviceConnectionType.ModbusRTU or DeviceConnectionType.ModbusTCP => new ModbusBarrierService(),
            _ => new BarrierService()
        };
    }

    private async Task LoadBarriersAsync()
    {
        var barriers = await _barrierRepo.FindAsync(b => b.IsActive);
        App.Current.Dispatcher.Invoke(() =>
        {
            Barriers.Clear();
            foreach (var b in barriers) Barriers.Add(b);
        });
    }
}
