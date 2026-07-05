using System.IO.Ports;
using System.Text;
using PesaControl.Core.Models;

namespace PesaControl.Core.Services;

public class SerialProtocol : IWeighingProtocol, IDisposable
{
    private SerialPort? _port;
    private CommunicationConfig? _config;
    private CancellationTokenSource? _cts;
    private volatile bool _disposed;
    private readonly SemaphoreSlim _portLock = new(1, 1);

    public string Name => "Serial (RS232/485/USB)";
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
                DtrEnable = false,
                RtsEnable = false,
                NewLine = "\r\n",
                Encoding = Encoding.ASCII
            };
            _port.Open();
            _disposed = false;
            _cts = new CancellationTokenSource();

            _ = ReadLoopAsync(_cts.Token);

            var msg = $"[{DateTime.Now:HH:mm:ss.fff}] Connect OK port={config.PortName} baud={config.BaudRate} parity={config.Parity} data={config.DataBits} stop={config.StopBits}";
            try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "PesaControl_debug.log"), msg + Environment.NewLine); } catch { }

            ConnectionStatusChanged?.Invoke(this, true);
        }
        catch (Exception ex)
        {
            var msg = $"[{DateTime.Now:HH:mm:ss.fff}] Connect FAIL: {ex.Message}";
            try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "PesaControl_debug.log"), msg + Environment.NewLine); } catch { }

            ErrorOccurred?.Invoke(this, $"Error conexión serial: {ex.Message}");
            ConnectionStatusChanged?.Invoke(this, false);
        }
    }

    public async Task DisconnectAsync()
    {
        _disposed = true;
        _cts?.Cancel();

        await Task.Run(() =>
        {
            if (_port?.IsOpen == true)
            {
                try { _port.Close(); } catch { }
            }
        });

        ConnectionStatusChanged?.Invoke(this, false);
    }

    private static readonly byte[][] _weightCommands =
        [ "W\r\n"u8.ToArray(), "R\r\n"u8.ToArray() ];

    public async Task<WeightReading> ReadWeightAsync()
    {
        if (_port?.IsOpen != true || _disposed)
            return new WeightReading { Timestamp = DateTime.UtcNow };

        await _portLock.WaitAsync();
        try
        {
            if (_port?.IsOpen != true || _disposed)
                return new WeightReading { Timestamp = DateTime.UtcNow };

            foreach (var cmd in _weightCommands)
            {
                _port.Write(cmd, 0, cmd.Length);
                LogDebug("SEND", Encoding.ASCII.GetString(cmd));

                await Task.Delay(300, _cts?.Token ?? CancellationToken.None);

                var rawBytes = ReadRawBytes();
                if (rawBytes.Length > 0)
                {
                    LogDebug("RECV", rawBytes);
                    var text = Encoding.ASCII.GetString(rawBytes);
                    var parsed = ParseRaw(text);
                    if (parsed.Value != 0 || !string.IsNullOrEmpty(parsed.RawData))
                        return parsed;
                }
                else
                {
                    LogDebug("RECV", Array.Empty<byte>());
                }
            }

            return new WeightReading { Timestamp = DateTime.UtcNow };
        }
        catch (OperationCanceledException)
        {
            LogDebug("CANCEL", "ReadWeightAsync cancelled");
            return new WeightReading { Timestamp = DateTime.UtcNow };
        }
        catch (TimeoutException)
        {
            LogDebug("TIMEOUT", "");
            return new WeightReading { Timestamp = DateTime.UtcNow };
        }
        catch (InvalidOperationException) when (_disposed)
        {
            return new WeightReading { Timestamp = DateTime.UtcNow };
        }
        catch (Exception ex) when (!_disposed)
        {
            LogDebug("ERROR", $"{ex.GetType().Name}: {ex.Message}");
            return new WeightReading { Timestamp = DateTime.UtcNow };
        }
        finally
        {
            try { _portLock.Release(); } catch (ObjectDisposedException) { }
        }
    }

    private byte[] ReadRawBytes()
    {
        try
        {
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

    public async Task<bool> TareAsync()
    {
        await _portLock.WaitAsync();
        try
        {
            if (_port?.IsOpen != true || _disposed) return false;
            _port.Write("T\r\n");
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            try { _portLock.Release(); } catch (ObjectDisposedException) { }
        }
    }

    public async Task<bool> ZeroAsync()
    {
        await _portLock.WaitAsync();
        try
        {
            if (_port?.IsOpen != true || _disposed) return false;
            _port.Write("Z\r\n");
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            try { _portLock.Release(); } catch (ObjectDisposedException) { }
        }
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        var interval = _config?.ReadIntervalMs ?? 200;
        var cycle = 0;

        while (!ct.IsCancellationRequested && !_disposed)
        {
            try
            {
                await Task.Delay(interval, ct);
                if (_port?.IsOpen != true || _disposed) continue;

                cycle++;
                var reading = await ReadWeightAsync();

                var isValid = reading.Value != 0 || !string.IsNullOrEmpty(reading.RawData);
                if (!_disposed && isValid)
                    WeightChanged?.Invoke(this, reading);

                LogDebug($"LOOP", $"cycle={cycle} isValid={isValid} val={reading.Value} raw=<{(reading.RawData ?? "").Replace("\r","\\r").Replace("\n","\\n")}>");
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex)
            {
                if (!_disposed)
                    ErrorOccurred?.Invoke(this, $"Error lectura continua: {ex.Message}");

                var msg = $"[{DateTime.Now:HH:mm:ss.fff}] ReadLoop EXCEPTION: {ex.GetType().Name}: {ex.Message}";
                try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "PesaControl_debug.log"), msg + Environment.NewLine); } catch { }
            }
        }

        var exitMsg = $"[{DateTime.Now:HH:mm:ss.fff}] ReadLoop EXITED (cancelled={ct.IsCancellationRequested} disposed={_disposed})";
        try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "PesaControl_debug.log"), exitMsg + Environment.NewLine); } catch { }
    }

    private static void LogDebug(string label, string data)
    {
        var msg = $"[{DateTime.Now:HH:mm:ss.fff}] {label} {data}";
        try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "PesaControl_debug.log"), msg + Environment.NewLine); } catch { }
    }

    private static void LogDebug(string label, byte[] data)
    {
        if (data.Length == 0)
        {
            var emptyMsg = $"[{DateTime.Now:HH:mm:ss.fff}] {label} len=0 (no data)";
            try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "PesaControl_debug.log"), emptyMsg + Environment.NewLine); } catch { }
            return;
        }

        var hex = string.Join(" ", data.Select(b => $"0x{b:X2}"));
        var sb = new StringBuilder(data.Length);
        foreach (var b in data)
        {
            if (b == 0x0D) sb.Append("\\r");
            else if (b == 0x0A) sb.Append("\\n");
            else if (b >= 0x20 && b < 0x7F) sb.Append((char)b);
            else sb.Append('.');
        }

        var msg = $"[{DateTime.Now:HH:mm:ss.fff}] {label} len={data.Length} hex=[{hex}] ascii=<{sb}>";
        try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "PesaControl_debug.log"), msg + Environment.NewLine); } catch { }
    }

    private static WeightReading ParseRaw(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return new WeightReading();

        var lines = raw.Split('\r', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines.Reverse())
        {
            var trimmed = line.Trim().Trim('\n');
            if (trimmed.Length > 0)
                return ParseLine(trimmed);
        }
        return new WeightReading();
    }

    private static WeightReading ParseLine(string line)
    {
        var value = ParseWeightValue(line);

        return new WeightReading
        {
            Value = value,
            IsStable = !line.Contains("UNST", StringComparison.OrdinalIgnoreCase)
                       && !line.Contains("MOT", StringComparison.OrdinalIgnoreCase),
            Timestamp = DateTime.UtcNow,
            RawData = line
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

    public void Dispose()
    {
        _disposed = true;
        _cts?.Cancel();
        _cts?.Dispose();
        _port?.Dispose();
        _portLock.Dispose();
    }
}
