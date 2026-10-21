namespace PesaControl.Core.Models;

// Archivo de intercambio de pesajes entre puestos (pendrive). Los pesajes no llevan los Id locales de producto
// y empresa, que cambian de un equipo a otro: llevan sus datos y se reasignan al importar.
public class ExchangePackage
{
    public const string PackageKind = "weighing-exchange";
    public const int CurrentSchemaVersion = 1;

    public string Kind { get; set; } = PackageKind;
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string StationCode { get; set; } = "";
    public string AppVersion { get; set; } = "1.0.0";
    public DateTime ExportedAt { get; set; } = DateTime.Now;

    public List<ExchangeCompany> Companies { get; set; } = [];
    public List<ExchangeProduct> Products { get; set; } = [];
    public List<ExchangeWeighing> Weighings { get; set; } = [];
}

public class ExchangeManifest
{
    public string Kind { get; set; } = ExchangePackage.PackageKind;
    public int SchemaVersion { get; set; } = ExchangePackage.CurrentSchemaVersion;
    public string StationCode { get; set; } = "";
    public DateTime ExportedAt { get; set; }
    public int WeighingCount { get; set; }
    public string Sha256 { get; set; } = "";
}

public class ExchangeCompany
{
    public string Name { get; set; } = "";
    public string RUT { get; set; } = "";
    public string Address { get; set; } = "";
    public string Phone { get; set; } = "";
}

public class ExchangeProduct
{
    public string Name { get; set; } = "";
}

public class ExchangeWeighing
{
    public string TicketNumber { get; set; } = "";
    public string StationCode { get; set; } = "";
    public WeighingStatus Status { get; set; }

    // Entrada
    public DateTime WeighInTime { get; set; }
    public decimal GrossWeight { get; set; }
    public string? EntryScaleName { get; set; }
    public string? PhotoFile { get; set; }

    // Salida
    public DateTime? WeighOutTime { get; set; }
    public decimal TareWeight { get; set; }
    public decimal NetWeight { get; set; }
    public string? ExitScaleName { get; set; }
    public bool IsExitWeightManual { get; set; }
    public string? PhotoFileExit { get; set; }

    // Datos del ticket
    public string LicensePlate { get; set; } = "";
    public string PlateCountry { get; set; } = "CL";
    public string OperatorName { get; set; } = "";
    public string DriverName { get; set; } = "";
    public string Notes { get; set; } = "";

    // Referencias por datos, no por Id
    public string? ProductName { get; set; }
    public string? CompanyRut { get; set; }
    public string? CompanyName { get; set; }
}

public class ExchangeExportResult
{
    public string FilePath { get; set; } = "";
    public bool OnlyPending { get; set; }
    public int Weighings { get; set; }
    public int Photos { get; set; }
}

public static class ExchangeKeys
{
    // "76.123.456-7" y "76123456-7" son la misma empresa: se compara sin puntos, guiones ni mayúsculas.
    public static string NormalizeRut(string? rut)
        => new string((rut ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    public static string NormalizeName(string? name)
        => string.Join(' ', (name ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
}

public class ExchangeConflict
{
    public string TicketNumber { get; set; } = "";
    public string Field { get; set; } = "";
    public string LocalValue { get; set; } = "";
    public string IncomingValue { get; set; } = "";
}

public class ExchangeImportResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public string SourceStationCode { get; set; } = "";
    public DateTime ExportedAt { get; set; }
    public int Inserted { get; set; }
    public int Updated { get; set; }
    public int Unchanged { get; set; }
    public int CompaniesCreated { get; set; }
    public int ProductsCreated { get; set; }
    public int PhotosCopied { get; set; }
    public List<ExchangeConflict> Conflicts { get; set; } = [];
}

// Un pesaje pendiente de exportar, tal como se muestra en la lista para elegir qué llevar en el archivo.
public class ExchangeCandidate
{
    public int Id { get; set; }
    public string TicketNumber { get; set; } = "";
    public string LicensePlate { get; set; } = "";
    public DateTime WeighInTime { get; set; }
    public DateTime? WeighOutTime { get; set; }
    public WeighingStatus Status { get; set; }
    public bool IsExitWeightManual { get; set; }

    // Fecha del último movimiento del pesaje: la salida si ya la tiene y, si no, la entrada.
    public DateTime Activity => WeighOutTime ?? WeighInTime;

    public string Kind
        => Status == WeighingStatus.Cancelled ? "Anulado"
         : WeighOutTime is not null && IsExitWeightManual ? "Salida manual"
         : WeighOutTime is not null ? "Salida"
         : "Entrada";
}

public enum ExportFilter { All, Pending, Exported }
