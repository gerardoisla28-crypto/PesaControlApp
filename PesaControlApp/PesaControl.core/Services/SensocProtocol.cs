using System.IO.Ports;
using System.Text;
using PesaControl.Core.Models;

namespace PesaControl.Core.Services;

public class SensocProtocol : IWeighingProtocol, IDisposable
{
    private SerialPort? _port;
    private CommunicationConfig? _config;
    private CancellationTokenSource? _cts;
    private volatile bool _disposed;
    private readonly object _bufferLock = new();
    private readonly StringBuilder _buffer = new();

    private WeightReading _lastReading;

    public string Name => "Sensoc (Kronos HU1081)";
    public bool IsConnected => _port?.IsOpen ?? false;

    public event EventHandler<WeightReading>? WeightChanged;
    public event EventHandler<bool>? ConnectionStatusChanged;
    public event EventHandler<string>? ErrorOccurred;

    public async Task ConnectAsync(CommunicationConfig config)
    {
        _config = config;
        try
        {
            _port = new SerialPort(config.PortName, config.BaudRate,
                (Parity)config.Parity, config.DataBits, (StopBits)config.StopBits)
            {
                ReadTimeout = 1000,
                WriteTimeout = 1000,
                DtrEnable = true,
                RtsEnable = true,
                NewLine = "\r\n",
                Encoding = Encoding.ASCII
            };

            _port.DataReceived += OnDataReceived;
            _port.Open();
            _disposed = false;
            _cts = new CancellationTokenSource();

            _ = ReadLoopAsync(_cts.Token);

            var msg = $"[{DateTime.Now:HH:mm:ss.fff}] Sensoc OK port={config.PortName} baud={config.BaudRate} dtr=1 rts=1";
            try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "PesaControl_debug.log"), msg + Environment.NewLine); } catch { }

            ConnectionStatusChanged?.Invoke(this, true);
        }
        catch (Exception ex)
        {
            var msg = $"[{DateTime.Now:HH:mm:ss.fff}] Sensoc FAIL: {ex.Message}";
            try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "PesaControl_debug.log"), msg + Environment.NewLine); } catch { }

            ErrorOccurred?.Invoke(this, $"Error conexión Sensoc: {ex.Message}");
            ConnectionStatusChanged?.Invoke(this, false);
        }
    }

    public async Task DisconnectAsync()
    {
        _disposed = true;
        _cts?.Cancel();

        if (_port is { IsOpen: true })
        {
            _port.DataReceived -= OnDataReceived;
            await Task.Run(() =>
            {
                try { _port.Close(); } catch { }
            });
        }

        ConnectionStatusChanged?.Invoke(this, false);
    }

    public Task<WeightReading> ReadWeightAsync()
    {
        lock (_bufferLock)
            return Task.FromResult(_lastReading);
    }

    public Task<bool> TareAsync()
    {
        try
        {
            if (_port?.IsOpen != true || _disposed) return Task.FromResult(false);
            _port.Write("T\r\n");
            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    public Task<bool> ZeroAsync()
    {
        try
        {
            if (_port?.IsOpen != true || _disposed) return Task.FromResult(false);
            _port.Write("Z\r\n");
            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        // En modo de envío continuo, el peso se recibe a través del evento DataReceived.
        // Este loop solo mantiene la conexión activa o realiza otras tareas de fondo si es necesario.
        // Por ahora, solo se espera la cancelación.
        try
        {
            await Task.Delay(Timeout.Infinite, ct);
        }
        catch (TaskCanceledException) { /* Ignorar cancelación */ }
    }

    private void OnDataReceived(object? sender, SerialDataReceivedEventArgs e)
    {
        if (_disposed) return;

        try
        {
            var raw = _port?.ReadExisting();
            if (string.IsNullOrEmpty(raw)) return;

            ProcessIncomingData(raw);
        }
        catch (Exception ex)
        {
            LogDebug("ERR", ex.Message);
            if (!_disposed)
                ErrorOccurred?.Invoke(this, $"Error recepción: {ex.Message}");
        }
    }

    private void ProcessIncomingData(string raw)
    {
        LogDebug("RX", raw);

        string[] frames;
        lock (_bufferLock)
        {
            _buffer.Append(raw);
            var full = _buffer.ToString();
            var parts = full.Split('\r');
            frames = parts[..^1];
            _buffer.Clear().Append(parts[^1].TrimEnd('\n'));
        }

        foreach (var frame in frames)
        {
            var trimmed = frame.Trim(); // Más permisivo con cualquier espacio o carácter de control
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            LogDebug("FRAME", trimmed);

            var reading = ParseSensocFrame(trimmed);
            if (reading.Value != 0 || !string.IsNullOrEmpty(reading.RawData))
            {
                lock (_bufferLock)
                    _lastReading = reading;
                WeightChanged?.Invoke(this, reading);
            }
        }
    }

    private static WeightReading ParseSensocFrame(string frame)
    {
        var logMsg = $"[{DateTime.Now:HH:mm:ss.fff}] PARSE frame=<{frame.Replace("\r","\\r").Replace("\n","\\n")}> len={frame.Length}";
        try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "PesaControl_debug.log"), logMsg + Environment.NewLine); } catch { }

        var value = ParseWeightValue(frame);
        var stable = frame.Contains('S', StringComparison.OrdinalIgnoreCase)
                     && !frame.Contains('U', StringComparison.OrdinalIgnoreCase);
        if (!stable)
            stable = !frame.Contains("US", StringComparison.OrdinalIgnoreCase)
                     && !frame.Contains("UNST", StringComparison.OrdinalIgnoreCase)
                     && !frame.Contains("MOT", StringComparison.OrdinalIgnoreCase);

        return new WeightReading
        {
            Value = value,
            IsStable = stable,
            Timestamp = DateTime.UtcNow,
            RawData = frame
        };
    }

    private static decimal ParseWeightValue(string line)
    {
        var cleaned = new string(line.Where(c =>
            char.IsDigit(c) || c == '.' || c == '-' || c == '+' || c == ',').ToArray())
            .Replace(',', '.')
            .Trim();

        if (string.IsNullOrEmpty(cleaned))
            return 0;

        if (decimal.TryParse(cleaned,
                System.Globalization.NumberStyles.AllowLeadingSign | System.Globalization.NumberStyles.AllowDecimalPoint,
                System.Globalization.CultureInfo.InvariantCulture, out var value))
            return value;

        return 0;
    }

    private static void LogDebug(string label, string data)
    {
        var hex = data.Length == 0 ? "(empty)" : string.Join(" ", data.Select(c => $"0x{(byte)c:X2}"));
        var escaped = data.Replace("\r", "\\r").Replace("\n", "\\n");
        var msg = $"[{DateTime.Now:HH:mm:ss.fff}] {label} len={data.Length} hex=[{hex}] txt=<{escaped}>";
        try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "PesaControl_debug.log"), msg + Environment.NewLine); } catch { }
    }

    public void Dispose()
    {
        _disposed = true;
        if (_port is { IsOpen: true })
        {
            try { _port.DataReceived -= OnDataReceived; _port.Close(); } catch { }
        }
        _port?.Dispose();
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
