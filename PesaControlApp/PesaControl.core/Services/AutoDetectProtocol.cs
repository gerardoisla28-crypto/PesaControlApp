using PesaControl.Core.Models;

namespace PesaControl.Core.Services;

public class AutoDetectProtocol : IWeighingProtocol, IDisposable
{
    private readonly List<(IWeighingProtocol Protocol, string Name)> _candidates;
    private IWeighingProtocol? _active;
    private CommunicationConfig? _config;
    private EventHandler<WeightReading>? _weightChangedHandler;
    private EventHandler<bool>? _connectionHandler;
    private EventHandler<string>? _errorHandler;

    public string Name => _active?.Name ?? "Auto-Detect";
    public bool IsConnected => _active?.IsConnected ?? false;

    public event EventHandler<WeightReading>? WeightChanged;
    public event EventHandler<bool>? ConnectionStatusChanged;
    public event EventHandler<string>? ErrorOccurred;

    public AutoDetectProtocol()
    {
        _candidates =
        [
            (new SerialProtocol(), "Serial"),
            (new ModbusTcpProtocol(), "Modbus TCP"),
            (new ModbusRtuProtocol(), "Modbus RTU")
        ];
    }

    public async Task ConnectAsync(CommunicationConfig config)
    {
        _config = config;
        foreach (var (protocol, name) in _candidates)
        {
            try
            {
                ErrorOccurred?.Invoke(this, $"Probando {name}...");
                await protocol.ConnectAsync(config);
                await Task.Delay(500);

                var test = await protocol.ReadWeightAsync();
                if (test.Value > 0 || !string.IsNullOrEmpty(test.RawData))
                {
                    _active = protocol;
                    WireEvents(_active);
                    ConnectionStatusChanged?.Invoke(this, true);
                    ErrorOccurred?.Invoke(this, $"Conectado via {name}");
                    return;
                }
                await protocol.DisconnectAsync();
            }
            catch
            {
                await protocol.DisconnectAsync();
            }
        }
        ErrorOccurred?.Invoke(this, "No se detectó ningún protocolo activo");
        ConnectionStatusChanged?.Invoke(this, false);
    }

    public async Task DisconnectAsync()
    {
        if (_active != null)
        {
            UnwireEvents(_active);
            await _active.DisconnectAsync();
            _active = null;
        }
        ConnectionStatusChanged?.Invoke(this, false);
    }

    public async Task<WeightReading> ReadWeightAsync()
    {
        if (_active != null)
            return await _active.ReadWeightAsync();
        return new WeightReading();
    }

    public Task<bool> TareAsync() => _active?.TareAsync() ?? Task.FromResult(false);
    public Task<bool> ZeroAsync() => _active?.ZeroAsync() ?? Task.FromResult(false);

    private void WireEvents(IWeighingProtocol protocol)
    {
        _weightChangedHandler = (s, r) => WeightChanged?.Invoke(s, r);
        _connectionHandler = (s, c) => ConnectionStatusChanged?.Invoke(s, c);
        _errorHandler = (s, e) => ErrorOccurred?.Invoke(s, e);
        protocol.WeightChanged += _weightChangedHandler;
        protocol.ConnectionStatusChanged += _connectionHandler;
        protocol.ErrorOccurred += _errorHandler;
    }

    private void UnwireEvents(IWeighingProtocol protocol)
    {
        if (_weightChangedHandler != null)
        {
            protocol.WeightChanged -= _weightChangedHandler;
            _weightChangedHandler = null;
        }
        if (_connectionHandler != null)
        {
            protocol.ConnectionStatusChanged -= _connectionHandler;
            _connectionHandler = null;
        }
        if (_errorHandler != null)
        {
            protocol.ErrorOccurred -= _errorHandler;
            _errorHandler = null;
        }
    }

    public void Dispose()
    {
        foreach (var (p, _) in _candidates)
            (p as IDisposable)?.Dispose();
    }
}
