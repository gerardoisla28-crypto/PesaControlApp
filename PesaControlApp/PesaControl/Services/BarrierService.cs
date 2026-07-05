using System.IO.Ports;
using PesaControl.Core.Models;
using PesaControl.Core.Services;

namespace PesaControl.UI.Services;

public class BarrierService : IBarrierService, IDisposable
{
    private SerialPort? _serial;
    private BarrierConfig _config = new();
    private bool _disposed;

    public string Name => _config.Name;
    public bool IsConnected => _serial?.IsOpen == true;

    public event EventHandler<bool>? ConnectionStatusChanged;
    public event EventHandler<string>? ErrorOccurred;

    public Task ConnectAsync(BarrierConfig config)
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
            ErrorOccurred?.Invoke(this, $"Error al conectar barrera: {ex.Message}");
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

    public Task OpenAsync()
    {
        if (_serial?.IsOpen != true)
        {
            var msg = "Barrera no conectada";
            ErrorOccurred?.Invoke(this, msg);
            throw new InvalidOperationException(msg);
        }

        _serial.WriteLine("OPEN");
        return Task.CompletedTask;
    }

    public Task CloseAsync()
    {
        if (_serial?.IsOpen != true)
        {
            var msg = "Barrera no conectada";
            ErrorOccurred?.Invoke(this, msg);
            throw new InvalidOperationException(msg);
        }

        _serial.WriteLine("CLOSE");
        return Task.CompletedTask;
    }

    public Task<bool> GetStatusAsync()
    {
        if (_serial?.IsOpen != true)
        {
            var msg = "Barrera no conectada";
            ErrorOccurred?.Invoke(this, msg);
            throw new InvalidOperationException(msg);
        }

        _serial.WriteLine("STATUS");
        var response = _serial.ReadLine()?.Trim().ToUpper();
        return Task.FromResult(response == "OPEN" || response == "1");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _serial?.Dispose();
    }
}
