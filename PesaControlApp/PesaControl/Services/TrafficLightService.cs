using System.IO.Ports;
using PesaControl.Core.Models;
using PesaControl.Core.Services;

namespace PesaControl.UI.Services;

public class TrafficLightService : ITrafficLightService, IDisposable
{
    private SerialPort? _serial;
    private TrafficLightConfig _config = new();
    private bool _disposed;

    public string Name => _config.Name;
    public bool IsConnected => _serial?.IsOpen == true;

    public event EventHandler<bool>? ConnectionStatusChanged;
    public event EventHandler<string>? ErrorOccurred;

    public Task ConnectAsync(TrafficLightConfig config)
    {
        DisconnectAsync();

        _config = config;
        _serial = new SerialPort(config.PortName, config.BaudRate)
        {
            ReadTimeout = 1000,
            WriteTimeout = 1000
        };

        try
        {
            _serial.Open();
            ConnectionStatusChanged?.Invoke(this, true);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Error al conectar semáforo: {ex.Message}");
            throw;
        }

        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        if (_serial?.IsOpen == true)
            _serial.Close();

        _serial?.Dispose();
        _serial = null;
        ConnectionStatusChanged?.Invoke(this, false);
        return Task.CompletedTask;
    }

    public Task SetColorAsync(TrafficLightColor color)
    {
        if (_serial?.IsOpen != true)
        {
            var msg = "Semáforo no conectado";
            ErrorOccurred?.Invoke(this, msg);
            throw new InvalidOperationException(msg);
        }

        var cmd = color switch
        {
            TrafficLightColor.Red => "RED",
            TrafficLightColor.Yellow => "YEL",
            TrafficLightColor.Green => "GRN",
            TrafficLightColor.Off => "OFF",
            _ => "OFF"
        };

        _serial.WriteLine(cmd);
        return Task.CompletedTask;
    }

    public Task<TrafficLightColor> GetColorAsync()
    {
        if (_serial?.IsOpen != true)
        {
            var msg = "Semáforo no conectado";
            ErrorOccurred?.Invoke(this, msg);
            throw new InvalidOperationException(msg);
        }

        _serial.WriteLine("COLOR");
        var response = _serial.ReadLine()?.Trim().ToUpper();

        var color = response switch
        {
            "RED" => TrafficLightColor.Red,
            "YEL" or "YELLOW" => TrafficLightColor.Yellow,
            "GRN" or "GREEN" => TrafficLightColor.Green,
            _ => TrafficLightColor.Off
        };

        return Task.FromResult(color);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _serial?.Dispose();
    }
}
