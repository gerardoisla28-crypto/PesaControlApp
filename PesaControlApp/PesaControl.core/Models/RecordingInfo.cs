namespace PesaControl.Core.Models;

public class RecordingInfo
{
    public int Channel { get; set; }
    public string FilePath { get; set; } = "";
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public long SizeBytes { get; set; }
    public string Type { get; set; } = "dav";
    public string Flags { get; set; } = "";
    public string EventType { get; set; } = "";
}
