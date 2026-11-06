namespace PesaControl.Core.Models;

/// <summary>
/// Plantilla de un protocolo de comunicación (protocolo, parámetros de comunicación y opciones) que se aplica a una báscula.
/// No incluye el puerto COM ni la IP: dependen de cada instalación.
/// </summary>
public class EquipmentProfile
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public ProtocolType Protocol { get; set; } = ProtocolType.AsciiCfg;

    public int BaudRate { get; set; } = 9600;
    public int Parity { get; set; }
    public int DataBits { get; set; } = 8;
    public int StopBits { get; set; } = 1;

    public int TcpPort { get; set; } = 502;
    public byte SlaveId { get; set; } = 1;
    public ushort RegisterAddress { get; set; }
    public int ReadIntervalMs { get; set; } = 200;

    public ProtocolOptions Options { get; set; } = new();

    /// <summary>Copia los valores del perfil a una báscula, conservando su nombre, puerto COM e IP.</summary>
    public ScaleConfig ApplyTo(ScaleConfig s) => new()
    {
        Id = s.Id,
        Name = s.Name,
        IsActive = s.IsActive,
        SortOrder = s.SortOrder,
        PortName = s.PortName,
        IpAddress = s.IpAddress,
        ProtocolType = Protocol,
        BaudRate = BaudRate,
        Parity = Parity,
        DataBits = DataBits,
        StopBits = StopBits,
        TcpPort = TcpPort,
        SlaveId = SlaveId,
        RegisterAddress = RegisterAddress,
        ReadIntervalMs = ReadIntervalMs,
        Options = Options.Clone()
    };

    /// <summary>Perfiles de equipos ya probados y plantillas para equipos nuevos.</summary>
    public static IReadOnlyList<EquipmentProfile> BuiltIn { get; } =
    [
        new()
        {
            Name = "Sensocar Kronos — Modbus RTU",
            Description = "Visor en COM > RS-232 > PC > PROTOC = MOD.RTU, 9600 8N1, esclavo 0. Peso neto FC04 reg 30010, tara/cero por registro de comando.",
            Protocol = ProtocolType.ModbusRtuCfg, SlaveId = 0, RegisterAddress = 9,
            Options = new ProtocolOptions { Function = 4, DataType = ModbusDataType.SignMagnitude32, Divisor = 1000, CommandRegister = 1000, TareValue = 2, ZeroValue = 1 }
        },
        new()
        {
            Name = "Sensocar SC-A12 — ASCII continuo",
            Description = "Trama '+     60' (signo + 7 caracteres) terminada en CR, 9600 8N1.",
            Protocol = ProtocolType.AsciiCfg,
            Options = new ProtocolOptions { Terminator = "\\r" }
        },
        new()
        {
            Name = "ASCII continuo genérico (CR)",
            Description = "Equipo que transmite solo; el peso es el primer número de cada línea terminada en CR.",
            Protocol = ProtocolType.AsciiCfg,
            Options = new ProtocolOptions { Terminator = "\\r" }
        },
        new()
        {
            Name = "ASCII continuo genérico (CR+LF)",
            Description = "Igual que el anterior pero con líneas terminadas en CR+LF (típico de muchos indicadores).",
            Protocol = ProtocolType.AsciiCfg,
            Options = new ProtocolOptions { Terminator = "\\r\\n" }
        },
        new()
        {
            Name = "ASCII por consulta genérico (W)",
            Description = "El equipo responde solo cuando se le envía 'W' + CR+LF. Ajuste el comando según el manual.",
            Protocol = ProtocolType.AsciiCfg, ReadIntervalMs = 300,
            Options = new ProtocolOptions { Terminator = "\\r\\n", PollCommand = "W\\r\\n" }
        },
        new()
        {
            Name = "ASCII Ethernet genérico",
            Description = "Visor que transmite texto por TCP (puerto 10001 por defecto en muchos módulos Ethernet).",
            Protocol = ProtocolType.AsciiTcpCfg, TcpPort = 10001,
            Options = new ProtocolOptions { Terminator = "\\r\\n" }
        },
        new()
        {
            Name = "Modbus RTU genérico (FC03, Int32)",
            Description = "Peso en 2 registros mantenidos desde la dirección indicada, entero de 32 bits. Ajuste divisor y orden de palabras.",
            Protocol = ProtocolType.ModbusRtuCfg,
            Options = new ProtocolOptions { Function = 3, DataType = ModbusDataType.Int32, Divisor = 1 }
        },
        new()
        {
            Name = "Modbus RTU genérico (FC03, Int16)",
            Description = "Peso en 1 registro mantenido de 16 bits.",
            Protocol = ProtocolType.ModbusRtuCfg,
            Options = new ProtocolOptions { Function = 3, DataType = ModbusDataType.Int16, Divisor = 1 }
        },
        new()
        {
            Name = "Modbus ASCII genérico (FC03, Int32)",
            Description = "Como Modbus RTU genérico pero con tramas ASCII (7E1 en muchos equipos: ajuste paridad y bits).",
            Protocol = ProtocolType.ModbusAsciiCfg,
            Options = new ProtocolOptions { Function = 3, DataType = ModbusDataType.Int32, Divisor = 1 }
        },
        new()
        {
            Name = "Modbus TCP genérico (FC03, Int32)",
            Description = "Modbus por Ethernet, puerto 502. Indique la IP de la báscula.",
            Protocol = ProtocolType.ModbusTcpCfg, TcpPort = 502,
            Options = new ProtocolOptions { Function = 3, DataType = ModbusDataType.Int32, Divisor = 1 }
        }
    ];
}
