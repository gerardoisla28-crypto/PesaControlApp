using PesaControl.Core.Models;

namespace PesaControl.Core.Services;

/// <summary>
/// Báscula virtual para demostraciones y pruebas sin hardware. Repite el ciclo de un camión:
/// báscula vacía, subida del peso, peso estable, bajada y vacía de nuevo.
/// </summary>
public class SimulatorProtocol : IWeighingProtocol, IDisposable
{
    private enum Phase { Empty, Rising, Stable, Falling }

    private readonly Random _random = new();
    private CancellationTokenSource? _cts;
    private Phase _phase = Phase.Empty;
    private int _ticksInPhase;
    private decimal _target;
    private decimal _current;
    private decimal _offset;

    public string Name => "Simulador (demostración)";
    public bool IsConnected { get; private set; }

    public event EventHandler<WeightReading>? WeightChanged;
    public event EventHandler<bool>? ConnectionStatusChanged;
    public event EventHandler<string>? ErrorOccurred;

    public Task ConnectAsync(CommunicationConfig config)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        IsConnected = true;
        ConnectionStatusChanged?.Invoke(this, true);
        _ = RunAsync(Math.Clamp(config.ReadIntervalMs, 50, 1000), _cts.Token);
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        _cts?.Cancel();
        if (IsConnected)
        {
            IsConnected = false;
            ConnectionStatusChanged?.Invoke(this, false);
        }
        return Task.CompletedTask;
    }

    public Task<WeightReading> ReadWeightAsync() => Task.FromResult(BuildReading());

    // Tara y cero desplazan la lectura, como en una báscula real.
    public Task<bool> TareAsync()
    {
        _offset = _current;
        return Task.FromResult(true);
    }

    public Task<bool> ZeroAsync()
    {
        _offset = 0;
        return Task.FromResult(true);
    }

    private async Task RunAsync(int intervalMs, CancellationToken token)
    {
        var ticksPerSecond = 1000m / intervalMs;
        try
        {
            while (!token.IsCancellationRequested)
            {
                Step(ticksPerSecond);
                WeightChanged?.Invoke(this, BuildReading());
                await Task.Delay(intervalMs, token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Error del simulador: {ex.Message}");
        }
    }

    private void Step(decimal ticksPerSecond)
    {
        _ticksInPhase++;
        var seconds = _ticksInPhase / ticksPerSecond;

        switch (_phase)
        {
            case Phase.Empty when seconds >= 4:
                // Camión cargado entre 28 y 46 toneladas, redondeado a 10 kg.
                _target = Math.Round(_random.Next(28000, 46000) / 10m) * 10m;
                GoTo(Phase.Rising);
                break;
            case Phase.Rising:
                _current += (_target - _current) * 0.12m + 40m;
                if (_current >= _target - 20m)
                {
                    _current = _target;
                    GoTo(Phase.Stable);
                }
                break;
            case Phase.Stable when seconds >= 20:
                GoTo(Phase.Falling);
                break;
            case Phase.Falling:
                _current -= _current * 0.15m + 60m;
                if (_current <= 5m)
                {
                    _current = 0;
                    GoTo(Phase.Empty);
                }
                break;
        }
    }

    private void GoTo(Phase phase)
    {
        _phase = phase;
        _ticksInPhase = 0;
    }

    private WeightReading BuildReading()
    {
        var moving = _phase is Phase.Rising or Phase.Falling;
        // Ruido de ±1 kg al reposo y de ±15 kg mientras el camión se mueve.
        var noise = moving ? _random.Next(-15, 16) : _random.Next(-1, 2);
        var value = Math.Max(0, _current - _offset + (_current > 0 ? noise : 0));
        return new WeightReading
        {
            Value = Math.Round(value),
            IsStable = !moving,
            Timestamp = DateTime.Now,
            RawData = $"SIM {value:0}"
        };
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
