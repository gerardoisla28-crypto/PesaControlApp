using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PesaControl.Core.Models;
using PesaControl.Data.Data;

namespace PesaControl.Data.Services;

public partial class WeighingExchangeService
{
    private const long MaxExchangeJsonBytes = 200L * 1024 * 1024;

    // Importa un archivo de intercambio fusionando con lo que ya hay: no borra nada y puede repetirse sin duplicar.
    // Todo o nada: si algo falla, la base queda como estaba.
    public async Task<ExchangeImportResult> ImportAsync(string sourcePath, IProgress<int>? progress = null)
    {
        var result = new ExchangeImportResult();
        try
        {
            using var zip = OpenZip(sourcePath);
            var package = ReadPackage(zip);
            result.SourceStationCode = package.StationCode;
            result.ExportedAt = package.ExportedAt;
            progress?.Report(10);

            var localStation = await _db.GetStationCodeAsync();
            using var ctx = _db.CreateContext();
            await using var transaction = await ctx.Database.BeginTransactionAsync();

            var companies = await ResolveCompaniesAsync(ctx, package, result);
            var products = await ResolveProductsAsync(ctx, package, result);
            progress?.Report(30);

            var existing = await LoadExistingAsync(ctx, package.Weighings.Select(w => w.TicketNumber));
            foreach (var incoming in package.Weighings)
            {
                var companyId = ResolveCompanyId(incoming, companies);
                var productId = string.IsNullOrWhiteSpace(incoming.ProductName)
                    ? (int?)null
                    : products.GetValueOrDefault(ExchangeKeys.NormalizeName(incoming.ProductName));

                if (existing.TryGetValue(incoming.TicketNumber, out var local))
                {
                    if (Merge(local, incoming, companyId, productId, zip, result))
                        result.Updated++;
                    else
                        result.Unchanged++;
                }
                else
                {
                    ctx.WeighingRecords.Add(CreateRecord(incoming, companyId, productId, zip, result));
                    result.Inserted++;
                }
            }
            await ctx.SaveChangesAsync();
            progress?.Report(80);

            await RaiseOwnCountersAsync(ctx, package, localStation);
            await transaction.CommitAsync();
            progress?.Report(100);

            result.Success = true;
            result.Message = BuildMessage(result);
        }
        catch (InvalidDataException ex)
        {
            result.Success = false;
            result.Message = ex.Message;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = ex switch
            {
                FileNotFoundException or DirectoryNotFoundException => "No se encontró el archivo seleccionado.",
                UnauthorizedAccessException => "No hay permiso para leer el archivo seleccionado.",
                _ => $"No se pudo importar el archivo: {ex.Message}"
            };
        }
        return result;
    }

    private static ZipArchive OpenZip(string path)
    {
        try { return ZipFile.OpenRead(path); }
        catch (InvalidDataException) { throw new InvalidDataException("El archivo no es un archivo de intercambio de pesajes de PesaControl."); }
    }

