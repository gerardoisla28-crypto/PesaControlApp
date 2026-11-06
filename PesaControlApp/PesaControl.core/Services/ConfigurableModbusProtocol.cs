using PesaControl.Core.Models;

namespace PesaControl.Core.Services;

/// <summary>
/// Modbus RTU / ASCII / TCP con el mapa de registros definido por el usuario (ProtocolOptions):
/// función, dirección, tipo de dato, orden de palabras, divisor, bit de estabilidad y comandos de tara/cero.
/// </summary>
public class ConfigurableModbusProtocol : IWeighingProtocol, IDisposable
{
    private const int ErrorReportThreshold = 5;

    private readonly ModbusTransport _transport;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private CommunicationConfig? _config;
    private ProtocolOptions _options = new();
    private ByteChannel? _channel;
    private ModbusClient? _client;
    private CancellationTokenSource? _cts;
    private WeightReading _lastReading;
    private int _failures;

    public ConfigurableModbusProtocol(ModbusTransport transport) => _transport = transport;

    public string Name => $"Modbus {_transport.ToString().ToUpperInvariant()} (configurable)";
    public bool IsConnected => _channel?.IsOpen ?? false;

    public event EventHandler<WeightReading>? WeightChanged;
    public event EventHandler<bool>? ConnectionStatusChanged;
    public event EventHandler<string>? ErrorOccurred;

    public async Task ConnectAsync(CommunicationConfig config)
    {
        Cleanup();
        _config = config;
        _options = config.Options ?? new ProtocolOptions();
        _failures = 0;
        _lastReading = default;

        try
        {
            _channel = _transport == ModbusTransport.Tcp
                ? new TcpChannel(config.IpAddress, config.TcpPort)
                : new SerialChannel(config, dtrRts: false);
            await _channel.OpenAsync(CancellationToken.None);
            _client = new ModbusClient(_channel, _transport);

            _cts = new CancellationTokenSource();
            _ = PollLoopAsync(_cts.Token);

            Log($"Connect OK {Describe(config)} slave={config.SlaveId} fn={_options.Function} addr={config.RegisterAddress}");
            ConnectionStatusChanged?.Invoke(this, true);
        }
        catch (Exception ex)
        {
            Log($"Connect FAIL {Describe(config)}: {ex.Message}");
            ErrorOccurred?.Invoke(this, $"Error de conexión Modbus ({Describe(config)}): {ex.Message}");
            Cleanup();
            ConnectionStatusChanged?.Invoke(this, false);
        }
    }

    public Task DisconnectAsync()
    {
        Cleanup();
        ConnectionStatusChanged?.Invoke(this, false);
        return Task.CompletedTask;
    }

    public Task<WeightReading> ReadWeightAsync() => Task.FromResult(_lastReading);

    public Task<bool> TareAsync() => WriteCommandAsync(_options.TareValue);
    public Task<bool> ZeroAsync() => WriteCommandAsync(_options.ZeroValue);

    private async Task PollLoopAsync(CancellationToken ct)
    {
        var interval = Math.Max(_config?.ReadIntervalMs ?? 200, 100);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, ct);

                var reading = await QueryAsync(ct);
                if (reading is null)
                {
                    _failures++;
                    if (_failures == ErrorReportThreshold)
                    {
                        var detail = _client?.LastException is > 0 and var code ? $" (excepción Modbus {code})" : "";
                        var msg = $"Sin respuesta Modbus en {Describe(_config!)}{detail}. Verifique esclavo {_config!.SlaveId}, función {_options.Function}, dirección {_config.RegisterAddress}, tipo de dato y parámetros de comunicación.";
                        Log(msg);
                        ErrorOccurred?.Invoke(this, msg);
                    }

                    // En TCP la conexión puede haberse cortado: se reabre cada pocos fallos.
                    if (_transport == ModbusTransport.Tcp && _failures % 3 == 0)
                        await TryReopenAsync(ct);
                    continue;
                }

                _failures = 0;
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

    private async Task<WeightReading?> QueryAsync(CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_client is null || _config is null || _channel?.IsOpen != true) return null;

