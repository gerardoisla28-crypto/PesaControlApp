namespace PesaControl.Core.Models;

public class Company
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string RUT { get; set; } = "";
    public string Address { get; set; } = "";
    public string Phone { get; set; } = "";
    public bool IsActive { get; set; } = true;
}
