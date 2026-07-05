using System.IO.Ports;
using System.Net.Sockets;

namespace PesaControl.Core.Services;

internal static class ModbusHelper
{
    public static byte[] BuildReadCoilsRequest(byte slaveId, ushort startAddress, ushort count)
    {
        var data = new byte[] { slaveId, 0x01,
            (byte)(startAddress >> 8), (byte)startAddress,
            (byte)(count >> 8), (byte)count };
        return AddCrc(data);
    }

    public static byte[] BuildWriteSingleCoilRequest(byte slaveId, ushort address, bool value)
    {
        var data = new byte[] { slaveId, 0x05,
            (byte)(address >> 8), (byte)address,
            (byte)(value ? 0xFF : 0x00), 0x00 };
        return AddCrc(data);
    }

    public static byte[] BuildReadHoldingRegistersRequest(byte slaveId, ushort startAddress, ushort count)
    {
        var data = new byte[] { slaveId, 0x03,
            (byte)(startAddress >> 8), (byte)startAddress,
            (byte)(count >> 8), (byte)count };
        return AddCrc(data);
    }

    public static byte[] BuildWriteSingleRegisterRequest(byte slaveId, ushort address, ushort value)
    {
        var data = new byte[] { slaveId, 0x06,
            (byte)(address >> 8), (byte)address,
            (byte)(value >> 8), (byte)value };
        return AddCrc(data);
    }

    public static byte[] BuildTcpMbap(byte slaveId, byte[] pdu)
    {
        var length = (ushort)(pdu.Length + 1);
        var mbap = new byte[]
        {
            0x00, 0x01,
            0x00, 0x00,
            (byte)(length >> 8), (byte)length,
            slaveId
        };
        return mbap.Concat(pdu).ToArray();
    }

    public static bool TryReadCoils(byte[] response, out bool[] coils)
    {
        coils = [];
        if (response.Length < 4 || response[1] != 0x01) return false;
        var byteCount = response[2];
        if (response.Length < 3 + byteCount + 2) return false;
        var crc = Crc16(response.Take(3 + byteCount).ToArray());
        if (response[^2] != (byte)(crc & 0xFF) || response[^1] != (byte)(crc >> 8)) return false;

        coils = new bool[byteCount * 8];
        for (var i = 0; i < byteCount; i++)
            for (var b = 0; b < 8; b++)
                coils[i * 8 + b] = (response[3 + i] & (1 << b)) != 0;
        return true;
    }

    public static bool TryReadHoldingRegisters(byte[] response, out ushort[] registers)
    {
        registers = [];
        if (response.Length < 4 || response[1] != 0x03) return false;
        var byteCount = response[2];
        if (response.Length < 3 + byteCount + 2) return false;
        var crc = Crc16(response.Take(3 + byteCount).ToArray());
        if (response[^2] != (byte)(crc & 0xFF) || response[^1] != (byte)(crc >> 8)) return false;

        registers = new ushort[byteCount / 2];
        for (var i = 0; i < registers.Length; i++)
            registers[i] = (ushort)((response[3 + i * 2] << 8) | response[4 + i * 2]);
        return true;
    }

    public static byte[] ReadResponse(SerialPort port, int expectedMinLength)
    {
        port.DiscardInBuffer();
        var buffer = new byte[256];
        var read = 0;
        var start = Environment.TickCount;
        while (read < expectedMinLength && Environment.TickCount - start < 1000)
        {
            try
            {
                read += port.Read(buffer, read, buffer.Length - read);
            }
            catch (TimeoutException) { break; }
        }
        return buffer.Take(read).ToArray();
    }

    public static byte[] ReadTcpResponse(NetworkStream stream, int timeoutMs = 2000)
    {
        stream.ReadTimeout = timeoutMs;
        var header = new byte[7];
        var read = 0;
        while (read < 7)
            read += stream.Read(header, read, 7 - read);

        var length = (header[4] << 8) | header[5];
        var body = new byte[length - 1];
        read = 0;
        while (read < body.Length)
            read += stream.Read(body, read, body.Length - read);

        return body;
    }

    public static ushort Crc16(byte[] data)
    {
        ushort crc = 0xFFFF;
        foreach (var b in data)
        {
            crc ^= b;
            for (var i = 0; i < 8; i++)
                crc = (crc & 1) != 0 ? (ushort)((crc >> 1) ^ 0xA001) : (ushort)(crc >> 1);
        }
        return crc;
    }

    private static byte[] AddCrc(byte[] data)
    {
        var crc = Crc16(data);
        return [.. data, (byte)(crc & 0xFF), (byte)(crc >> 8)];
    }
}
