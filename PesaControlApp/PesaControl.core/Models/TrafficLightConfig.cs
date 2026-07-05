namespace PesaControl.Core.Models;

public enum TrafficLightColor
{
    Red,
    Yellow,
    Green,
    Off
}

public class TrafficLightConfig
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public DeviceConnectionType ConnectionType { get; set; } = DeviceConnectionType.Serial;

    public string PortName { get; set; } = "COM1";
    public int BaudRate { get; set; } = 9600;

    public byte SlaveId { get; set; } = 1;
    public ushort RegisterAddress { get; set; } = 0;
    public ushort RedValue { get; set; } = 1;
    public ushort YellowValue { get; set; } = 2;
    public ushort GreenValue { get; set; } = 3;
    public ushort OffValue { get; set; } = 0;
    public string IpAddress { get; set; } = "192.168.1.201";
    public int TcpPort { get; set; } = 502;

    public bool IsActive { get; set; } = true;
}
