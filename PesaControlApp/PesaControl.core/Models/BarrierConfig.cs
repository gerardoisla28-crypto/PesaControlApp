namespace PesaControl.Core.Models;

public enum DeviceConnectionType
{
    Serial,
    ModbusRTU,
    ModbusTCP
}

public class BarrierConfig
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public DeviceConnectionType ConnectionType { get; set; } = DeviceConnectionType.Serial;

    public string PortName { get; set; } = "COM1";
    public int BaudRate { get; set; } = 9600;

    public byte SlaveId { get; set; } = 1;
    public ushort CoilAddress { get; set; } = 0;
    public string IpAddress { get; set; } = "192.168.1.200";
    public int TcpPort { get; set; } = 502;

    public bool IsActive { get; set; } = true;
}
