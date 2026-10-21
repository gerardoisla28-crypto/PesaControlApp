using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PesaControl.Core.Models;
using PesaControl.Data.Data;
using PesaControl.Core.Security;

namespace PesaControl.Data.Services;

public class ExportImportService
{
    private readonly DatabaseService _db;
    private readonly IPasswordHasher _passwordHasher = new Argon2idPasswordHasher();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ExportImportService(DatabaseService db)
    {
        _db = db;
    }

    public async Task ExportAsync(string destinationPath, IProgress<int>? progress = null)
    {
        var package = await GatherDataAsync(progress);
        var json = JsonSerializer.Serialize(package, JsonOptions);

        await using var zipStream = new FileStream(destinationPath, FileMode.Create);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Create);

        var jsonEntry = archive.CreateEntry("export.json", CompressionLevel.Optimal);
        await using (var jsonStream = jsonEntry.Open())
        await using (var writer = new StreamWriter(jsonStream, Encoding.UTF8))
        {
            await writer.WriteAsync(json);
        }

        int photoIndex = 0;
        int totalPhotos = package.Photos.Count;
        foreach (var photo in package.Photos)
        {
            var photosDir = GetPhotosDirectory();
            var sourcePath = Path.Combine(photosDir, photo.FileName);
            if (File.Exists(sourcePath))
            {
                var photoEntry = archive.CreateEntry($"photos/{photo.FileName}", CompressionLevel.Optimal);
                await using var entryStream = photoEntry.Open();
                await using var fileStream = File.OpenRead(sourcePath);
                await fileStream.CopyToAsync(entryStream);
            }
            photoIndex++;
            progress?.Report(50 + (photoIndex * 40 / Math.Max(totalPhotos, 1)));
        }

        var metadata = new
        {
            exportedAt = package.ExportedAt,
            appVersion = package.AppVersion,
            schemaVersion = package.SchemaVersion,
            totalRecords = new
            {
                companies = package.Companies.Count,
                products = package.Products.Count,
                drivers = package.Drivers.Count,
                operators = package.Operators.Count,
                users = package.Users.Count,
                weighings = package.WeighingRecords.Count,
                plateRecords = package.PlateRecords.Count,
                configs = package.SystemConfigs.Count,
                photos = package.Photos.Count
            }
        };

        var metaJson = JsonSerializer.Serialize(metadata, JsonOptions);
        var metaEntry = archive.CreateEntry("metadata.json", CompressionLevel.Optimal);
        await using (var metaStream = metaEntry.Open())
        await using (var metaWriter = new StreamWriter(metaStream, Encoding.UTF8))
        {
            await metaWriter.WriteAsync(metaJson);
        }

