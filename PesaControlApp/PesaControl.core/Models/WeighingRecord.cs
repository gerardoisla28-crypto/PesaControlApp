namespace PesaControl.Core.Models;

public enum WeighingStatus
{
    WeighingIn,
    WeighingOut,
    Completed,
    Cancelled
}

public class WeighingRecord
{
    public int Id { get; set; }
    public string TicketNumber { get; set; } = "";
    public string StationCode { get; set; } = "";
    public DateTime WeighInTime { get; set; }
    public DateTime? WeighOutTime { get; set; }
    public decimal GrossWeight { get; set; }
    public decimal TareWeight { get; set; }
    public decimal NetWeight { get; set; }
    public string LicensePlate { get; set; } = "";
    public string PlateCountry { get; set; } = "CL";
    public string OperatorName { get; set; } = "";
    public string DriverName { get; set; } = "";
    public int? ProductId { get; set; }
    public int? CompanyId { get; set; }
    public WeighingStatus Status { get; set; } = WeighingStatus.WeighingIn;
    public string? PhotoPath { get; set; }
    public string? PhotoPathExit { get; set; }
    public string? EntryScaleName { get; set; }
    public string? ExitScaleName { get; set; }
    public bool IsExitWeightManual { get; set; }
    public string Notes { get; set; } = "";

    // Pendiente de incluir en un archivo de intercambio: se activa al crear o modificar el pesaje y se desactiva al exportarlo.
    public bool ExportPending { get; set; } = true;

    public Product? Product { get; set; }
    public Company? Company { get; set; }
}
