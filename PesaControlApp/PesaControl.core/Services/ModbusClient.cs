using System.Text;

namespace PesaControl.Core.Services;

public enum ModbusTransport
{
    Rtu,
    Ascii,
    Tcp
}

/// <summary>
/// Cliente Modbus maestro mínimo (lectura de registros FC 03/04 y escritura de un registro FC 06)
/// sobre RTU, ASCII o TCP. Una sola petición a la vez: quien lo use debe serializar las llamadas.
/// </summary>
public sealed class ModbusClient
{
    private readonly ByteChannel _channel;
    private readonly ModbusTransport _transport;
    private ushort _transactionId;

    public int TimeoutMs { get; set; } = 600;

    /// <summary>Código de la última excepción Modbus recibida (0 = ninguna).</summary>
    public byte LastException { get; private set; }

    public ModbusClient(ByteChannel channel, ModbusTransport transport)
    {
        _channel = channel;
        _transport = transport;
    }

    public async Task<ushort[]?> ReadRegistersAsync(byte unit, byte function, ushort address, ushort count, CancellationToken ct)
    {
        var pdu = new byte[] { function, (byte)(address >> 8), (byte)address, (byte)(count >> 8), (byte)count };
        var resp = await ExchangeAsync(unit, pdu, ct);

        if (resp is null || resp.Length < 2 + count * 2 || resp[0] != function || resp[1] != count * 2)
            return null;

        var regs = new ushort[count];
        for (var i = 0; i < count; i++)
            regs[i] = (ushort)((resp[2 + i * 2] << 8) | resp[3 + i * 2]);
        return regs;
    }

    public async Task<bool> WriteSingleRegisterAsync(byte unit, ushort address, ushort value, CancellationToken ct)
    {
        var pdu = new byte[] { 0x06, (byte)(address >> 8), (byte)address, (byte)(value >> 8), (byte)value };
        var resp = await ExchangeAsync(unit, pdu, ct);
        return resp is { Length: 5 } && resp.AsSpan().SequenceEqual(pdu);
    }

    /// <summary>Envía el PDU y devuelve el PDU de la respuesta (código de función + datos), o null.</summary>
    private async Task<byte[]?> ExchangeAsync(byte unit, byte[] pdu, CancellationToken ct)
    {
        LastException = 0;
        _channel.DiscardInput();

        return _transport switch
        {
            ModbusTransport.Rtu => await ExchangeRtuAsync(unit, pdu, ct),
            ModbusTransport.Ascii => await ExchangeAsciiAsync(unit, pdu, ct),
            _ => await ExchangeTcpAsync(unit, pdu, ct)
        };
    }

    // ── RTU: unidad + PDU + CRC16 ────────────────────────
    private async Task<byte[]?> ExchangeRtuAsync(byte unit, byte[] pdu, CancellationToken ct)
    {
        var adu = new byte[pdu.Length + 3];
        adu[0] = unit;
        pdu.CopyTo(adu, 1);
        var crc = Crc16(adu, adu.Length - 2);
        adu[^2] = (byte)(crc & 0xFF);
        adu[^1] = (byte)(crc >> 8);
        await _channel.WriteAsync(adu, ct);

        var head = await _channel.ReadExactAsync(2, TimeoutMs, ct);
        if (head is null || head[0] != unit) return null;

        int rest;
        if ((head[1] & 0x80) != 0) rest = 3;                 // código de excepción + CRC
        else if (head[1] is 0x03 or 0x04) rest = -1;         // el tamaño lo da el siguiente byte
        else rest = 6;                                       // eco de FC 06: dirección + valor + CRC

        byte[] tail;
        if (rest < 0)
        {
            var len = await _channel.ReadExactAsync(1, TimeoutMs, ct);
            if (len is null) return null;
            var body = await _channel.ReadExactAsync(len[0] + 2, TimeoutMs, ct);
            if (body is null) return null;
            tail = new byte[1 + body.Length];
            tail[0] = len[0];
            body.CopyTo(tail, 1);
        }
        else
        {
            var t = await _channel.ReadExactAsync(rest, TimeoutMs, ct);
            if (t is null) return null;
            tail = t;
        }

        var frame = new byte[2 + tail.Length];
        head.CopyTo(frame, 0);
        tail.CopyTo(frame, 2);

        var calc = Crc16(frame, frame.Length - 2);
        if (frame[^2] != (byte)(calc & 0xFF) || frame[^1] != (byte)(calc >> 8)) return null;

        if ((head[1] & 0x80) != 0)
        {
            LastException = frame[2];
            return null;
        }
        return frame[1..^2];
    }

