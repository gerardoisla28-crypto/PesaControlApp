using System.IO.Ports;
using System.Text;
using PesaControl.Core.Models;

namespace PesaControl.Core.Services;

public class SensocarA12Protocol : IWeighingProtocol, IDisposable
{
    private SerialPort? _port;
    private CommunicationConfig? _config;
    private CancellationTokenSource? _cts;
    private volatile bool _disposed;
    private readonly object _bufferLock = new();
    private readonly List<byte> _buffer = new();
    private static bool _debugEnabled;

    private WeightReading _lastReading;
    private DateTime _lastReadingTime;
    private volatile bool _hasPendingData;

    public string Name => "Sensocar SC-A12";
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
                NewLine = "\r",
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
            ErrorOccurred?.Invoke(this, $"Error conexión SC-A12: {ex.Message}");
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
            _port.Write("T\r");
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
            _port.Write("Z\r");
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

                var cmd = "W\r"u8.ToArray();
                _port.Write(cmd, 0, cmd.Length);

                await Task.Delay(150, ct);

                var rawBytes = ReadRawBytes();
                if (rawBytes.Length > 0)
                    ProcessIncomingBytes(rawBytes);
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
            var raw = ReadRawBytes();
            if (raw.Length == 0) return;

            _hasPendingData = true;
            ProcessIncomingBytes(raw);
        }
        catch (Exception ex)
        {
            if (!_disposed)
                ErrorOccurred?.Invoke(this, $"Error recepción: {ex.Message}");
        }
    }

    private void ProcessIncomingBytes(byte[] data)
    {
        LogDebug("RECV", data);

        lock (_bufferLock)
        {
            _buffer.AddRange(data);

            while (true)
            {
                var crIndex = _buffer.IndexOf(0x0D);
                if (crIndex < 0)
                {
                    if (_buffer.Count > 50)
                        _buffer.Clear();
                    break;
                }

                if (crIndex < 1)
                {
                    _buffer.RemoveRange(0, crIndex + 1);
                    continue;
                }

                var frame = _buffer.GetRange(0, crIndex);
                _buffer.RemoveRange(0, crIndex + 1);

                if (frame.Count >= 8)
                    ParseFrame(frame);
            }
        }
    }

    private void ParseFrame(List<byte> frame)
    {
        if (frame.Count < 8) return;

        var statusByte = frame[0];
        var weightChars = new byte[7];
        for (var i = 0; i < 7 && i + 1 < frame.Count; i++)
            weightChars[i] = frame[i + 1];

        var weightText = Encoding.ASCII.GetString(weightChars).Trim();
        var stable = (statusByte & 0x04) == 0x04;
        var isNegative = (statusByte & 0x08) == 0x08;
        var isNet = (statusByte & 0x01) == 0x01;
        var isZero = (statusByte & 0x02) == 0x02;
        var outOfRange = (statusByte & 0x10) == 0x10;

        var value = ParseWeightValue(weightText);
        if (isNegative && value > 0)
            value = -value;

        LogDebug("FRAME",
            $"status=0x{statusByte:X2} stable={stable} neg={isNegative} net={isNet} zero={isZero} oor={outOfRange} " +
            $"weight='{weightText}' value={value}");

        var reading = new WeightReading
        {
            Value = outOfRange ? 0 : value,
            IsStable = stable && !outOfRange,
            Timestamp = DateTime.UtcNow,
            RawData = $"0x{statusByte:X2} {weightText}"
        };

        if (reading.Value != 0 || !string.IsNullOrEmpty(weightText))
        {
            _lastReading = reading;
            _lastReadingTime = DateTime.UtcNow;
            WeightChanged?.Invoke(this, reading);
        }
    }

    private static decimal ParseWeightValue(string text)
    {
        var cleaned = new string(text.Where(c =>
            char.IsDigit(c) || c == '.' || c == ',').ToArray())
            .Replace(',', '.')
            .Trim();

        if (string.IsNullOrEmpty(cleaned))
            return 0;

        if (decimal.TryParse(cleaned,
                System.Globalization.NumberStyles.AllowDecimalPoint,
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

    private static void LogDebug(string label, byte[] data)
    {
        if (!_debugEnabled) return;

        var hex = string.Join(" ", data.Select(b => $"0x{b:X2}"));
        var ascii = new StringBuilder(data.Length);
        foreach (var b in data)
        {
            if (b == 0x0D) ascii.Append("\\r");
            else if (b == 0x0A) ascii.Append("\\n");
            else if (b >= 0x20 && b < 0x7F) ascii.Append((char)b);
            else ascii.Append('.');
        }
        var msg = $"[{DateTime.Now:HH:mm:ss.fff}] [SC-A12] {label} len={data.Length} hex=[{hex}] ascii=<{ascii}>";
        try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "PesaControl_debug.log"), msg + Environment.NewLine); } catch { }
    }

    private static void LogDebug(string label, string data)
    {
        if (!_debugEnabled) return;
        var msg = $"[{DateTime.Now:HH:mm:ss.fff}] [SC-A12] {label} {data}";
        try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "PesaControl_debug.log"), msg + Environment.NewLine); } catch { }
    }

    public void Dispose()
    {
        CleanupAsync().GetAwaiter().GetResult();
    }
}
