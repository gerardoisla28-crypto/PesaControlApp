using System.Diagnostics;
using System.IO.Ports;
using System.Net.Sockets;
using PesaControl.Core.Models;

namespace PesaControl.Core.Services;

/// <summary>Canal de bytes (puerto serie/USB o TCP) que usan los protocolos configurables.</summary>
public abstract class ByteChannel : IDisposable
{
    public abstract bool IsOpen { get; }
    public abstract Task OpenAsync(CancellationToken ct);
    public abstract void Close();
    public abstract Task WriteAsync(byte[] data, CancellationToken ct);

    /// <summary>Lee hasta <paramref name="count"/> bytes. Devuelve 0 si vence el tiempo sin datos.</summary>
    public abstract Task<int> ReadAsync(byte[] buffer, int offset, int count, int timeoutMs, CancellationToken ct);
    public abstract void DiscardInput();

    /// <summary>Lee exactamente <paramref name="count"/> bytes o devuelve null si no llegan a tiempo.</summary>
    public async Task<byte[]?> ReadExactAsync(int count, int timeoutMs, CancellationToken ct)
    {
        var buf = new byte[count];
        var read = 0;
        var sw = Stopwatch.StartNew();
        while (read < count)
        {
            var remaining = timeoutMs - (int)sw.ElapsedMilliseconds;
            if (remaining <= 0) return null;
            var n = await ReadAsync(buf, read, count - read, remaining, ct);
            if (n <= 0) return null;
            read += n;
        }
        return buf;
    }

    public void Dispose() => Close();
}

public sealed class SerialChannel : ByteChannel
{
    private readonly CommunicationConfig _config;
    private readonly bool _dtrRts;
    private SerialPort? _port;

    /// <param name="dtrRts">Activa DTR/RTS (los visores ASCII de Sensocar lo necesitan; Modbus no).</param>
    public SerialChannel(CommunicationConfig config, bool dtrRts)
    {
        _config = config;
        _dtrRts = dtrRts;
    }

    public override bool IsOpen => _port?.IsOpen ?? false;

    public override Task OpenAsync(CancellationToken ct)
    {
        Close();
        _port = new SerialPort(_config.PortName, _config.BaudRate,
            (Parity)_config.Parity, _config.DataBits, (StopBits)_config.StopBits)
        {
            ReadTimeout = 500,
            WriteTimeout = 500,
            DtrEnable = _dtrRts,
            RtsEnable = _dtrRts
        };
        _port.Open();
        return Task.CompletedTask;
    }

    public override void Close()
    {
        var port = _port;
        _port = null;
        if (port is null) return;
        try { if (port.IsOpen) port.Close(); } catch { }
        port.Dispose();
    }

    public override Task WriteAsync(byte[] data, CancellationToken ct)
    {
        _port!.Write(data, 0, data.Length);
        return Task.CompletedTask;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, int timeoutMs, CancellationToken ct)
    {
        var port = _port;
        if (port is null || !port.IsOpen) return Task.FromResult(0);

        return Task.Run(() =>
        {
            try
            {
                port.ReadTimeout = Math.Max(timeoutMs, 1);
                return port.Read(buffer, offset, count);
            }
            catch (TimeoutException) { return 0; }
            catch (InvalidOperationException) { return 0; }
            catch (IOException) { return 0; }
        }, ct);
    }

    public override void DiscardInput()
    {
        try { _port?.DiscardInBuffer(); } catch { }
    }
}

public sealed class TcpChannel : ByteChannel
{
    private readonly string _host;
    private readonly int _port;
    private TcpClient? _client;
    private NetworkStream? _stream;

    public TcpChannel(string host, int port)
    {
        _host = host;
        _port = port;
    }

    public override bool IsOpen => _client?.Connected ?? false;

    public override async Task OpenAsync(CancellationToken ct)
    {
        Close();
        var client = new TcpClient { NoDelay = true };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            await client.ConnectAsync(_host, _port, timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            client.Dispose();
            throw new TimeoutException($"Sin respuesta de {_host}:{_port} (3 s).");
        }
        catch
        {
            client.Dispose();
            throw;
        }
        _client = client;
        _stream = client.GetStream();
    }

    public override void Close()
    {
        var client = _client;
        _client = null;
        _stream = null;
        try { client?.Close(); } catch { }
    }

    public override async Task WriteAsync(byte[] data, CancellationToken ct)
        => await _stream!.WriteAsync(data, ct);

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, int timeoutMs, CancellationToken ct)
    {
        var stream = _stream;
        if (stream is null) return 0;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Math.Max(timeoutMs, 1));
        try
        {
            return await stream.ReadAsync(buffer.AsMemory(offset, count), timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return 0; }
        catch (IOException) { return 0; }
        catch (ObjectDisposedException) { return 0; }
    }

    public override void DiscardInput()
    {
        try
        {
            var stream = _stream;
            var trash = new byte[256];
            while (stream is { DataAvailable: true })
                stream.Read(trash, 0, trash.Length);
        }
        catch { }
    }
}