    // ── ASCII: ':' + hex(unidad + PDU + LRC) + CRLF ──────
    private async Task<byte[]?> ExchangeAsciiAsync(byte unit, byte[] pdu, CancellationToken ct)
    {
        var raw = new byte[pdu.Length + 1];
        raw[0] = unit;
        pdu.CopyTo(raw, 1);

        var text = ":" + Convert.ToHexString(raw) + Lrc(raw).ToString("X2") + "\r\n";
        await _channel.WriteAsync(Encoding.ASCII.GetBytes(text), ct);

        var sb = new StringBuilder();
        var one = new byte[1];
        var deadline = Environment.TickCount64 + TimeoutMs;
        while (Environment.TickCount64 < deadline && sb.Length < 600)
        {
            var n = await _channel.ReadAsync(one, 0, 1, (int)Math.Max(deadline - Environment.TickCount64, 1), ct);
            if (n <= 0) break;
            var c = (char)one[0];
            if (c == ':') sb.Clear();
            if (c == '\n') break;
            if (c != '\r') sb.Append(c);
        }

        var line = sb.ToString();
        if (line.Length < 9 || line[0] != ':' || (line.Length - 1) % 2 != 0) return null;

        byte[] bytes;
        try { bytes = Convert.FromHexString(line[1..]); }
        catch (FormatException) { return null; }

        if (bytes.Length < 4 || bytes[0] != unit) return null;
        if (Lrc(bytes.AsSpan(0, bytes.Length - 1)) != bytes[^1]) return null;

        if ((bytes[1] & 0x80) != 0)
        {
            LastException = bytes[2];
            return null;
        }
        return bytes[1..^1];
    }

    // ── TCP: MBAP (id, protocolo, longitud, unidad) + PDU ─
    private async Task<byte[]?> ExchangeTcpAsync(byte unit, byte[] pdu, CancellationToken ct)
    {
        var id = ++_transactionId;
        var adu = new byte[7 + pdu.Length];
        adu[0] = (byte)(id >> 8);
        adu[1] = (byte)id;
        adu[4] = (byte)((pdu.Length + 1) >> 8);
        adu[5] = (byte)(pdu.Length + 1);
        adu[6] = unit;
        pdu.CopyTo(adu, 7);
        await _channel.WriteAsync(adu, ct);

        var header = await _channel.ReadExactAsync(7, TimeoutMs, ct);
        if (header is null || header[0] != (byte)(id >> 8) || header[1] != (byte)id) return null;

        var length = (header[4] << 8) | header[5];
        if (length < 2 || length > 260) return null;

        var body = await _channel.ReadExactAsync(length - 1, TimeoutMs, ct);
        if (body is null) return null;

        if ((body[0] & 0x80) != 0)
        {
            LastException = body.Length > 1 ? body[1] : (byte)0;
            return null;
        }
        return body;
    }

    private static ushort Crc16(byte[] data, int length)
    {
        ushort crc = 0xFFFF;
        for (var i = 0; i < length; i++)
        {
            crc ^= data[i];
            for (var j = 0; j < 8; j++)
                crc = (crc & 1) != 0 ? (ushort)((crc >> 1) ^ 0xA001) : (ushort)(crc >> 1);
        }
        return crc;
    }

    private static byte Lrc(ReadOnlySpan<byte> data)
    {
        var sum = 0;
        foreach (var b in data) sum += b;
        return (byte)(-sum & 0xFF);
    }
}
