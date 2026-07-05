using PesaControl.Core.Models;

namespace PesaControl.Core.Services;

public class WeighingService
{
    private IWeighingProtocol _protocol;
    private CommunicationConfig _config;
    private readonly List<decimal> _readingsWindow = new();
    private EventHandler<bool>? _connectionHandler;
    private EventHandler<string>? _errorHandler;

    public IWeighingProtocol Protocol => _protocol;
    public bool IsConnected => _protocol.IsConnected;
    public decimal CurrentWeight { get; private set; }
    public bool IsStable { get; private set; }
    public WeightReading? LastReading { get; private set; }
    public int StableWindowSize { get; set; } = 5;
    public decimal ToleranceKg { get; set; } = 2m;

    public event EventHandler<decimal>? WeightUpdated;
    public event EventHandler<bool>? StabilityChanged;
    public event EventHandler<string>? ErrorOccurred;
    public event EventHandler<bool>? ConnectionChanged;

    public WeighingService(IWeighingProtocol protocol, CommunicationConfig config)
    {
        _protocol = protocol;
        _config = config;
        WireEvents();
    }

    public async Task SetProtocol(IWeighingProtocol protocol, CommunicationConfig config)
    {
        var oldProtocol = _protocol;

        UnwireEvents();

        if (oldProtocol.IsConnected)
            await oldProtocol.DisconnectAsync();

        (oldProtocol as IDisposable)?.Dispose();

        _protocol = protocol;
        _config = config;
        WireEvents();
    }

    public async Task ConnectAsync() => await _protocol.ConnectAsync(_config);
    public Task DisconnectAsync() => _protocol.DisconnectAsync();
    public Task<bool> TareAsync() => _protocol.TareAsync();
    public Task<bool> ZeroAsync() => _protocol.ZeroAsync();
    public async Task RequestWeightAsync()
    {
        var reading = await _protocol.ReadWeightAsync();
        if (reading.Value != 0 || !string.IsNullOrEmpty(reading.RawData))
            OnWeightChanged(this, reading);
    }

    private void WireEvents()
    {
        _protocol.WeightChanged += OnWeightChanged;
        _connectionHandler = (s, c) => ConnectionChanged?.Invoke(this, c);
        _errorHandler = (s, e) => ErrorOccurred?.Invoke(this, e);
        _protocol.ConnectionStatusChanged += _connectionHandler;
        _protocol.ErrorOccurred += _errorHandler;
    }

    private void UnwireEvents()
    {
        _protocol.WeightChanged -= OnWeightChanged;
        if (_connectionHandler != null)
        {
            _protocol.ConnectionStatusChanged -= _connectionHandler;
            _connectionHandler = null;
        }
        if (_errorHandler != null)
        {
            _protocol.ErrorOccurred -= _errorHandler;
            _errorHandler = null;
        }
    }

    private void OnWeightChanged(object? sender, WeightReading reading)
    {
        var isValid = reading.Value != 0 || !string.IsNullOrEmpty(reading.RawData);
        LastReading = isValid ? reading : LastReading;
        if (!isValid) return;

        CurrentWeight = reading.Value;
        WeightUpdated?.Invoke(this, CurrentWeight);

        _readingsWindow.Add(CurrentWeight);
        if (_readingsWindow.Count > StableWindowSize)
            _readingsWindow.RemoveAt(0);

        if (_readingsWindow.Count < 2) return;

        var avg = _readingsWindow.Average();
        var maxDev = _readingsWindow.Max() - _readingsWindow.Min();
        var stable = maxDev <= ToleranceKg && reading.IsStable;

        if (stable != IsStable)
        {
            IsStable = stable;
            StabilityChanged?.Invoke(this, IsStable);
        }
    }
}
