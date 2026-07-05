namespace PesaControl.Core.Models;

public class TicketTemplate
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Header { get; set; } = "";
    public string Footer { get; set; } = "";
    public byte[]? Logo { get; set; }
    public int WidthMM { get; set; } = 80;
}
