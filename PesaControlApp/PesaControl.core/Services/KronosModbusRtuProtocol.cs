using System.IO.Ports;
using PesaControl.Core.Models;

namespace PesaControl.Core.Services;

/// <summary>
/// Sensocar Kronos por Modbus RTU (RS-232/RS-485). En el visor: COM > RS-232 > PC > PROTOC = MOD.RTU.
/// Mapa según KRONOS_Modbus_ES.pdf:
///   Registros de entrada (FC 04), dirección = registro - 30001:
///     30010-30011 peso neto, 30012-30013 bruto, 30014-30015 tara (Long: bit31 = signo, resto = peso x 1000),
///     30016 flags de estado (bits 8-10 = decimales).
///   Registro de comando (FC 06) 41001: 1 = cero, 2 = tara, 6 = quitar tara.
/// </summary>
public class KronosModbusRtuProtocol : IWeighingProtocol, IDisposable
{
    private const ushort WeightStartAddress = 9;   // 30010
    private const ushort WeightRegisterCount = 7;  // 30010..30016
    private const ushort CommandAddress = 1000;    // 41001
    private const ushort CmdZero = 1;
    private const ushort CmdTare = 2;

    private const int WeightResponseLength = 5 + WeightRegisterCount * 2; // id+fc+bytecount+datos+crc
    private const int ErrorReportThreshold = 5;

    private SerialPort? _port;
    private CommunicationConfig? _config;
    private CancellationTokenSource? _cts;
    private volatile bool _disposed;
    private readonly SemaphoreSlim _portLock = new(1, 1);
    private WeightReading _lastReading;
    private int _consecutiveFailures;

    public string Name => "Kronos Modbus RTU";
    public bool IsConnected => _port?.IsOpen ?? false;

    public event EventHandler<WeightReading>? WeightChanged;
    public event EventHandler<bool>? ConnectionStatusChanged;
    public event EventHandler<string>? ErrorOccurred;

    public Task ConnectAsync(CommunicationConfig config)
    {
        Cleanup();
        _disposed = false;
        _config = config;
        _consecutiveFailures = 0;
        _lastReading = default;

        try
        {
            _port = new SerialPort(config.PortName, config.BaudRate,
                (Parity)config.Parity, config.DataBits, (StopBits)config.StopBits)
            {
                ReadTimeout = 300,
                WriteTimeout = 500
            };
            _port.Open();
            _cts = new CancellationTokenSource();
            _ = PollLoopAsync(_cts.Token);

            Log($"Connect OK port={config.PortName} baud={config.BaudRate} slave={config.SlaveId}");
            ConnectionStatusChanged?.Invoke(this, true);
        }
        catch (Exception ex)
        {
            Log($"Connect FAIL port={config.PortName}: {ex.Message}");
            ErrorOccurred?.Invoke(this, $"Error conexión Kronos Modbus ({config.PortName}): {ex.Message}");
            ConnectionStatusChanged?.Invoke(this, false);
        }
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        Cleanup();
        ConnectionStatusChanged?.Invoke(this, false);
        return Task.CompletedTask;
    }

    public Task<WeightReading> ReadWeightAsync() => Task.FromResult(_lastReading);

    public Task<bool> TareAsync() => WriteCommandAsync(CmdTare);
    public Task<bool> ZeroAsync() => WriteCommandAsync(CmdZero);

