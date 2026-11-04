using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PesaControl.Core.Models;
using PesaControl.Data.Data;

namespace PesaControl.Data.Services;

// Intercambio de pesajes entre puestos mediante un archivo (por ejemplo en un pendrive), para los puestos
// que trabajan sin conexión al servidor.
public partial class WeighingExchangeService
{
    public const string ExchangeEntry = "exchange.json";
    public const string ManifestEntry = "manifest.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly DatabaseService _db;

    public WeighingExchangeService(DatabaseService db)
    {
        _db = db;
    }

    public static string SuggestFileName(string stationCode)
        => $"intercambio-{stationCode}-{DateTime.Now:yyyyMMdd-HHmm}.zip";

    // Exporta los pesajes elegidos con sus fotos.
    public async Task<ExchangeExportResult> ExportSelectedAsync(string destinationPath, IReadOnlyCollection<int> ids, IProgress<int>? progress = null)
    {
        var stationCode = await _db.GetStationCodeAsync()
            ?? throw new InvalidOperationException("El código de puesto no está configurado.");

        using var ctx = _db.CreateContext();
        var records = new List<WeighingRecord>();
        foreach (var chunk in ids.Distinct().Chunk(500))
        {
            var part = chunk.ToList();
            records.AddRange(await ctx.WeighingRecords.AsNoTracking()
                .Include(w => w.Product).Include(w => w.Company)
                .Where(w => part.Contains(w.Id)).ToListAsync());
        }
        records = records.OrderBy(w => w.Id).ToList();
        progress?.Report(20);

        var package = new ExchangePackage { StationCode = stationCode };
        foreach (var record in records)
            package.Weighings.Add(ToExchange(record));

        package.Products = records.Where(r => r.Product != null)
            .Select(r => r.Product!.Name).Distinct()
            .Select(name => new ExchangeProduct { Name = name }).ToList();
        package.Companies = records.Where(r => r.Company != null)
            .Select(r => r.Company!).DistinctBy(c => c.Id)
            .Select(c => new ExchangeCompany { Name = c.Name, RUT = c.RUT, Address = c.Address, Phone = c.Phone }).ToList();

        var json = JsonSerializer.SerializeToUtf8Bytes(package, JsonOptions);
        var manifest = new ExchangeManifest
        {
            StationCode = stationCode,
            ExportedAt = package.ExportedAt,
            WeighingCount = package.Weighings.Count,
            Sha256 = Convert.ToHexString(SHA256.HashData(json))
        };

        // Se escribe en un archivo temporal y se reemplaza al final: un pendrive lleno no deja un archivo a medias.
        var temp = destinationPath + ".tmp";
        var photoCount = 0;
        try
        {
            await using (var zipStream = new FileStream(temp, FileMode.Create))
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
            {
                await WriteEntryAsync(archive, ExchangeEntry, json);
                await WriteEntryAsync(archive, ManifestEntry, JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions));

                var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var record in records)
                {
                    foreach (var path in new[] { record.PhotoPath, record.PhotoPathExit })
                    {
                        if (string.IsNullOrEmpty(path) || !File.Exists(path)) continue;
                        var fileName = Path.GetFileName(path);
                        if (!added.Add(fileName)) continue;
                        archive.CreateEntryFromFile(path, $"photos/{fileName}", CompressionLevel.NoCompression);
                        photoCount++;
                    }
                }
            }

            File.Move(temp, destinationPath, overwrite: true);
        }
        catch
        {
            if (File.Exists(temp)) File.Delete(temp);
            throw;
        }
        progress?.Report(100);

        return new ExchangeExportResult
        {
            FilePath = destinationPath,
            Weighings = records.Count,
            Photos = photoCount
        };
    }

    private static ExchangeWeighing ToExchange(WeighingRecord r) => new()
    {
        TicketNumber = r.TicketNumber,
        StationCode = r.StationCode,
        Status = r.Status,
        WeighInTime = r.WeighInTime,
        GrossWeight = r.GrossWeight,
        EntryScaleName = r.EntryScaleName,
        PhotoFile = PhotoName(r.PhotoPath),
        WeighOutTime = r.WeighOutTime,
        TareWeight = r.TareWeight,
        NetWeight = r.NetWeight,
        ExitScaleName = r.ExitScaleName,
        IsExitWeightManual = r.IsExitWeightManual,
        PhotoFileExit = PhotoName(r.PhotoPathExit),
        LicensePlate = r.LicensePlate,
        PlateCountry = r.PlateCountry,
        OperatorName = r.OperatorName,
        DriverName = r.DriverName,
        Notes = r.Notes,
        ProductName = r.Product?.Name,
        CompanyRut = r.Company?.RUT,
        CompanyName = r.Company?.Name
    };

    private static string? PhotoName(string? path)
        => !string.IsNullOrEmpty(path) && File.Exists(path) ? Path.GetFileName(path) : null;

    private static async Task WriteEntryAsync(ZipArchive archive, string name, byte[] content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await stream.WriteAsync(content);
    }
}
