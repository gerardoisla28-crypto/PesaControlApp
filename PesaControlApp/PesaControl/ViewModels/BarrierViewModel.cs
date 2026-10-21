using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PesaControl.Core.Models;
using PesaControl.Core.Services;
using PesaControl.Data.Data;
using PesaControl.Data.Repositories;
using PesaControl.UI.Services;

namespace PesaControl.UI.ViewModels;

public partial class BarrierViewModel : ObservableObject, IUnsavedChanges
{
    private readonly DatabaseService _dbService;
    private IBarrierService? _barrierService;
    private IBarrierService? _externalService;
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

    public void SetExternalService(IBarrierService? service)
    {
        _externalService = service;
    }

    // ── Cambios sin guardar ────────────────────────────────

    private EditorTracker? _editorTracker;
    private EditorTracker Editor => _editorTracker ??= new EditorTracker(() => System.Text.Json.JsonSerializer.Serialize(EditingBarrier));

    partial void OnIsBarrierEditorOpenChanged(bool value) { if (!value) Editor.End(); }

    public bool HasUnsavedChanges => IsBarrierEditorOpen && Editor.IsDirty;
    public string UnsavedChangesDescription => "Barrera";

    public async Task<bool> SaveChangesAsync()
    {
        await SaveBarrierAsync();
        return !IsBarrierEditorOpen;
    }

    public void DiscardChanges() => CancelBarrierEditor();

    // ── Config editor ──────────────────────────────────────

    [ObservableProperty] private bool _isBarrierEditorOpen;
    [ObservableProperty] private BarrierConfig? _editingBarrier;

    public string[] ConnectionTypeNames => ["Serial", "Modbus RTU", "Modbus TCP"];
    public int[] BaudRates => [300, 600, 1200, 2400, 4800, 9600, 14400, 19200, 38400, 57600, 115200];

    [RelayCommand]
    private void AddBarrier()
    {
        EditingBarrier = new BarrierConfig
        {
            Name = $"Barrera {Barriers.Count + 1}",
            IsActive = true,
            ConnectionType = DeviceConnectionType.Serial,
            PortName = "COM3",
            BaudRate = 9600
        };
        Editor.Begin();
        IsBarrierEditorOpen = true;
    }

    [RelayCommand]
    private void EditBarrier()
    {
        if (SelectedBarrier is null) return;
        EditingBarrier = new BarrierConfig
        {
            Id = SelectedBarrier.Id,
            Name = SelectedBarrier.Name,
            IsActive = SelectedBarrier.IsActive,
            ConnectionType = SelectedBarrier.ConnectionType,
            PortName = SelectedBarrier.PortName,
            BaudRate = SelectedBarrier.BaudRate,
            SlaveId = SelectedBarrier.SlaveId,
            CoilAddress = SelectedBarrier.CoilAddress,
            IpAddress = SelectedBarrier.IpAddress,
            TcpPort = SelectedBarrier.TcpPort
        };
        Editor.Begin();
        IsBarrierEditorOpen = true;
    }

    [RelayCommand]
    private async Task SaveBarrierAsync()
    {
        if (EditingBarrier is null) return;
        var saved = EditingBarrier;
        var ctx = _dbService.CreateContext();
        var repo = new Repository<BarrierConfig>(ctx);
        if (saved.Id == 0)
            await repo.AddAsync(saved);
        else
            await repo.UpdateAsync(saved);
        IsBarrierEditorOpen = false;
        EditingBarrier = null;
        await LoadBarriersAsync();
        StatusMessage = "Barrera guardada";
    }

    [RelayCommand]
    private void CancelBarrierEditor()
    {
        IsBarrierEditorOpen = false;
        EditingBarrier = null;
    }

    [RelayCommand]
    private async Task DeleteBarrierAsync()
    {
        if (SelectedBarrier is null) return;
        var ctx = _dbService.CreateContext();
        var repo = new Repository<BarrierConfig>(ctx);
        await repo.DeleteAsync(SelectedBarrier);
        await LoadBarriersAsync();
        StatusMessage = "Barrera eliminada";
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

            _barrierService = _externalService ?? CreateService(SelectedBarrier);
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
            if (_externalService == null)
            {
                _barrierService = null;
            }
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
