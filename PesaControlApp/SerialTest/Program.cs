using System.IO.Ports;
using System.Text;

var portName = args.Length > 0 ? args[0] : "COM6";
var baudRate = args.Length > 1 ? int.Parse(args[1]) : 9600;
var slaveId = args.Length > 2 ? byte.Parse(args[2]) : (byte)1;

Console.Title = $"SerialTest ({portName})";
Console.WriteLine($"=== SerialTest ({portName} @ {baudRate} 8N1, Slave={slaveId}) ===");
Console.WriteLine();

using var port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
{
    ReadTimeout = 500,
    WriteTimeout = 1000,
    NewLine = "\r\n",
    Encoding = Encoding.ASCII
};

port.DataReceived += (s, e) =>
{
    try
    {
        var sp = (SerialPort)s!;
        while (sp.BytesToRead > 0)
        {
            var raw = sp.ReadExisting();
            var hex = string.Join(" ", raw.Select(c => $"0x{(byte)c:X2}"));

            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write($"[RECV] HEX: {hex}");
            Console.WriteLine();

            Console.ForegroundColor = ConsoleColor.Cyan;
            var escaped = raw
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\x02", "STX")
                .Replace("\x03", "ETX");
            Console.Write($"[RECV] RAW: \"{escaped}\"");
            Console.WriteLine();

            if (raw.Any(c => !char.IsControl(c)))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write($"[RECV] TXT: \"{new string(raw.Where(c => !char.IsControl(c)).ToArray())}\"");
                Console.WriteLine();
            }

            Console.ResetColor();
            Console.WriteLine();
        }
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[ERR] {ex.Message}");
        Console.ResetColor();
    }
};

try
{
    port.Open();
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"[OK] Conectado a {portName} ({baudRate} 8N1)");
    Console.ResetColor();
    Console.WriteLine();
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"[ERR] No se pudo abrir {portName}: {ex.Message}");
    Console.ResetColor();
    Console.WriteLine("Sugerencias:");
    Console.WriteLine("  1. Verifica que el adaptador USB-RS232 esté conectado");
    Console.WriteLine("  2. Abre Administrador de dispositivos > Puertos (COM y LPT)");
    Console.WriteLine("  3. Prueba desconectar y reconectar el adaptador");
    return;
}

Console.WriteLine("=== COMANDOS DISPONIBLES ===");
Console.WriteLine("  R/W/S/?/T/Z  - Comandos ASCII (serial directo)");
Console.WriteLine("  m            - Modbus RTU: Read Holding Regs (addr=0, count=2)");
Console.WriteLine("  ms <id>      - Cambiar Slave ID (actual: {0})", slaveId);
Console.WriteLine("  mr <addr>    - Cambiar Register Address (actual: 0)");
Console.WriteLine("  mb           - Modbus RTU: enviar bytes hex manual (ej: 01 03 00 00 00 02)");
Console.WriteLine("  dtr          - Alternar DTR (actualmente: {0})", port.DtrEnable ? "ON" : "OFF");
Console.WriteLine("  rts          - Alternar RTS (actualmente: {0})", port.RtsEnable ? "ON" : "OFF");
Console.WriteLine("  cont/esc     - Modo continuo / comando manual");
Console.WriteLine("  hex/raw      - Ver último raw string");
Console.WriteLine("  cls/quit     - Limpiar / Salir");
Console.WriteLine();

var continuousMode = false;
string? lastRaw = null;
var registerAddress = (ushort)0;

port.DtrEnable = true;
port.RtsEnable = true;

