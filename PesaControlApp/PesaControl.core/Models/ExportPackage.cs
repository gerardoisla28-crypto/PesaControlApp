using System.Text.Json.Serialization;

namespace PesaControl.Core.Models;

public class ExportPackage
{
    public int SchemaVersion { get; set; } = 1;
    public string AppVersion { get; set; } = "1.0.0";
    public DateTime ExportedAt { get; set; } = DateTime.UtcNow;
    public string Checksum { get; set; } = "";

    public List<Company> Companies { get; set; } = [];
    public List<Product> Products { get; set; } = [];
    public List<Driver> Drivers { get; set; } = [];
    public List<Operator> Operators { get; set; } = [];
    public List<User> Users { get; set; } = [];
    public List<WeighingRecord> WeighingRecords { get; set; } = [];
    public List<PlateRecord> PlateRecords { get; set; } = [];
    public List<SystemConfig> SystemConfigs { get; set; } = [];
    public List<BarrierConfig> BarrierConfigs { get; set; } = [];
    public List<TrafficLightConfig> TrafficLightConfigs { get; set; } = [];
    public List<CameraConfig> CameraConfigs { get; set; } = [];
    public List<TicketTemplate> TicketTemplates { get; set; } = [];
    public List<PhotoEntry> Photos { get; set; } = [];
}

public class PhotoEntry
{
    public string FileName { get; set; } = "";
    public string RelativePath { get; set; } = "";
}

public class ImportResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public int CompaniesImported { get; set; }
    public int ProductsImported { get; set; }
    public int DriversImported { get; set; }
    public int OperatorsImported { get; set; }
    public int UsersImported { get; set; }
    public int WeighingRecordsImported { get; set; }
    public int PlateRecordsImported { get; set; }
    public int SystemConfigsImported { get; set; }
    public int BarrierConfigsImported { get; set; }
    public int TrafficLightConfigsImported { get; set; }
    public int CameraConfigsImported { get; set; }
    public int TicketTemplatesImported { get; set; }
    public int PhotosImported { get; set; }
}
