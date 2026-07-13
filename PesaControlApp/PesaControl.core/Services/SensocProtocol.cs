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
    private static bool _debugEnabled;

    private WeightReading _lastReading;
    private DateTime _lastReadingTime;
    private volatile bool _hasPendingData;

    public string Name => "Sensoc (Kronos HU1081)";
    public bool IsConnected => _port?.IsOpen ?? false;
    public static bool DebugEnabled { get => _debugEnabled; set => _debugEnabled = value; }

    public event EventHandler<WeightReading>? WeightChanged;
    public event EventHandler<bool>? ConnectionStatusChanged;
    public event EventHandler<string>? ErrorOccurred;

    public async Task ConnectAsync(CommunicationConfig config)
    {
        await CleanupAsync();
        _disposed = false;
        _config = config;
        _buffer.Clear();
        _hasPendingData = false;
        _lastReading = default;
        _lastReadingTime = DateTime.MinValue;

        try
        {
            _port = new SerialPort(config.PortName, config.BaudRate,
                (Parity)config.Parity, config.DataBits, (StopBits)config.StopBits)
            {
                ReadTimeout = 500,
                WriteTimeout = 500,
                DtrEnable = true,
                RtsEnable = true,
                NewLine = "\r\n",
                Encoding = Encoding.ASCII
            };

            _port.DataReceived += OnDataReceived;
            _port.Open();
            _cts = new CancellationTokenSource();

            _ = ReadLoopAsync(_cts.Token);

            ConnectionStatusChanged?.Invoke(this, true);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Error conexión Sensoc: {ex.Message}");
            ConnectionStatusChanged?.Invoke(this, false);
        }
    }

    public async Task DisconnectAsync()
    {
        await CleanupAsync();
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
        var pollInterval = Math.Max(_config?.ReadIntervalMs ?? 500, 300);

        while (!ct.IsCancellationRequested && !_disposed)
        {
            try
            {
                await Task.Delay(pollInterval, ct);
                if (_port?.IsOpen != true || _disposed) continue;

                if ((DateTime.UtcNow - _lastReadingTime).TotalMilliseconds < pollInterval)
                    continue;

                if (_hasPendingData)
                {
                    _hasPendingData = false;
                    continue;
                }

                // Fallback: send weight request command
                var cmd = "W\r\n"u8.ToArray();
                _port.Write(cmd, 0, cmd.Length);

                await Task.Delay(100, ct);

                var rawBytes = ReadRawBytes();
                if (rawBytes.Length > 0)
                {
                    var text = Encoding.ASCII.GetString(rawBytes);
                    ProcessIncomingData(text);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex)
            {
                if (!_disposed)
                    ErrorOccurred?.Invoke(this, $"Error lectura: {ex.Message}");
            }
        }
    }

    private byte[] ReadRawBytes()
    {
        try
        {
            if (_port == null) return [];
            var buf = new byte[256];
            var total = 0;
            while (_port!.BytesToRead > 0 && total < buf.Length)
            {
                var read = _port.Read(buf, total, Math.Min(buf.Length - total, _port.BytesToRead));
                if (read <= 0) break;
                total += read;
            }
            if (total == 0) return [];
            var result = new byte[total];
            Array.Copy(buf, result, total);
            return result;
        }
        catch
        {
            return [];
        }
    }

    private void OnDataReceived(object? sender, SerialDataReceivedEventArgs e)
    {
        if (_disposed) return;

        try
        {
            var raw = _port?.ReadExisting();
            if (string.IsNullOrEmpty(raw)) return;

            _hasPendingData = true;
            ProcessIncomingData(raw);
        }
        catch (Exception ex)
        {
            if (!_disposed)
                ErrorOccurred?.Invoke(this, $"Error recepción: {ex.Message}");
        }
    }

    private void ProcessIncomingData(string raw)
    {
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
            var trimmed = frame.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            var reading = ParseSensocFrame(trimmed);
            if (reading.Value != 0 || !string.IsNullOrEmpty(reading.RawData))
            {
                lock (_bufferLock)
                {
                    _lastReading = reading;
                    _lastReadingTime = DateTime.UtcNow;
                }
                WeightChanged?.Invoke(this, reading);
            }
        }
    }

    private static WeightReading ParseSensocFrame(string frame)
    {
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

    private async Task CleanupAsync()
    {
        _disposed = true;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        if (_port != null)
        {
            _port.DataReceived -= OnDataReceived;
            await Task.Run(() =>
            {
                try
                {
                    if (_port.IsOpen)
                        _port.Close();
                }
                catch { }
            });
            _port.Dispose();
            _port = null;
        }

        lock (_bufferLock)
        {
            _buffer.Clear();
            _lastReading = default;
            _lastReadingTime = DateTime.MinValue;
            _hasPendingData = false;
        }
    }

    public void Dispose()
    {
        CleanupAsync().GetAwaiter().GetResult();
    }
}
