namespace PesaControl.Core.Models;

public enum ProtocolType
{
    Serial,
    ModbusTcp,
    ModbusRtu,
    TcpClient,
    Sensoc
}

public class CommunicationConfig
{
    public ProtocolType Type { get; set; } = ProtocolType.Serial;

    public string PortName { get; set; } = "COM1";
    public int BaudRate { get; set; } = 9600;
    public int Parity { get; set; } = 0;
    public int DataBits { get; set; } = 8;
    public int StopBits { get; set; } = 1;

    public string IpAddress { get; set; } = "192.168.1.100";
    public int TcpPort { get; set; } = 502;

    public byte SlaveId { get; set; } = 1;
    public ushort RegisterAddress { get; set; } = 0;
    public int ReadIntervalMs { get; set; } = 200;
}