    private static ExchangePackage ReadPackage(ZipArchive zip)
    {
        const string notExchange = "El archivo no es un archivo de intercambio de pesajes de PesaControl.";

        var manifestEntry = zip.GetEntry(ManifestEntry);
        var exchangeEntry = zip.GetEntry(ExchangeEntry);
        if (manifestEntry is null || exchangeEntry is null) throw new InvalidDataException(notExchange);

        ExchangeManifest? manifest;
        try
        {
            using var stream = manifestEntry.Open();
            manifest = JsonSerializer.Deserialize<ExchangeManifest>(stream, JsonOptions);
        }
        catch (JsonException) { throw new InvalidDataException(notExchange); }
        if (manifest is null || manifest.Kind != ExchangePackage.PackageKind) throw new InvalidDataException(notExchange);
        if (manifest.SchemaVersion > ExchangePackage.CurrentSchemaVersion)
            throw new InvalidDataException("El archivo fue creado por una versión más nueva de PesaControl. Actualice la aplicación.");

        if (exchangeEntry.Length > MaxExchangeJsonBytes) throw new InvalidDataException("El archivo es demasiado grande.");
        byte[] json;
        using (var stream = exchangeEntry.Open())
        using (var buffer = new MemoryStream())
        {
            stream.CopyTo(buffer);
            json = buffer.ToArray();
        }
        if (!string.Equals(Convert.ToHexString(SHA256.HashData(json)), manifest.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("El archivo está dañado: la suma de verificación no coincide. Vuelva a copiarlo o a exportarlo.");

        ExchangePackage? package;
        try { package = JsonSerializer.Deserialize<ExchangePackage>(json, JsonOptions); }
        catch (JsonException) { throw new InvalidDataException(notExchange); }
        if (package is null) throw new InvalidDataException(notExchange);

        var seen = new HashSet<string>();
        foreach (var w in package.Weighings)
        {
            if (string.IsNullOrWhiteSpace(w.TicketNumber) || w.TicketNumber.Length > 20 || w.StationCode.Length > 4)
                throw new InvalidDataException("El archivo contiene un pesaje con un número de ticket o un código de puesto inválido.");
            if (!seen.Add(w.TicketNumber))
                throw new InvalidDataException($"El archivo repite el ticket {w.TicketNumber}.");
        }
        return package;
    }

    // ── Catálogos: se emparejan por RUT y, si no, por nombre ──

    private sealed class CompanyIndex
    {
        public Dictionary<string, int> ByRut { get; } = new();
        public Dictionary<string, int> ByName { get; } = new();
        // Una empresa local sin RUT puede ser la misma que llega con RUT; una con otro RUT es distinta.
        public Dictionary<string, int> ByNameWithoutRut { get; } = new();
    }

    private static async Task<CompanyIndex> ResolveCompaniesAsync(AppDbContext ctx, ExchangePackage package, ExchangeImportResult result)
    {
        var index = new CompanyIndex();
        foreach (var c in await ctx.Companies.AsNoTracking().OrderBy(c => c.Id).ToListAsync())
            AddToIndex(index, c);

        foreach (var incoming in package.Companies)
        {
            var rut = ExchangeKeys.NormalizeRut(incoming.RUT);
            var name = ExchangeKeys.NormalizeName(incoming.Name);
            if (name.Length == 0 && rut.Length == 0) continue;
            if (FindCompany(index, rut, name) is not null) continue;

            var company = new Company
            {
                Name = incoming.Name, RUT = incoming.RUT, Address = incoming.Address, Phone = incoming.Phone, IsActive = true
            };
            ctx.Companies.Add(company);
            await ctx.SaveChangesAsync();
            AddToIndex(index, company);
            result.CompaniesCreated++;
        }
        return index;
    }

    private static void AddToIndex(CompanyIndex index, Company c)
    {
        var rut = ExchangeKeys.NormalizeRut(c.RUT);
        var name = ExchangeKeys.NormalizeName(c.Name);
        if (rut.Length > 0) index.ByRut.TryAdd(rut, c.Id);
        else if (name.Length > 0) index.ByNameWithoutRut.TryAdd(name, c.Id);
        if (name.Length > 0) index.ByName.TryAdd(name, c.Id);
    }

    private static int? FindCompany(CompanyIndex index, string rut, string name)
    {
        if (rut.Length > 0)
        {
            if (index.ByRut.TryGetValue(rut, out var byRut)) return byRut;
            return index.ByNameWithoutRut.TryGetValue(name, out var byName) ? byName : null;
        }
        return index.ByName.TryGetValue(name, out var any) ? any : null;
    }

    private static int? ResolveCompanyId(ExchangeWeighing incoming, CompanyIndex index)
    {
        var rut = ExchangeKeys.NormalizeRut(incoming.CompanyRut);
        var name = ExchangeKeys.NormalizeName(incoming.CompanyName);
        if (rut.Length == 0 && name.Length == 0) return null;
        return FindCompany(index, rut, name);
    }

    private static async Task<Dictionary<string, int>> ResolveProductsAsync(AppDbContext ctx, ExchangePackage package, ExchangeImportResult result)
    {
        var products = new Dictionary<string, int>();
        foreach (var p in await ctx.Products.AsNoTracking().OrderBy(p => p.Id).ToListAsync())
            products.TryAdd(ExchangeKeys.NormalizeName(p.Name), p.Id);

        foreach (var incoming in package.Products)
        {
            var key = ExchangeKeys.NormalizeName(incoming.Name);
            if (key.Length == 0 || products.ContainsKey(key)) continue;

            var product = new Product { Name = incoming.Name, IsActive = true };
            ctx.Products.Add(product);
            await ctx.SaveChangesAsync();
            products[key] = product.Id;
            result.ProductsCreated++;
        }
        return products;
    }

    // ── Pesajes ──

    private static async Task<Dictionary<string, WeighingRecord>> LoadExistingAsync(AppDbContext ctx, IEnumerable<string> tickets)
    {
        var found = new Dictionary<string, WeighingRecord>();
        foreach (var chunk in tickets.Chunk(500))
        {
            var part = chunk.ToList();
            foreach (var record in await ctx.WeighingRecords.Where(w => part.Contains(w.TicketNumber)).ToListAsync())
                found[record.TicketNumber] = record;
        }
        return found;
    }

    private static WeighingRecord CreateRecord(ExchangeWeighing i, int? companyId, int? productId, ZipArchive zip, ExchangeImportResult result)
        => new()
        {
            TicketNumber = i.TicketNumber,
            StationCode = i.StationCode,
            Status = i.Status,
            WeighInTime = i.WeighInTime,
            GrossWeight = i.GrossWeight,
            EntryScaleName = i.EntryScaleName,
            PhotoPath = CopyPhoto(zip, i.PhotoFile, result),
            WeighOutTime = i.WeighOutTime,
            TareWeight = i.TareWeight,
            NetWeight = i.NetWeight,
            ExitScaleName = i.ExitScaleName,
            IsExitWeightManual = i.IsExitWeightManual,
            PhotoPathExit = CopyPhoto(zip, i.PhotoFileExit, result),
            LicensePlate = i.LicensePlate,
            PlateCountry = i.PlateCountry,
            OperatorName = i.OperatorName,
            DriverName = i.DriverName,
            Notes = i.Notes,
            CompanyId = companyId,
            ProductId = productId
        };

    // Une las dos mitades de un ticket. Un dato vacío se completa con el del archivo; si ambos lados tienen
    // el mismo dato con valores distintos, se conserva el local y se informa el conflicto. Devuelve si cambió algo.
    private static bool Merge(WeighingRecord local, ExchangeWeighing i, int? companyId, int? productId, ZipArchive zip, ExchangeImportResult result)
    {
        var changed = false;

        void Text(string label, string? current, string? incoming, Action<string> set)
        {
            if (string.IsNullOrEmpty(incoming) || current == incoming) return;
            if (string.IsNullOrEmpty(current)) { set(incoming); changed = true; }
            else Conflict(result, local.TicketNumber, label, current, incoming);
        }

        void Reference(string label, int? current, int? incoming, Action<int?> set)
        {
            if (incoming is null || current == incoming) return;
            if (current is null) { set(incoming); changed = true; }
            else Conflict(result, local.TicketNumber, label, current.ToString()!, incoming.ToString()!);
        }

        // Datos del ticket
        Text("Placa", local.LicensePlate, i.LicensePlate, v => local.LicensePlate = v);
        Text("País de la placa", local.PlateCountry, i.PlateCountry, v => local.PlateCountry = v);
        Text("Operador", local.OperatorName, i.OperatorName, v => local.OperatorName = v);
        Text("Conductor", local.DriverName, i.DriverName, v => local.DriverName = v);
        Text("Notas", local.Notes, i.Notes, v => local.Notes = v);
        Reference("Empresa", local.CompanyId, companyId, v => local.CompanyId = v);
        Reference("Producto", local.ProductId, productId, v => local.ProductId = v);

        // Entrada: se toma completa si la local está vacía
        var localEntryEmpty = local.GrossWeight == 0 && string.IsNullOrEmpty(local.EntryScaleName);
        var entryDiffers = local.WeighInTime != i.WeighInTime || local.GrossWeight != i.GrossWeight || local.EntryScaleName != i.EntryScaleName;
        var incomingEntryEmpty = i.GrossWeight == 0 && string.IsNullOrEmpty(i.EntryScaleName);
        if (entryDiffers && !incomingEntryEmpty)
        {
            if (localEntryEmpty)
            {
                local.WeighInTime = i.WeighInTime; local.GrossWeight = i.GrossWeight; local.EntryScaleName = i.EntryScaleName;
                changed = true;
            }
            else
            {
                if (local.GrossWeight != i.GrossWeight) Conflict(result, local.TicketNumber, "Peso de entrada", local.GrossWeight.ToString(), i.GrossWeight.ToString());
                if (local.WeighInTime != i.WeighInTime) Conflict(result, local.TicketNumber, "Hora de entrada", local.WeighInTime.ToString("g"), i.WeighInTime.ToString("g"));
                if (local.EntryScaleName != i.EntryScaleName && !string.IsNullOrEmpty(i.EntryScaleName))
                    Conflict(result, local.TicketNumber, "Báscula de entrada", local.EntryScaleName ?? "", i.EntryScaleName);
            }
        }

        // Salida: igual que la entrada
        var localExitEmpty = local.WeighOutTime is null && local.TareWeight == 0 && local.NetWeight == 0 && string.IsNullOrEmpty(local.ExitScaleName);
        var incomingExitEmpty = i.WeighOutTime is null && i.TareWeight == 0 && i.NetWeight == 0 && string.IsNullOrEmpty(i.ExitScaleName);
        var exitDiffers = local.WeighOutTime != i.WeighOutTime || local.TareWeight != i.TareWeight || local.NetWeight != i.NetWeight
                          || local.ExitScaleName != i.ExitScaleName || local.IsExitWeightManual != i.IsExitWeightManual;
        if (exitDiffers && !incomingExitEmpty)
        {
            if (localExitEmpty)
            {
                local.WeighOutTime = i.WeighOutTime; local.TareWeight = i.TareWeight; local.NetWeight = i.NetWeight;
                local.ExitScaleName = i.ExitScaleName; local.IsExitWeightManual = i.IsExitWeightManual;
                changed = true;
            }
            else
            {
                if (local.TareWeight != i.TareWeight) Conflict(result, local.TicketNumber, "Tara", local.TareWeight.ToString(), i.TareWeight.ToString());
                if (local.NetWeight != i.NetWeight) Conflict(result, local.TicketNumber, "Peso neto", local.NetWeight.ToString(), i.NetWeight.ToString());
                if (local.WeighOutTime != i.WeighOutTime) Conflict(result, local.TicketNumber, "Hora de salida", local.WeighOutTime?.ToString("g") ?? "", i.WeighOutTime?.ToString("g") ?? "");
                if (local.ExitScaleName != i.ExitScaleName && !string.IsNullOrEmpty(i.ExitScaleName))
                    Conflict(result, local.TicketNumber, "Báscula de salida", local.ExitScaleName ?? "", i.ExitScaleName);
            }
        }

        // Fotos: solo se completan si faltan (dos fotos distintas no son un conflicto que resolver)
        if (!PhotoExists(local.PhotoPath) && CopyPhoto(zip, i.PhotoFile, result) is { } entryPhoto)
        { local.PhotoPath = entryPhoto; changed = true; }
        if (!PhotoExists(local.PhotoPathExit) && CopyPhoto(zip, i.PhotoFileExit, result) is { } exitPhoto)
        { local.PhotoPathExit = exitPhoto; changed = true; }

        // Estado: una anulación prevalece; si no, el más avanzado
        var status = MergeStatus(local.Status, i.Status);
        if (status != local.Status) { local.Status = status; changed = true; }

        return changed;
    }

    private static WeighingStatus MergeStatus(WeighingStatus local, WeighingStatus incoming)
    {
        if (local == WeighingStatus.Cancelled || incoming == WeighingStatus.Cancelled) return WeighingStatus.Cancelled;
        return (WeighingStatus)Math.Max((int)local, (int)incoming);
    }

    private static void Conflict(ExchangeImportResult result, string ticket, string field, string local, string incoming)
        => result.Conflicts.Add(new ExchangeConflict { TicketNumber = ticket, Field = field, LocalValue = local, IncomingValue = incoming });

    private static bool PhotoExists(string? path) => !string.IsNullOrEmpty(path) && File.Exists(path);

    // Copia la foto del archivo a la carpeta local. Solo se usa el nombre del archivo: nunca una ruta del zip.
    private static string? CopyPhoto(ZipArchive zip, string? fileName, ExchangeImportResult result)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;
        var safeName = Path.GetFileName(fileName);
        var entry = safeName.Length == 0 ? null : zip.GetEntry($"photos/{safeName}");
        if (entry is null) return null;

        var directory = DatabaseSettings.GetPhotosDirectory();
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, safeName);
        if (!File.Exists(destination))
        {
            entry.ExtractToFile(destination);
            result.PhotosCopied++;
        }
        return destination;
    }

