using System.IO.Ports;
using PesaControl.Core.Models;

namespace PesaControl.Core.Services;

public class ModbusRtuProtocol : IWeighingProtocol, IDisposable
{
    private SerialPort? _port;
    private CommunicationConfig? _config;
    private CancellationTokenSource? _cts;

    public string Name => "Modbus RTU";
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
                WriteTimeout = 1000
            };
            _port.Open();
            _cts = new CancellationTokenSource();
            _ = ReadLoopAsync(_cts.Token);
            ConnectionStatusChanged?.Invoke(this, true);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Error Modbus RTU: {ex.Message}");
            ConnectionStatusChanged?.Invoke(this, false);
        }
        await Task.CompletedTask;
    }

    public async Task DisconnectAsync()
    {
        _cts?.Cancel();

        await Task.Run(() =>
        {
            _port?.Close();
            _port?.Dispose();
        });

        ConnectionStatusChanged?.Invoke(this, false);
    }

    public async Task<WeightReading> ReadWeightAsync()
    {
        try
        {
            if (_port?.IsOpen != true || _config == null)
                return new WeightReading();

            var request = BuildReadHoldingRegistersRequest(
                _config.SlaveId, _config.RegisterAddress, 2);

            _port.DiscardInBuffer();
            _port.Write(request, 0, request.Length);
            await Task.Delay(50);

            var response = new byte[9];
            var read = 0;
            try
            {
                while (read < response.Length)
                    read += _port.Read(response, read, response.Length - read);
            }
            catch (TimeoutException) { }

            if (read < 9 || response[1] != 0x03)
                return new WeightReading();

            var raw = BitConverter.ToString(response);
            var bytes = new[] { response[3], response[4], response[5], response[6] };
            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes);
            var value = BitConverter.ToSingle(bytes, 0);

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
            ErrorOccurred?.Invoke(this, $"Error lectura RTU: {ex.Message}");
            return new WeightReading();
        }
    }

    public Task<bool> TareAsync() => Task.FromResult(false);
    public Task<bool> ZeroAsync() => Task.FromResult(false);

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
                ErrorOccurred?.Invoke(this, $"Error loop RTU: {ex.Message}");
            }
        }
    }

    private static byte[] BuildReadHoldingRegistersRequest(byte slaveId, ushort startAddress, ushort count)
    {
        var request = new byte[8];
        request[0] = slaveId;
        request[1] = 0x03;
        request[2] = (byte)(startAddress >> 8);
        request[3] = (byte)(startAddress & 0xFF);
        request[4] = (byte)(count >> 8);
        request[5] = (byte)(count & 0xFF);
        var crc = CalculateCrc(request, 6);
        request[6] = (byte)(crc & 0xFF);
        request[7] = (byte)(crc >> 8);
        return request;
    }

    private static ushort CalculateCrc(byte[] data, int length)
    {
        ushort crc = 0xFFFF;
        for (var i = 0; i < length; i++)
        {
            crc ^= data[i];
            for (var j = 0; j < 8; j++)
            {
                if ((crc & 0x0001) != 0)
                {
                    crc >>= 1;
                    crc ^= 0xA001;
                }
                else
                {
                    crc >>= 1;
                }
            }
        }
        return crc;
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _port?.Dispose();
    }
}