    private async Task PollLoopAsync(CancellationToken ct)
    {
        var interval = Math.Max(_config?.ReadIntervalMs ?? 200, 100);

        while (!ct.IsCancellationRequested && !_disposed)
        {
            try
            {
                await Task.Delay(interval, ct);
                if (_port?.IsOpen != true) continue;

                var reading = await QueryWeightAsync(ct);
                if (reading is null)
                {
                    if (++_consecutiveFailures == ErrorReportThreshold)
                        ErrorOccurred?.Invoke(this,
                            $"Kronos sin respuesta en {_config?.PortName} (esclavo {_config?.SlaveId}, {_config?.BaudRate} baud). Verifique PROTOC=MOD.RTU, número de esclavo y que ningún otro programa use el puerto.");
                    continue;
                }

                _consecutiveFailures = 0;
                _lastReading = reading.Value;
                WeightChanged?.Invoke(this, reading.Value);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex)
            {
                Log($"Poll EXCEPTION {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    private async Task<WeightReading?> QueryWeightAsync(CancellationToken ct)
    {
        await _portLock.WaitAsync(ct);
        try
        {
            if (_port?.IsOpen != true || _config is null) return null;

            var request = BuildRequest(_config.SlaveId, 0x04, WeightStartAddress, WeightRegisterCount);
            _port.DiscardInBuffer();
            _port.Write(request, 0, request.Length);

            var response = ReadExactly(WeightResponseLength);
            if (response is null) return null;

            if (response[0] != _config.SlaveId || response[1] != 0x04 || response[2] != WeightRegisterCount * 2)
                return null;
            if (!CrcOk(response)) return null;

            var rawNet = ((uint)response[3] << 24) | ((uint)response[4] << 16) | ((uint)response[5] << 8) | response[6];
            var flags = (ushort)((response[15] << 8) | response[16]);

            var negative = (rawNet & 0x80000000) != 0;
            var value = (rawNet & 0x7FFFFFFF) / 1000m;
            if (negative) value = -value;

            // Sobrecarga (bit 5) o error de célula (bit 7) → lectura no válida como peso estable.
            var fault = (flags & 0x20) != 0 || (flags & 0x80) != 0;

            // El flag de estabilidad (bit 0) del equipo no se usa: WeighingService ya decide la
            // estabilidad con su ventana de lecturas y tolerancia.
            return new WeightReading
            {
                Value = value,
                IsStable = !fault,
                Timestamp = DateTime.UtcNow,
                RawData = $"net={rawNet:X8} flags={flags:X4}"
            };
        }
        finally
        {
            try { _portLock.Release(); } catch (ObjectDisposedException) { }
        }
    }

    private async Task<bool> WriteCommandAsync(ushort command)
    {
        try
        {
            await _portLock.WaitAsync();
            try
            {
                if (_port?.IsOpen != true || _config is null) return false;

                var request = BuildRequest(_config.SlaveId, 0x06, CommandAddress, command);
                _port.DiscardInBuffer();
                _port.Write(request, 0, request.Length);

                // FC 06: la respuesta repite la petición (8 bytes).
                var response = ReadExactly(8);
                return response is not null && CrcOk(response) && response[1] == 0x06;
            }
            finally
            {
                try { _portLock.Release(); } catch (ObjectDisposedException) { }
            }
        }
        catch
        {
            return false;
        }
    }

    private byte[]? ReadExactly(int length)
    {
        var buf = new byte[length];
        var read = 0;
        try
        {
            while (read < length)
            {
                var n = _port!.Read(buf, read, length - read);
                if (n <= 0) break;
                read += n;
            }
        }
        catch (TimeoutException) { }

        return read == length ? buf : null;
    }

    private static byte[] BuildRequest(byte slave, byte function, ushort address, ushort valueOrCount)
    {
        var req = new byte[8];
        req[0] = slave;
        req[1] = function;
        req[2] = (byte)(address >> 8);
        req[3] = (byte)(address & 0xFF);
        req[4] = (byte)(valueOrCount >> 8);
        req[5] = (byte)(valueOrCount & 0xFF);
        var crc = Crc16(req, 6);
        req[6] = (byte)(crc & 0xFF);
        req[7] = (byte)(crc >> 8);
        return req;
    }

    private static bool CrcOk(byte[] frame)
    {
        var crc = Crc16(frame, frame.Length - 2);
        return frame[^2] == (byte)(crc & 0xFF) && frame[^1] == (byte)(crc >> 8);
    }

    private static ushort Crc16(byte[] data, int length)
    {
        ushort crc = 0xFFFF;
        for (var i = 0; i < length; i++)
        {
            crc ^= data[i];
            for (var j = 0; j < 8; j++)
                crc = (crc & 1) != 0 ? (ushort)((crc >> 1) ^ 0xA001) : (ushort)(crc >> 1);
        }
        return crc;
    }

    private void Cleanup()
    {
        _disposed = true;
        try { _cts?.Cancel(); } catch { }
        _cts?.Dispose();
        _cts = null;

        if (_port != null)
        {
            try { if (_port.IsOpen) _port.Close(); } catch { }
            _port.Dispose();
            _port = null;
        }
    }

    private static void Log(string message)
    {
        try
        {
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "PesaControl_debug.log"),
                $"[{DateTime.Now:HH:mm:ss.fff}] [KronosModbus] {message}{Environment.NewLine}");
        }
        catch { }
    }

    public void Dispose()
    {
        Cleanup();
        _portLock.Dispose();
    }
}