        progress?.Report(100);
    }

    public async Task<ImportResult> ImportAsync(string sourcePath, IProgress<int>? progress = null)
    {
        var result = new ImportResult();

        try
        {
            progress?.Report(0);

            ExportPackage? package;
            Dictionary<string, byte[]> extractedPhotos = [];

            await using (var zipStream = File.OpenRead(sourcePath))
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Read))
            {
                var jsonEntry = archive.GetEntry("export.json")
                    ?? throw new InvalidDataException("export.json no encontrado en el archivo de respaldo");

                await using (var jsonStream = jsonEntry.Open())
                using (var reader = new StreamReader(jsonStream, Encoding.UTF8))
                {
                    var json = await reader.ReadToEndAsync();
                    package = JsonSerializer.Deserialize<ExportPackage>(json, JsonOptions);
                }

                if (package == null)
                    throw new InvalidDataException("El archivo de respaldo contiene datos inválidos");

                foreach (var entry in archive.Entries)
                {
                    if (entry.FullName.StartsWith("photos/") && !entry.FullName.EndsWith("/"))
                    {
                        var fileName = Path.GetFileName(entry.FullName);
                        await using var entryStream = entry.Open();
                        using var ms = new MemoryStream();
                        await entryStream.CopyToAsync(ms);
                        extractedPhotos[fileName] = ms.ToArray();
                    }
                }
            }

            progress?.Report(10);

            progress?.Report(20);

            using var context = _db.CreateContext();
            await context.Database.MigrateAsync();

            // La restauración reemplaza todos los datos; si algo falla, la transacción deja la base como estaba.
            await using var transaction = await context.Database.BeginTransactionAsync();
            await ClearAllTablesAsync(context);

            foreach (var entity in context.ChangeTracker.Entries().ToList())
            {
                entity.State = EntityState.Detached;
            }

            progress?.Report(30);

            if (package.Companies.Count > 0)
            {
                context.Companies.AddRange(package.Companies);
                await context.SaveChangesAsync();
            }
            result.CompaniesImported = package.Companies.Count;

            if (package.Products.Count > 0)
            {
                context.Products.AddRange(package.Products);
                await context.SaveChangesAsync();
            }
            result.ProductsImported = package.Products.Count;

            if (package.Drivers.Count > 0)
            {
                context.Drivers.AddRange(package.Drivers);
                await context.SaveChangesAsync();
            }
            result.DriversImported = package.Drivers.Count;

            if (package.Operators.Count > 0)
            {
                context.Operators.AddRange(package.Operators);
                await context.SaveChangesAsync();
            }
            result.OperatorsImported = package.Operators.Count;

            if (package.Users.Count > 0)
            {
                foreach (var user in package.Users)
                {
                    if (string.IsNullOrWhiteSpace(user.PasswordHash) && !string.IsNullOrWhiteSpace(user.LegacyPassword))
                        user.PasswordHash = _passwordHasher.Hash(user.LegacyPassword);
                    user.LegacyPassword = null;
                    if (string.IsNullOrWhiteSpace(user.PasswordHash))
                        throw new InvalidDataException($"El usuario '{user.Username}' no contiene una contraseña válida.");
                }
                context.Users.AddRange(package.Users);
                await context.SaveChangesAsync();
            }
            result.UsersImported = package.Users.Count;

            if (package.CameraConfigs.Count > 0)
            {
                context.CameraConfigs.AddRange(package.CameraConfigs);
                await context.SaveChangesAsync();
            }
            result.CameraConfigsImported = package.CameraConfigs.Count;

            if (package.BarrierConfigs.Count > 0)
            {
                context.BarrierConfigs.AddRange(package.BarrierConfigs);
                await context.SaveChangesAsync();
            }
            result.BarrierConfigsImported = package.BarrierConfigs.Count;

            if (package.TrafficLightConfigs.Count > 0)
            {
                context.TrafficLightConfigs.AddRange(package.TrafficLightConfigs);
                await context.SaveChangesAsync();
            }
            result.TrafficLightConfigsImported = package.TrafficLightConfigs.Count;

            if (package.TicketTemplates.Count > 0)
            {
                context.TicketTemplates.AddRange(package.TicketTemplates);
                await context.SaveChangesAsync();
            }
            result.TicketTemplatesImported = package.TicketTemplates.Count;

            if (package.WeighingRecords.Count > 0)
            {
                context.WeighingRecords.AddRange(package.WeighingRecords);
                await context.SaveChangesAsync();
            }
            result.WeighingRecordsImported = package.WeighingRecords.Count;

            if (package.PlateRecords.Count > 0)
            {
                context.PlateRecords.AddRange(package.PlateRecords);
                await context.SaveChangesAsync();
            }
            result.PlateRecordsImported = package.PlateRecords.Count;

            if (package.SystemConfigs.Count > 0)
            {
                context.SystemConfigs.AddRange(package.SystemConfigs);
                await context.SaveChangesAsync();
            }
            result.SystemConfigsImported = package.SystemConfigs.Count;

            progress?.Report(70);

            var photosDir = GetPhotosDirectory();
            Directory.CreateDirectory(photosDir);
            foreach (var kvp in extractedPhotos)
            {
                var destPath = Path.Combine(photosDir, kvp.Key);
                await File.WriteAllBytesAsync(destPath, kvp.Value);
            }
            result.PhotosImported = extractedPhotos.Count;

            progress?.Report(90);

            if (package.WeighingRecords.Count > 0)
            {
                foreach (var record in package.WeighingRecords)
                {
                    var fileName = $"{record.TicketNumber}-entry.jpg";
                    if (extractedPhotos.ContainsKey(fileName) && !string.IsNullOrEmpty(record.PhotoPath))
                    {
                        record.PhotoPath = Path.Combine(photosDir, fileName);
                    }

                    var exitFileName = $"{record.TicketNumber}-exit.jpg";
                    if (extractedPhotos.ContainsKey(exitFileName) && !string.IsNullOrEmpty(record.PhotoPathExit))
                    {
                        record.PhotoPathExit = Path.Combine(photosDir, exitFileName);
                    }
                }

                await context.SaveChangesAsync();
            }

            await transaction.CommitAsync();

            result.Success = true;
            result.Message = $"Importación completada. {result.WeighingRecordsImported} registros de pesaje, " +
                             $"{result.CompaniesImported} empresas, {result.ProductsImported} productos, " +
                             $"{result.DriversImported} conductores, {result.OperatorsImported} operadores, " +
                             $"{result.UsersImported} usuarios, {result.PhotosImported} fotos restaurados.";

            progress?.Report(100);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"Error durante la importación: {ex.Message}";
        }

        return result;
    }

    private static IEnumerable<string> GetTableNames(AppDbContext context)
        => context.Model.GetEntityTypes().Select(t => t.GetTableName()).OfType<string>().Distinct();

    private static async Task ClearAllTablesAsync(AppDbContext context)
    {
        // Con las claves foráneas diferidas hasta el commit, el orden de borrado no importa.
        await context.Database.ExecuteSqlRawAsync("PRAGMA defer_foreign_keys = ON;");
        foreach (var table in GetTableNames(context))
        {
#pragma warning disable EF1002 // los nombres salen del modelo de EF, no de entrada del usuario
            await context.Database.ExecuteSqlRawAsync($"DELETE FROM \"{table}\"");
#pragma warning restore EF1002
        }
        // Reinicia los contadores de autoincremento; los registros importados traen su propio Id.
        await context.Database.ExecuteSqlRawAsync("DELETE FROM sqlite_sequence");
    }

    private async Task<ExportPackage> GatherDataAsync(IProgress<int>? progress = null)
    {
        progress?.Report(0);
        var package = new ExportPackage
        {
            SchemaVersion = 1,
            AppVersion = "1.0.0",
            ExportedAt = DateTime.UtcNow
        };

        using var context = _db.CreateContext();

        package.Companies = await context.Companies.ToListAsync();
        progress?.Report(5);

        package.Products = await context.Products.ToListAsync();
        progress?.Report(10);

        package.Drivers = await context.Drivers.ToListAsync();
        ClearNavProperties(package.Drivers, d => d.Company = null);
        progress?.Report(15);

        package.Operators = await context.Operators.ToListAsync();
        ClearNavProperties(package.Operators, o => o.Company = null);
        progress?.Report(20);

        package.Users = await context.Users.ToListAsync();
        progress?.Report(25);

        package.WeighingRecords = await context.WeighingRecords.ToListAsync();
        ClearNavProperties(package.WeighingRecords, w =>
        {
            w.Product = null;
            w.Company = null;
        });
        progress?.Report(35);

        package.PlateRecords = await context.PlateRecords.ToListAsync();
        ClearNavProperties(package.PlateRecords, p =>
        {
            p.Camera = null;
            p.WeighingRecord = null;
        });
        progress?.Report(40);

        package.SystemConfigs = await context.SystemConfigs.ToListAsync();
        progress?.Report(45);

        package.BarrierConfigs = await context.BarrierConfigs.ToListAsync();
        package.TrafficLightConfigs = await context.TrafficLightConfigs.ToListAsync();
        package.CameraConfigs = await context.CameraConfigs.ToListAsync();
        package.TicketTemplates = await context.TicketTemplates.ToListAsync();
        progress?.Report(50);

        var photosDir = GetPhotosDirectory();
        var photoFiles = new HashSet<string>();
        foreach (var weigh in package.WeighingRecords)
        {
            if (!string.IsNullOrEmpty(weigh.PhotoPath))
            {
                var fileName = Path.GetFileName(weigh.PhotoPath);
                if (File.Exists(weigh.PhotoPath) && photoFiles.Add(fileName))
                    package.Photos.Add(new PhotoEntry { FileName = fileName, RelativePath = $"photos/{fileName}" });
            }
            if (!string.IsNullOrEmpty(weigh.PhotoPathExit))
            {
                var fileName = Path.GetFileName(weigh.PhotoPathExit);
                if (File.Exists(weigh.PhotoPathExit) && photoFiles.Add(fileName))
                    package.Photos.Add(new PhotoEntry { FileName = fileName, RelativePath = $"photos/{fileName}" });
            }
        }

        var json = JsonSerializer.Serialize(package, JsonOptions);
        package.Checksum = ComputeSha256(json);

        progress?.Report(55);

        return package;
    }

    private static void ClearNavProperties<T>(List<T> items, Action<T> clearAction)
    {
        foreach (var item in items)
        {
            clearAction(item);
        }
    }

    private static string ComputeSha256(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }

    private static string GetPhotosDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PesaControl", "Photos");
    }
}
