using System.IO.Ports;
using System.Text;

Console.Title = "SerialTest - Kronos HU1081";

Console.WriteLine("=== SerialTest: Kronos HU1081 (COM5) ===");
Console.WriteLine();

string portName = "COM5";
int baudRate = 9600;

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
    Console.WriteLine("  3. Verifica que sea COM5");
    Console.WriteLine("  4. Prueba desconectar y reconectar el adaptador");
    return;
}

Console.WriteLine("=== COMANDOS DISPONIBLES ===");
Console.WriteLine("  R       - Request (solicitar peso)");
Console.WriteLine("  P       - Print (imprimir peso)");
Console.WriteLine("  W       - Weigh (pesar)");
Console.WriteLine("  S       - Status (estado)");
Console.WriteLine("  ?       - Help (ayuda)");
Console.WriteLine("  T       - Tare (tarar)");
Console.WriteLine("  Z       - Zero (cero)");
Console.WriteLine("  cont    - Cambiar a modo lectura continua");
Console.WriteLine("  esc     - Cambiar a modo lectura con escape sequence");
Console.WriteLine("  hex     - Ver últimos bytes en hex");
Console.WriteLine("  raw     - Ver último raw string");
Console.WriteLine("  cls     - Limpiar pantalla");
Console.WriteLine("  quit    - Salir");
Console.WriteLine();

var continuousMode = false;
string? lastRaw = null;

while (true)
{
    Console.ForegroundColor = ConsoleColor.White;
    Console.Write(continuousMode ? "(CONTINUO) > " : "> ");
    Console.ResetColor();

    var line = Console.ReadLine()?.Trim().ToLower();

    switch (line)
    {
        case "quit":
        case "exit":
        case "q":
            return;

        case "cls":
            Console.Clear();
            break;

        case "cont":
            continuousMode = true;
            Console.WriteLine("[MODO] Lectura continua");
            break;

        case "esc":
            continuousMode = false;
            Console.WriteLine("[MODO] Comando manual");
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

        default:
            if (!string.IsNullOrEmpty(line))
            {
                var cmd = line.ToUpperInvariant();
                try
                {
                    Console.ForegroundColor = ConsoleColor.Magenta;
                    Console.Write($"[SEND] \"{cmd}\\r\\n\"");
                    Console.WriteLine();
                    Console.ResetColor();

                    port.WriteLine(cmd);

                    // esperar respuesta
                    await Task.Delay(300);
                    var data = port.ReadExisting();

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
                        Console.Write($"RESPUESTA HEX: {hex}");
                        Console.WriteLine();
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.Write($"RESPUESTA RAW: \"{data.Replace("\r", "\\r").Replace("\n", "\\n")}\"");
                        Console.WriteLine();
                        if (data.Any(c => !char.IsControl(c)))
                        {
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.Write($"RESPUESTA TXT: \"{new string(data.Where(c => !char.IsControl(c)).ToArray())}\"");
                            Console.WriteLine();
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
            break;
    }
}
