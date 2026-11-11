using System.IO.Ports;
using PesaControl.Core.Models;
using PesaControl.Core.Services;

namespace PesaControl.UI.Services;

public class ArduinoAccessService : IBarrierService, ITrafficLightService, IAccessDeviceInfo, IDisposable
{
    /// <summary>Salidas que el equipo dijo tener (se llena al conectar).</summary>
    public bool HasBarrier { get; private set; } = true;
    public bool HasTrafficLight { get; private set; } = true;

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

        // Sin barrera instalada no hay nada que consultar
        if (!HasBarrier) return Task.FromResult(false);

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
        if (!HasTrafficLight) { _currentColor = color; return; }

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

        if (!HasTrafficLight) return Task.FromResult(TrafficLightColor.Off);

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

                // Comprobar que del otro lado hay realmente un equipo y qué salidas tiene
                var serial = _serial;
                try
                {
                    var caps = await Task.Run(() => Handshake(serial));
                    HasBarrier = caps.barrier;
                    HasTrafficLight = caps.light;
                }
                catch
                {
                    try { _serial.Close(); } catch { }
                    _serial.Dispose();
                    _serial = null;
                    throw;
                }

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

    /// <summary>
    /// Le pregunta al equipo qué tiene instalado ("CAPS"). Si el sketch es antiguo y no conoce
    /// ese comando, prueba con "STATUS" y asume barrera + semáforo. Si no responde nada, falla.
    /// </summary>
    private static (bool barrier, bool light) Handshake(SerialPort serial)
    {
        // El Arduino se reinicia al abrir el puerto: darle tiempo a arrancar
        Thread.Sleep(1800);

        var oldTimeout = serial.ReadTimeout;
        serial.ReadTimeout = 1200;
        try
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                serial.DiscardInBuffer();
                serial.WriteLine("CAPS");
                var response = ReadResponse(serial, "CAPS");
                if (response != null)
                {
                    var upper = response.ToUpperInvariant();
                    return (upper.Contains("BARRIER=1"), upper.Contains("LIGHT=1"));
                }
            }

            // Sketch antiguo: solo entiende STATUS (barrera + semáforo)
            serial.DiscardInBuffer();
            serial.WriteLine("STATUS");
            var legacy = ReadResponse(serial, "OPEN", "CLOSE");
            if (legacy != null) return (true, true);

            throw new InvalidOperationException(
                $"No hay un equipo de control de acceso respondiendo en {serial.PortName}");
        }
        finally
        {
            serial.ReadTimeout = oldTimeout;
        }
    }

    private static string? ReadResponse(SerialPort serial, params string[] prefixes)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(1500);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var line = serial.ReadLine().Trim();
                if (prefixes.Any(p => line.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                    return line;
            }
            catch (TimeoutException)
            {
                return null;
            }
        }
        return null;
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
