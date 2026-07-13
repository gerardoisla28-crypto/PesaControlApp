using System.IO.Ports;
using PesaControl.Core.Models;
using PesaControl.Core.Services;

namespace PesaControl.UI.Services;

public class ArduinoAccessService : IBarrierService, ITrafficLightService, IDisposable
{
    private SerialPort? _serial;
    private BarrierConfig? _barrierConfig;
    private TrafficLightConfig? _trafficLightConfig;
    private TrafficLightColor _currentColor = TrafficLightColor.Off;
    private int _connectCount;
    private readonly SemaphoreSlim _serialLock = new(1, 1);
    private bool _disposed;

    public string Name => _barrierConfig?.Name ?? _trafficLightConfig?.Name ?? "Arduino";
    public bool IsConnected => _serial?.IsOpen == true;

    public event EventHandler<bool>? ConnectionStatusChanged;
    public event EventHandler<string>? ErrorOccurred;

    public void SetBarrierConfig(BarrierConfig config) => _barrierConfig = config;
    public void SetTrafficLightConfig(TrafficLightConfig config) => _trafficLightConfig = config;

    // --- IBarrierService ---
    async Task IBarrierService.ConnectAsync(BarrierConfig config)
    {
        _barrierConfig = config;
        await ConnectInternalAsync(config.PortName, config.BaudRate);
    }

    public Task<bool> GetStatusAsync()
    {
        if (_serial?.IsOpen != true)
        {
            ErrorOccurred?.Invoke(this, "Arduino no conectado");
            throw new InvalidOperationException("Arduino no conectado");
        }

        _serial.WriteLine("STATUS");
        var response = _serial.ReadLine()?.Trim().ToUpper();
        return Task.FromResult(response == "OPEN" || response == "1");
    }

    public async Task OpenAsync()
    {
        await SendCommandAsync("OPEN");
    }

    public async Task CloseAsync()
    {
        await SendCommandAsync("CLOSE");
    }

    // --- ITrafficLightService ---
    async Task ITrafficLightService.ConnectAsync(TrafficLightConfig config)
    {
        _trafficLightConfig = config;
        await ConnectInternalAsync(config.PortName, config.BaudRate);
    }

    public async Task SetColorAsync(TrafficLightColor color)
    {
        var cmd = color switch
        {
            TrafficLightColor.Red => "RED",
            TrafficLightColor.Yellow => "YEL",
            TrafficLightColor.Green => "GRN",
            _ => "OFF"
        };
        await SendCommandAsync(cmd);
        _currentColor = color;
    }

    public Task<TrafficLightColor> GetColorAsync()
    {
        if (_serial?.IsOpen != true)
        {
            ErrorOccurred?.Invoke(this, "Arduino no conectado");
            throw new InvalidOperationException("Arduino no conectado");
        }

        _serial.WriteLine("COLOR");
        var response = _serial.ReadLine()?.Trim().ToUpper();

        var color = response switch
        {
            "RED" => TrafficLightColor.Red,
            "GRN" or "GREEN" => TrafficLightColor.Green,
            _ => TrafficLightColor.Off
        };
        return Task.FromResult(color);
    }

    // --- Shared Disconnect ---
    public async Task DisconnectAsync()
    {
        await _serialLock.WaitAsync();
        try
        {
            if (_connectCount > 0)
                _connectCount--;

            if (_connectCount == 0 && _serial?.IsOpen == true)
            {
                _serial.Close();
                ConnectionStatusChanged?.Invoke(this, false);
            }
        }
        finally
        {
            _serialLock.Release();
        }
    }

    // --- Internal ---
    private async Task ConnectInternalAsync(string portName, int baudRate)
    {
        await _serialLock.WaitAsync();
        try
        {
            if (_connectCount == 0)
            {
                _serial?.Dispose();
                _serial = new SerialPort(portName, baudRate)
                {
                    ReadTimeout = 2000,
                    WriteTimeout = 1000
                };
                _serial.Open();
                ConnectionStatusChanged?.Invoke(this, true);
            }
            _connectCount++;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Error al conectar Arduino: {ex.Message}");
            throw;
        }
        finally
        {
            _serialLock.Release();
        }
    }

    private async Task SendCommandAsync(string command)
    {
        await _serialLock.WaitAsync();
        try
        {
            if (_serial?.IsOpen != true)
                throw new InvalidOperationException("Arduino no conectado");
            _serial.WriteLine(command);
        }
        finally
        {
            _serialLock.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _serial?.Dispose();
        _serialLock.Dispose();
    }
}