    // Si el archivo trae tickets de este mismo puesto (por ejemplo, tras reinstalar el equipo), el contador
    // debe seguir después del mayor número ya usado para no repetirlo.
    private static async Task RaiseOwnCountersAsync(AppDbContext ctx, ExchangePackage package, string? localStation)
    {
        if (string.IsNullOrEmpty(localStation)) return;

        var highest = new Dictionary<string, int>();
        foreach (var w in package.Weighings.Where(w => w.StationCode == localStation))
        {
            var cut = w.TicketNumber.LastIndexOf('-');
            if (cut < 0 || !int.TryParse(w.TicketNumber[(cut + 1)..], out var number)) continue;
            var prefix = w.TicketNumber[..(cut + 1)];
            highest[prefix] = Math.Max(highest.GetValueOrDefault(prefix), number);
        }

        foreach (var (prefix, number) in highest)
        {
            await ctx.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "TicketCounters" ("Prefix", "LastNumber") VALUES ({prefix}, {number})
                ON CONFLICT ("Prefix") DO UPDATE SET "LastNumber" = MAX("TicketCounters"."LastNumber", excluded."LastNumber")
                """);
        }
    }

    private static string BuildMessage(ExchangeImportResult r)
    {
        var parts = new List<string> { $"{r.Inserted} nuevos", $"{r.Updated} actualizados", $"{r.Unchanged} sin cambios" };
        if (r.Conflicts.Count > 0) parts.Add($"{r.Conflicts.Select(c => c.TicketNumber).Distinct().Count()} con conflictos");
        var text = $"Importación completada desde el puesto {r.SourceStationCode}: {string.Join(", ", parts)}.";
        if (r.CompaniesCreated + r.ProductsCreated > 0)
            text += $" Se crearon {r.CompaniesCreated} empresa(s) y {r.ProductsCreated} producto(s).";
        if (r.PhotosCopied > 0) text += $" Se copiaron {r.PhotosCopied} foto(s).";
        return text;
    }
}
