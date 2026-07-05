using System.IO.Ports;
using System.Net.Sockets;
using PesaControl.Core.Models;

namespace PesaControl.Core.Services;

public class ModbusBarrierService : IBarrierService, IDisposable
{
    private SerialPort? _serial;
    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private BarrierConfig _config = new();
    private bool _disposed;

    public string Name => _config.Name;
    public bool IsConnected => _serial?.IsOpen == true || _stream != null;

    public event EventHandler<bool>? ConnectionStatusChanged;
    public event EventHandler<string>? ErrorOccurred;

    public Task ConnectAsync(BarrierConfig config)
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
            var msg = $"Error al conectar barrera Modbus: {ex.Message}";
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

    public async Task OpenAsync()
    {
        var request = ModbusHelper.BuildWriteSingleCoilRequest(_config.SlaveId, _config.CoilAddress, true);
        await SendAsync(request);
        await Task.Delay(50);
    }

    public async Task CloseAsync()
    {
        var request = ModbusHelper.BuildWriteSingleCoilRequest(_config.SlaveId, _config.CoilAddress, false);
        await SendAsync(request);
        await Task.Delay(50);
    }

    public async Task<bool> GetStatusAsync()
    {
        var request = ModbusHelper.BuildReadCoilsRequest(_config.SlaveId, _config.CoilAddress, 1);
        await SendAsync(request);

        var response = _stream != null
            ? ModbusHelper.ReadTcpResponse(_stream)
            : ModbusHelper.ReadResponse(_serial!, 5);

        if (ModbusHelper.TryReadCoils(response, out var coils) && coils.Length > 0)
            return coils[0];

        return false;
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
            var msg = "Barrera Modbus no conectada";
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
