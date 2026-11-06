using PesaControl.Core.Models;

namespace PesaControl.UI.ViewModels;

/// <summary>Nombres que se muestran en pantalla para cada tipo de protocolo, en un solo lugar.</summary>
public static class ProtocolCatalog
{
    private static readonly (string Name, ProtocolType Type)[] Items =
    [
        ("Serial", ProtocolType.Serial),
        ("Modbus TCP", ProtocolType.ModbusTcp),
        ("Modbus RTU", ProtocolType.ModbusRtu),
        ("Sensoc", ProtocolType.Sensoc),
        ("Sensocar A12", ProtocolType.SensocarA12),
        ("Kronos Modbus", ProtocolType.KronosModbus),
        ("Modbus RTU (configurable)", ProtocolType.ModbusRtuCfg),
        ("Modbus ASCII (configurable)", ProtocolType.ModbusAsciiCfg),
        ("Modbus TCP (configurable)", ProtocolType.ModbusTcpCfg),
        ("ASCII serie/USB (configurable)", ProtocolType.AsciiCfg),
        ("ASCII Ethernet (configurable)", ProtocolType.AsciiTcpCfg)
    ];

    public static string[] Names { get; } = Items.Select(i => i.Name).ToArray();

    public static string NameOf(ProtocolType type)
        => Items.FirstOrDefault(i => i.Type == type).Name ?? "Serial";

    public static ProtocolType TypeOf(string name)
        => Items.FirstOrDefault(i => i.Name == name) is { Name: not null } hit ? hit.Type : ProtocolType.Serial;

    public static bool IsTcp(ProtocolType t)
        => t is ProtocolType.ModbusTcp or ProtocolType.ModbusTcpCfg or ProtocolType.AsciiTcpCfg;

    public static bool IsConfigurable(ProtocolType t)
        => t is ProtocolType.ModbusRtuCfg or ProtocolType.ModbusAsciiCfg or ProtocolType.ModbusTcpCfg
            or ProtocolType.AsciiCfg or ProtocolType.AsciiTcpCfg;

    public static bool IsModbusCfg(ProtocolType t)
        => t is ProtocolType.ModbusRtuCfg or ProtocolType.ModbusAsciiCfg or ProtocolType.ModbusTcpCfg;

    public static bool IsAsciiCfg(ProtocolType t)
        => t is ProtocolType.AsciiCfg or ProtocolType.AsciiTcpCfg;

    /// <summary>Protocolos que avisan por evento cuando llega un peso (los antiguos Serial/Modbus se consultan).</summary>
    public static bool IsEventDriven(ProtocolType t)
        => t is not (ProtocolType.Serial or ProtocolType.ModbusTcp or ProtocolType.ModbusRtu);
}
