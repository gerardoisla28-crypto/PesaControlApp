using System.IO.Ports;
using System.Net.Sockets;
using PesaControl.Core.Models;

namespace PesaControl.Core.Services;

public class ModbusTrafficLightService : ITrafficLightService, IDisposable
{
    private SerialPort? _serial;
    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private TrafficLightConfig _config = new();
    private bool _disposed;

    public string Name => _config.Name;
    public bool IsConnected => _serial?.IsOpen == true || _stream != null;

    public event EventHandler<bool>? ConnectionStatusChanged;
    public event EventHandler<string>? ErrorOccurred;

    public Task ConnectAsync(TrafficLightConfig config)
    {
        DisconnectAsync();
        _config = config;

        try
        {
            if (config.ConnectionType == DeviceConnectionType.ModbusTCP)
            {
                _tcpClient = new TcpClient(config.IpAddress, config.TcpPort);
                _stream = _tcpClient.GetStream();
            }
            else
            {
                _serial = new SerialPort(config.PortName, config.BaudRate)
                {
                    ReadTimeout = 1000,
                    WriteTimeout = 1000
                };
                _serial.Open();
            }

            ConnectionStatusChanged?.Invoke(this, true);
        }
        catch (Exception ex)
        {
            var msg = $"Error al conectar semáforo Modbus: {ex.Message}";
            ErrorOccurred?.Invoke(this, msg);
            throw new InvalidOperationException(msg);
        }

        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        _stream?.Dispose();
        _stream = null;
        _tcpClient?.Dispose();
        _tcpClient = null;

        if (_serial?.IsOpen == true)
            _serial.Close();
        _serial?.Dispose();
        _serial = null;

        ConnectionStatusChanged?.Invoke(this, false);
        return Task.CompletedTask;
    }

    public async Task SetColorAsync(TrafficLightColor color)
    {
        var value = color switch
        {
            TrafficLightColor.Red => _config.RedValue,
            TrafficLightColor.Yellow => _config.YellowValue,
            TrafficLightColor.Green => _config.GreenValue,
            TrafficLightColor.Off => _config.OffValue,
            _ => _config.OffValue
        };

        var request = ModbusHelper.BuildWriteSingleRegisterRequest(_config.SlaveId, _config.RegisterAddress, value);
        await SendAsync(request);
        await Task.Delay(50);
    }

    public async Task<TrafficLightColor> GetColorAsync()
    {
        var request = ModbusHelper.BuildReadHoldingRegistersRequest(_config.SlaveId, _config.RegisterAddress, 1);
        await SendAsync(request);

        var response = _stream != null
            ? ModbusHelper.ReadTcpResponse(_stream)
            : ModbusHelper.ReadResponse(_serial!, 7);

        if (ModbusHelper.TryReadHoldingRegisters(response, out var registers) && registers.Length > 0)
        {
            var value = registers[0];
            if (value == _config.RedValue) return TrafficLightColor.Red;
            if (value == _config.YellowValue) return TrafficLightColor.Yellow;
            if (value == _config.GreenValue) return TrafficLightColor.Green;
        }

        return TrafficLightColor.Off;
    }

    private async Task SendAsync(byte[] data)
    {
        if (_serial?.IsOpen == true)
        {
            await _serial.BaseStream.WriteAsync(data);
        }
        else if (_stream != null)
        {
            var tcpData = ModbusHelper.BuildTcpMbap(_config.SlaveId, data[..^2]);
            await _stream.WriteAsync(tcpData);
        }
        else
        {
            var msg = "Semáforo Modbus no conectado";
            ErrorOccurred?.Invoke(this, msg);
            throw new InvalidOperationException(msg);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stream?.Dispose();
        _tcpClient?.Dispose();
        _serial?.Dispose();
    }
}
