using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using PesaControl.Core.Models;

namespace PesaControl.Core.Services;

/// <summary>
/// Visores de texto (continuo o con comando de consulta) por puerto serie/USB o por TCP.
/// El formato lo define el usuario: terminador de trama, expresión regular del peso, divisor,
/// patrón de inestable y comandos de consulta/tara/cero.
/// </summary>
public class ConfigurableAsciiProtocol : IWeighingProtocol, IDisposable
{
    private const int MaxBuffer = 4096;
    private const int ErrorReportMs = 5000;

    private readonly bool _tcp;
    private readonly StringBuilder _buffer = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private CommunicationConfig? _config;
    private ProtocolOptions _options = new();
    private ByteChannel? _channel;
    private CancellationTokenSource? _cts;
    private Regex? _weight;
    private Regex? _unstable;
    private string _terminator = "\r";
    private WeightReading _lastReading;
    private DateTime _lastFrame;
    private bool _noDataReported;

    public ConfigurableAsciiProtocol(bool tcp) => _tcp = tcp;

    public string Name => _tcp ? "ASCII TCP (configurable)" : "ASCII serie/USB (configurable)";
    public bool IsConnected => _channel?.IsOpen ?? false;

    public event EventHandler<WeightReading>? WeightChanged;
    public event EventHandler<bool>? ConnectionStatusChanged;
    public event EventHandler<string>? ErrorOccurred;

    public async Task ConnectAsync(CommunicationConfig config)
    {
        Cleanup();
        _config = config;
        _options = config.Options ?? new ProtocolOptions();
        _lastReading = default;
        _lastFrame = DateTime.UtcNow;
        _noDataReported = false;
        _buffer.Clear();

        try
        {
            var timeout = TimeSpan.FromMilliseconds(100);
            _weight = new Regex(string.IsNullOrWhiteSpace(_options.WeightPattern) ? ProtocolOptions.DefaultWeightPattern : _options.WeightPattern,
                RegexOptions.CultureInvariant, timeout);
            _unstable = string.IsNullOrWhiteSpace(_options.UnstablePattern)
                ? null
                : new Regex(_options.UnstablePattern, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, timeout);

            var term = ProtocolOptions.DecodeEscapes(_options.Terminator);
            _terminator = term.Length == 0 ? "\n" : term;

            _channel = _tcp
                ? new TcpChannel(config.IpAddress, config.TcpPort)
                : new SerialChannel(config, dtrRts: true);
            await _channel.OpenAsync(CancellationToken.None);

            _cts = new CancellationTokenSource();
            _ = ReadLoopAsync(_cts.Token);

            Log($"Connect OK {Describe(config)} terminator={Escape(_terminator)} poll='{_options.PollCommand}'");
            ConnectionStatusChanged?.Invoke(this, true);
        }
        catch (Exception ex)
        {
            Log($"Connect FAIL {Describe(config)}: {ex.Message}");
            ErrorOccurred?.Invoke(this, $"Error de conexión ASCII ({Describe(config)}): {ex.Message}");
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

    public Task<bool> TareAsync() => SendAsync(_options.TareCommand);
    public Task<bool> ZeroAsync() => SendAsync(_options.ZeroCommand);

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        var interval = Math.Max(_config?.ReadIntervalMs ?? 200, 100);
        var poll = ProtocolOptions.DecodeEscapes(_options.PollCommand);
        var chunk = new byte[512];

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (poll.Length > 0)
                {
                    await SendRawAsync(poll, ct);
                    await Task.Delay(interval, ct);
                }

                var n = await _channel!.ReadAsync(chunk, 0, chunk.Length, poll.Length > 0 ? 300 : 250, ct);
                if (n > 0)
                    Process(Encoding.Latin1.GetString(chunk, 0, n));

                if (!_noDataReported && (DateTime.UtcNow - _lastFrame).TotalMilliseconds > ErrorReportMs)
                {
                    _noDataReported = true;
                    var msg = $"Sin datos válidos en {Describe(_config!)}. Revise baudios, terminador, patrón de peso{(poll.Length > 0 ? " y comando de consulta" : "")}, y que el equipo transmita.";
                    Log(msg);
                    ErrorOccurred?.Invoke(this, msg);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex)
            {
                Log($"Read EXCEPTION {ex.GetType().Name}: {ex.Message}");
                try { await Task.Delay(500, ct); } catch (OperationCanceledException) { break; }
            }
        }
    }

    private void Process(string incoming)
    {
        _buffer.Append(incoming);
        if (_buffer.Length > MaxBuffer)
            _buffer.Remove(0, _buffer.Length - MaxBuffer);

        var text = _buffer.ToString();
        var parts = text.Split(_terminator);
        _buffer.Clear().Append(parts[^1]);

        foreach (var frame in parts.Take(parts.Length - 1))
        {
            var clean = frame.Trim('\r', '\n');
            if (clean.Length == 0) continue;
            ParseFrame(clean);
        }
    }

    private void ParseFrame(string frame)
    {
        Match m;
        try { m = _weight!.Match(frame); }
        catch (RegexMatchTimeoutException) { return; }
        if (!m.Success) return;

        var number = Regex.Replace(m.Value, @"\s+", "").Replace(',', '.');
        if (!decimal.TryParse(number, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var raw))
            return;

        var divisor = _options.Divisor == 0 ? 1m : _options.Divisor;

        var unstable = false;
        if (_unstable is not null)
        {
            try { unstable = _unstable.IsMatch(frame); }
            catch (RegexMatchTimeoutException) { }
        }

        var reading = new WeightReading
        {
            Value = raw / divisor,
            IsStable = !unstable,
            Timestamp = DateTime.UtcNow,
            RawData = frame
        };

        _lastReading = reading;
        _lastFrame = DateTime.UtcNow;
        _noDataReported = false;
        WeightChanged?.Invoke(this, reading);
    }

    private async Task<bool> SendAsync(string? command)
    {
        var text = ProtocolOptions.DecodeEscapes(command);
        if (text.Length == 0) return false;
        try
        {
            await SendRawAsync(text, CancellationToken.None);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task SendRawAsync(string text, CancellationToken ct)
    {
        if (_channel?.IsOpen != true) return;
        await _writeLock.WaitAsync(ct);
        try
        {
            await _channel.WriteAsync(Encoding.Latin1.GetBytes(text), ct);
        }
        finally
        {
            try { _writeLock.Release(); } catch (ObjectDisposedException) { }
        }
    }

    private string Describe(CommunicationConfig c)
        => _tcp ? $"{c.IpAddress}:{c.TcpPort}" : $"{c.PortName} {c.BaudRate} baud";

    private static string Escape(string s) => s.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");

    private void Cleanup()
    {
        try { _cts?.Cancel(); } catch { }
        _cts?.Dispose();
        _cts = null;
        _channel?.Close();
        _channel = null;
    }

    private static void Log(string message)
    {
        try
        {
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "PesaControl_debug.log"),
                $"[{DateTime.Now:HH:mm:ss.fff}] [AsciiCfg] {message}{Environment.NewLine}");
        }
        catch { }
    }

    public void Dispose()
    {
        Cleanup();
        _writeLock.Dispose();
    }
}