while (true)
{
    Console.ForegroundColor = ConsoleColor.White;
    Console.Write(continuousMode ? "(CONTINUO) > " : "> ");
    Console.ResetColor();

    var line = Console.ReadLine()?.Trim();
    if (string.IsNullOrEmpty(line)) continue;
    var lower = line.ToLowerInvariant();

    switch (lower)
    {
        case "quit":
        case "exit":
        case "q":
            return;

        case "cls":
            Console.Clear();
            break;

        case "dtr":
            port.DtrEnable = !port.DtrEnable;
            Console.WriteLine($"[DTR] {(port.DtrEnable ? "ON" : "OFF")}");
            break;

        case "rts":
            port.RtsEnable = !port.RtsEnable;
            Console.WriteLine($"[RTS] {(port.RtsEnable ? "ON" : "OFF")}");
            break;

        case "cont":
            continuousMode = true;
            port.DtrEnable = true;
            port.RtsEnable = true;
            Console.WriteLine("[MODO] Lectura continua (DTR+RTS activados)");
            break;

        case "esc":
            continuousMode = false;
            port.DtrEnable = false;
            port.RtsEnable = false;
            Console.WriteLine("[MODO] Comando manual (DTR+RTS desactivados)");
            break;

        case "hex":
            if (lastRaw != null)
            {
                var hex = string.Join(" ", lastRaw.Select(c => $"0x{(byte)c:X2}"));
                Console.WriteLine($"Último RAW (hex): {hex}");
            }
            else
                Console.WriteLine("(sin datos aún)");
            break;

        case "raw":
            if (lastRaw != null)
                Console.WriteLine($"Último RAW: \"{lastRaw}\"");
            else
                Console.WriteLine("(sin datos aún)");
            break;

        case "m":
            await SendModbusRequest(port, slaveId, registerAddress, 2);
            break;

        case "mb":
            Console.WriteLine("Uso: mb <hex bytes>  ej: mb 01 03 00 00 00 02");
            break;

        default:
            if (lower.StartsWith("ms ") && byte.TryParse(line.AsSpan(3), out var newSlave))
            {
                slaveId = newSlave;
                Console.WriteLine($"[MODBUS] Slave ID = {slaveId}");
            }
            else if (lower.StartsWith("mr ") && ushort.TryParse(line.AsSpan(3), out var newReg))
            {
                registerAddress = newReg;
                Console.WriteLine($"[MODBUS] Register Address = {registerAddress}");
            }
            else if (lower.StartsWith("mb "))
            {
                var hexBytes = line[3..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var bytes = new byte[hexBytes.Length];
                for (var i = 0; i < hexBytes.Length; i++)
                    bytes[i] = Convert.ToByte(hexBytes[i], 16);
                await SendRawBytes(port, bytes);
            }
            else if (lower is "m" or "w" or "r" or "s" or "t" or "z" or "p" or "?" or "")
            {
                // nada, ya se manejo m arriba
            }
            else if (!string.IsNullOrEmpty(line))
            {
                await SendAsciiCommand(port, line.ToUpperInvariant());
            }
            break;
    }
}

// ───────── helpers ─────────

static ushort Crc16(byte[] data, int length)
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

async Task SendModbusRequest(SerialPort sp, byte slave, ushort addr, ushort count)
{
    var req = new byte[8];
    req[0] = slave;
    req[1] = 0x03; // Read Holding Registers
    req[2] = (byte)(addr >> 8);
    req[3] = (byte)(addr & 0xFF);
    req[4] = (byte)(count >> 8);
    req[5] = (byte)(count & 0xFF);
    var crc = Crc16(req, 6);
    req[6] = (byte)(crc & 0xFF);
    req[7] = (byte)(crc >> 8);

    await SendRawBytes(sp, req);
}

async Task SendRawBytes(SerialPort sp, byte[] data)
{
    try
    {
        var hex = string.Join(" ", data.Select(b => $"0x{b:X2}"));
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine($"[SEND] HEX: {hex}");
        Console.ResetColor();

        sp.DiscardInBuffer();
        sp.Write(data, 0, data.Length);

        await Task.Delay(200);
        var resp = new List<byte>();
        var buf = new byte[256];
        try
        {
            while (sp.BytesToRead > 0)
            {
                var read = sp.Read(buf, 0, buf.Length);
                if (read <= 0) break;
                resp.AddRange(buf[..read]);
            }
        }
        catch (TimeoutException) { }

        if (resp.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine("(sin respuesta)");
            Console.ResetColor();
        }
        else
        {
            lastRaw = Encoding.ASCII.GetString(resp.ToArray());
            var respHex = string.Join(" ", resp.Select(b => $"0x{b:X2}"));
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[RECV] HEX: {respHex}");

            if (resp.Count >= 4 && resp[1] == 0x03)
            {
                // Intentar parsear como float32 si es respuesta Modbus valida
                var byteCount = resp[2];
                if (byteCount >= 4 && resp.Count >= byteCount + 5)
                {
                    var payload = resp.GetRange(3, byteCount);
                    {
                        var fBytes = new[] { payload[0], payload[1], payload[2], payload[3] };
                        if (BitConverter.IsLittleEndian)
                            Array.Reverse(fBytes);
                        var floatVal = BitConverter.ToSingle(fBytes, 0);
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"[PARSE] float32: {floatVal:F3}");
                        Console.ResetColor();
                    }
                }
            }
            Console.ResetColor();
        }
    }
    catch (TimeoutException)
    {
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine("(timeout - sin respuesta)");
        Console.ResetColor();
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[ERR] {ex.Message}");
        Console.ResetColor();
    }
}

async Task SendAsciiCommand(SerialPort sp, string cmd)
{
    try
    {
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine($"[SEND] \"{cmd}\\r\\n\"");
        Console.ResetColor();

        sp.WriteLine(cmd);

        await Task.Delay(300);
        var data = sp.ReadExisting();

        if (string.IsNullOrEmpty(data))
        {
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine("(sin respuesta)");
            Console.ResetColor();
        }
        else
        {
            lastRaw = data;
            var hex = string.Join(" ", data.Select(c => $"0x{(byte)c:X2}"));
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"RESPUESTA HEX: {hex}");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"RESPUESTA RAW: \"{data.Replace("\r", "\\r").Replace("\n", "\\n")}\"");
            if (data.Any(c => !char.IsControl(c)))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"RESPUESTA TXT: \"{new string(data.Where(c => !char.IsControl(c)).ToArray())}\"");
            }
            Console.ResetColor();
        }
    }
    catch (TimeoutException)
    {
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine("(timeout - sin respuesta)");
        Console.ResetColor();
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[ERR] {ex.Message}");
        Console.ResetColor();
    }
}
