namespace PesaControl.Core.Models;

public class DvrEventInfo
{
    public int Channel { get; set; }
    public string EventType { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public string Description { get; set; } = "";
}
