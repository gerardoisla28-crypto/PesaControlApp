using NModbus;
using PesaControl.Core.Models;
using System.Net.Sockets;

namespace PesaControl.Core.Services;

public class ModbusTcpProtocol : IWeighingProtocol, IDisposable
{
    private TcpClient? _client;
    private IModbusMaster? _master;
    private CommunicationConfig? _config;
    private CancellationTokenSource? _cts;

    public string Name => "Modbus TCP/IP";
    public bool IsConnected => _client?.Connected ?? false;

    public event EventHandler<WeightReading>? WeightChanged;
    public event EventHandler<bool>? ConnectionStatusChanged;
    public event EventHandler<string>? ErrorOccurred;

    public async Task ConnectAsync(CommunicationConfig config)
    {
        _config = config;
        try
        {
            _client = new TcpClient();
            await _client.ConnectAsync(config.IpAddress, config.TcpPort);
            var factory = new ModbusFactory();
            _master = factory.CreateMaster(_client);
            _cts = new CancellationTokenSource();
            _ = ReadLoopAsync(_cts.Token);
            ConnectionStatusChanged?.Invoke(this, true);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Error Modbus TCP: {ex.Message}");
            ConnectionStatusChanged?.Invoke(this, false);
        }
    }

    public async Task DisconnectAsync()
    {
        _cts?.Cancel();

        await Task.Run(() =>
        {
            _master = null;
            _client?.Close();
            _client?.Dispose();
        });

        ConnectionStatusChanged?.Invoke(this, false);
    }

    public async Task<WeightReading> ReadWeightAsync()
    {
        try
        {
            if (_master == null || _config == null)
                return new WeightReading();

            var registers = await _master.ReadHoldingRegistersAsync(
                _config.SlaveId, _config.RegisterAddress, 2);

            var raw = string.Join(",", registers);
            var bytes = new byte[4];
            bytes[0] = (byte)(registers[0] >> 8);
            bytes[1] = (byte)(registers[0] & 0xFF);
            bytes[2] = (byte)(registers[1] >> 8);
            bytes[3] = (byte)(registers[1] & 0xFF);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes);
            var value = BitConverter.ToSingle(bytes);

            return new WeightReading
            {
                Value = Math.Round((decimal)value, 3),
                IsStable = true,
                Timestamp = DateTime.UtcNow,
                RawData = raw
            };
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Error lectura Modbus: {ex.Message}");
            return new WeightReading();
        }
    }

    public async Task<bool> TareAsync()
    {
        try
        {
            if (_master == null || _config == null)
                return false;
            await _master.WriteSingleRegisterAsync(_config.SlaveId, 1, 1);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> ZeroAsync()
    {
        try
        {
            if (_master == null || _config == null)
                return false;
            await _master.WriteSingleRegisterAsync(_config.SlaveId, 1, 2);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        var interval = _config?.ReadIntervalMs ?? 200;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, ct);
                var reading = await ReadWeightAsync();
                WeightChanged?.Invoke(this, reading);
            }
            catch (TaskCanceledException) { break; }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, $"Error loop: {ex.Message}");
            }
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _client?.Dispose();
    }
}
