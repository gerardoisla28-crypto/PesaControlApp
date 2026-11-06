using System.IO.Ports;
using System.Text;
using System.Text.RegularExpressions;
using PesaControl.Core.Models;

namespace PesaControl.Core.Services;

public sealed record DetectionResult(
    ProtocolType Protocol,
    string PortName,
    int BaudRate,
    int Parity,
    int DataBits,
    int StopBits,
    byte SlaveId,
    string Description,
    string Sample,
    ProtocolOptions? Options = null);

/// <summary>
/// Busca por puerto serie qué visor hay conectado y con qué parámetros, para no configurarlo a mano en terreno.
/// Solo escucha y envía consultas Modbus de lectura (FC 04); nunca escribe comandos (tara/cero).
/// Reconoce: Sensocar SC-A12 (ASCII continuo "+     60") y Sensocar Kronos en Modbus RTU.
/// </summary>
public class EquipmentDetector
{
    private static readonly int[] BaudRates = [9600, 19200, 4800, 2400, 1200, 38400, 57600, 115200];
    private static readonly byte[] SlaveIds = [0, 1, 2];

    // Signo + 7 caracteres numéricos alineados a la derecha, p. ej. "+     60" o "-  12.5".
    private static readonly Regex AsciiFrame = new(@"^[+\-][ 0-9.,]{7}$", RegexOptions.Compiled);

    private const int ListenMs = 1200;
    private const int ModbusWaitMs = 300;

    public async Task<DetectionResult?> DetectAsync(IEnumerable<string> ports, IProgress<string>? progress, CancellationToken ct = default)
    {
        // Los puertos son independientes: se prueban a la vez y se corta al primer acierto,
        // para que un puerto virtual o sin equipo no retrase la detección.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var pending = ports.Select(p => ScanPortAsync(p, progress, cts.Token)).ToList();

        try
        {
            while (pending.Count > 0)
            {
                var done = await Task.WhenAny(pending);
                pending.Remove(done);

                var result = await done;
                if (result is not null)
                {
                    cts.Cancel();
                    return result;
                }
            }
            return null;
        }
        finally
        {
            cts.Cancel();
            try { await Task.WhenAll(pending); } catch { }
        }
    }

