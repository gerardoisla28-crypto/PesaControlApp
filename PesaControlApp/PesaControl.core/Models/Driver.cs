namespace PesaControl.Core.Models;

public class Driver
{
    public int Id { get; set; }
    public string FullName { get; set; } = "";
    public string? Phone { get; set; }
    public int CompanyId { get; set; }
    public Company? Company { get; set; }
    public bool IsActive { get; set; } = true;
}