            var count = (ushort)(_options.DataType is ModbusDataType.Int16 or ModbusDataType.UInt16 ? 1 : 2);
            var regs = await _client.ReadRegistersAsync(_config.SlaveId, (byte)_options.Function, _config.RegisterAddress, count, ct);
            if (regs is null) return null;

            var divisor = _options.Divisor == 0 ? 1m : _options.Divisor;
            var value = Math.Round(Decode(regs, _options) / divisor, 4);

            var stable = true;
            if (_options.StatusRegister >= 0)
            {
                var status = await _client.ReadRegistersAsync(_config.SlaveId, (byte)_options.Function, (ushort)_options.StatusRegister, 1, ct);
                if (status is not null)
                {
                    var bit = (status[0] >> Math.Clamp(_options.StableBit, 0, 15)) & 1;
                    stable = (bit == 1) == _options.StableWhenSet;
                }
            }

            return new WeightReading
            {
                Value = value,
                IsStable = stable,
                Timestamp = DateTime.UtcNow,
                RawData = string.Join(" ", regs.Select(r => r.ToString("X4")))
            };
        }
        finally
        {
            try { _lock.Release(); } catch (ObjectDisposedException) { }
        }
    }

    /// <summary>Convierte los registros leídos en el valor numérico según el tipo de dato y el orden configurados.</summary>
    internal static decimal Decode(ushort[] regs, ProtocolOptions o)
    {
        ushort R(int i)
        {
            var r = regs[i];
            return o.SwapBytes ? (ushort)((r << 8) | (r >> 8)) : r;
        }

        if (o.DataType == ModbusDataType.Int16) return (short)R(0);
        if (o.DataType == ModbusDataType.UInt16) return R(0);

        var hi = o.LowWordFirst ? R(1) : R(0);
        var lo = o.LowWordFirst ? R(0) : R(1);
        var raw = ((uint)hi << 16) | lo;

        return o.DataType switch
        {
            ModbusDataType.Int32 => (int)raw,
            ModbusDataType.UInt32 => raw,
            ModbusDataType.Float32 => (decimal)BitConverter.UInt32BitsToSingle(raw),
            ModbusDataType.SignMagnitude32 => (raw & 0x80000000) != 0 ? -(decimal)(raw & 0x7FFFFFFF) : raw,
            _ => raw
        };
    }

    private async Task<bool> WriteCommandAsync(int value)
    {
        if (_options.CommandRegister < 0) return false;
        try
        {
            await _lock.WaitAsync();
            try
            {
                if (_client is null || _config is null || _channel?.IsOpen != true) return false;
                return await _client.WriteSingleRegisterAsync(_config.SlaveId, (ushort)_options.CommandRegister, (ushort)value, CancellationToken.None);
            }
            finally
            {
                try { _lock.Release(); } catch (ObjectDisposedException) { }
            }
        }
        catch
        {
            return false;
        }
    }

    private async Task TryReopenAsync(CancellationToken ct)
    {
        try
        {
            await _lock.WaitAsync(ct);
            try
            {
                if (_channel is null) return;
                await _channel.OpenAsync(ct);
                _client = new ModbusClient(_channel, _transport);
            }
            finally
            {
                try { _lock.Release(); } catch (ObjectDisposedException) { }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log($"Reabrir TCP falló: {ex.Message}");
        }
    }

    private string Describe(CommunicationConfig c)
        => _transport == ModbusTransport.Tcp ? $"{c.IpAddress}:{c.TcpPort}" : $"{c.PortName} {c.BaudRate} baud";

    private void Cleanup()
    {
        try { _cts?.Cancel(); } catch { }
        _cts?.Dispose();
        _cts = null;
        _channel?.Close();
        _channel = null;
        _client = null;
    }

    private static void Log(string message)
    {
        try
        {
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "PesaControl_debug.log"),
                $"[{DateTime.Now:HH:mm:ss.fff}] [ModbusCfg] {message}{Environment.NewLine}");
        }
        catch { }
    }

    public void Dispose()
    {
        Cleanup();
        _lock.Dispose();
    }
}
