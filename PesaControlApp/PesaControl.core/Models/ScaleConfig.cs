namespace PesaControl.Core.Models;

public class ScaleConfig
{
    public int Id { get; set; }
    public string Name { get; set; } = "Báscula 1";
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }

    public ProtocolType ProtocolType { get; set; } = ProtocolType.Serial;
    public string PortName { get; set; } = "COM1";
    public int BaudRate { get; set; } = 9600;
    public int Parity { get; set; } = 0;
    public int DataBits { get; set; } = 8;
    public int StopBits { get; set; } = 1;

    public string IpAddress { get; set; } = "";
    public int TcpPort { get; set; } = 502;

    public byte SlaveId { get; set; } = 1;
    public ushort RegisterAddress { get; set; } = 0;
    public int ReadIntervalMs { get; set; } = 200;
    public CommunicationConfig ToCommunicationConfig() => new()
    {
        Type = ProtocolType,
        PortName = PortName,
        BaudRate = BaudRate,
        Parity = Parity,
        DataBits = DataBits,
        StopBits = StopBits,
        IpAddress = IpAddress,
        TcpPort = TcpPort,
        SlaveId = SlaveId,
        RegisterAddress = RegisterAddress,
        ReadIntervalMs = ReadIntervalMs
    };
}
