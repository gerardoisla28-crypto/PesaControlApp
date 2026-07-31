namespace PesaControl.Core.Models;

public enum CameraType
{
    USB,
    IP,
    RTSP
}

public class CameraConfig
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public CameraType Type { get; set; } = CameraType.RTSP;
    public bool IsActive { get; set; } = true;

    public int? DvrConfigId { get; set; }
    public int? ChannelNumber { get; set; }
    public int Subtype { get; set; }
}