    private static async Task<DetectionResult?> ScanPortAsync(string port, IProgress<string>? progress, CancellationToken ct)
    {
        try
        {
            foreach (var baud in BaudRates)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report($"Probando {port} a {baud} baudios…");

                var (result, unavailable) = await TryAsync(port, baud, ct);
                if (result is not null)
                    return result;

                if (unavailable)
                {
                    progress?.Report($"{port} no está disponible (ocupado o inexistente), se omite.");
                    return null;
                }
            }
            return null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    private static async Task<(DetectionResult? Result, bool Unavailable)> TryAsync(string port, int baud, CancellationToken ct)
    {
        SerialPort? sp = null;
        try
        {
            try
            {
                sp = new SerialPort(port, baud, Parity.None, 8, StopBits.One)
                {
                    ReadTimeout = 200,
                    WriteTimeout = 500,
                    DtrEnable = true,
                    RtsEnable = true
                };
                sp.Open();
            }
            catch
            {
                return (null, true);
            }

            // 1) Escucha pasiva: algunos visores transmiten solos.
            sp.DiscardInBuffer();
            await Task.Delay(ListenMs, ct);
            var data = ReadAll(sp);

            var ascii = DetectAsciiContinuous(data);
            if (ascii is not null)
                return (new DetectionResult(ProtocolType.SensocarA12, port, baud, 0, 8, 1, 0,
                    "Sensocar SC-A12 (ASCII continuo)", ascii), false);

            // Texto continuo de cualquier otra marca: se propone el terminador y el patrón de peso por defecto.
            var generic = DetectGenericText(data);
            if (generic is not null)
                return (new DetectionResult(ProtocolType.AsciiCfg, port, baud, 0, 8, 1, 0,
                    "Texto continuo (equipo genérico)", generic.Value.Sample,
                    new ProtocolOptions { Terminator = generic.Value.Terminator }), false);

            // 2) Consulta Modbus RTU de solo lectura al mapa del Kronos.
            foreach (var slave in SlaveIds)
            {
                ct.ThrowIfCancellationRequested();
                var request = BuildReadInputRegisters(slave, 9, 7);
                sp.DiscardInBuffer();
                sp.Write(request, 0, request.Length);
                await Task.Delay(ModbusWaitMs, ct);
                var response = ReadAll(sp);

                if (IsKronosResponse(response, slave))
                    return (new DetectionResult(ProtocolType.KronosModbus, port, baud, 0, 8, 1, slave,
                        $"Sensocar Kronos (Modbus RTU, esclavo {slave})", Convert.ToHexString(response)), false);
            }

            return (null, false);
        }
        finally
        {
            try { if (sp?.IsOpen == true) sp.Close(); } catch { }
            sp?.Dispose();
        }
    }

    private static string? DetectAsciiContinuous(byte[] data)
    {
        if (data.Length < 16) return null;

        var text = Encoding.Latin1.GetString(data);
        var frames = text.Split('\r').Select(f => f.Trim('\n')).ToArray();
        if (frames.Length < 3) return null;

        // El primero puede estar cortado y el último incompleto.
        var complete = frames.Skip(1).Take(frames.Length - 2).ToArray();
        var matches = complete.Where(f => AsciiFrame.IsMatch(f) && f.Any(char.IsDigit)).ToArray();

        if (matches.Length >= 2 && matches.Length * 10 >= complete.Length * 6)
            return matches[0];
        return null;
    }

    private static readonly Regex GenericWeight = new(ProtocolOptions.DefaultWeightPattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Líneas de texto repetidas con un número, sea cual sea la marca.</summary>
    private static (string Sample, string Terminator)? DetectGenericText(byte[] data)
    {
        if (data.Length < 24) return null;

        var printable = data.Count(b => b is >= 0x20 and <= 0x7E or 0x0D or 0x0A);
        if (printable * 100 < data.Length * 95) return null;

        var text = Encoding.Latin1.GetString(data);
        string escaped, term;
        if (text.Contains("\r\n")) { escaped = "\\r\\n"; term = "\r\n"; }
        else if (text.Contains('\r')) { escaped = "\\r"; term = "\r"; }
        else if (text.Contains('\n')) { escaped = "\\n"; term = "\n"; }
        else return null;

        var frames = text.Split(term);
        if (frames.Length < 4) return null;

        // El primero puede estar cortado y el último incompleto.
        var complete = frames.Skip(1).Take(frames.Length - 2).Select(f => f.Trim('\r', '\n')).Where(f => f.Length > 0).ToArray();
        if (complete.Length < 2) return null;

        var withNumber = complete.Where(f => GenericWeight.IsMatch(f)).ToArray();
        if (withNumber.Length * 10 < complete.Length * 7) return null;

        return (withNumber[0], escaped);
    }

    private static bool IsKronosResponse(byte[] r, byte slave)
        => r.Length >= 19 && r[0] == slave && r[1] == 0x04 && r[2] == 14 && CrcOk(r.AsSpan(0, 19));

    private static byte[] ReadAll(SerialPort sp)
    {
        try
        {
            var n = sp.BytesToRead;
            if (n <= 0) return [];
            var buf = new byte[n];
            var read = sp.Read(buf, 0, n);
            return read == n ? buf : buf[..read];
        }
        catch
        {
            return [];
        }
    }

    private static byte[] BuildReadInputRegisters(byte slave, ushort address, ushort count)
    {
        var req = new byte[8];
        req[0] = slave;
        req[1] = 0x04;
        req[2] = (byte)(address >> 8);
        req[3] = (byte)(address & 0xFF);
        req[4] = (byte)(count >> 8);
        req[5] = (byte)(count & 0xFF);
        var crc = Crc16(req.AsSpan(0, 6));
        req[6] = (byte)(crc & 0xFF);
        req[7] = (byte)(crc >> 8);
        return req;
    }

    private static bool CrcOk(ReadOnlySpan<byte> frame)
    {
        var crc = Crc16(frame[..^2]);
        return frame[^2] == (byte)(crc & 0xFF) && frame[^1] == (byte)(crc >> 8);
    }

    private static ushort Crc16(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (var b in data)
        {
            crc ^= b;
            for (var j = 0; j < 8; j++)
                crc = (crc & 1) != 0 ? (ushort)((crc >> 1) ^ 0xA001) : (ushort)(crc >> 1);
        }
        return crc;
    }
}
