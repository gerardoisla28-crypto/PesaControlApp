namespace PesaControl.Core.Models;

public class DvrConfig
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Manufacturer { get; set; } = "Dahua";
    public string Model { get; set; } = "DH-XVR1B04-I";
    public string IpAddress { get; set; } = "192.168.1.100";
    public int HttpPort { get; set; } = 80;
    public int RtspPort { get; set; } = 554;
    public string Username { get; set; } = "admin";
    public string Password { get; set; } = "";
    public int MaxChannels { get; set; } = 4;
    public bool IsActive { get; set; } = true;
}
