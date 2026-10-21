using Microsoft.EntityFrameworkCore;
using PesaControl.Core.Models;
using PesaControl.Core.Services;
using PesaControl.Data.Data;

namespace PesaControl.Data.Services;

public class LocalWeighingDataService : IWeighingDataService
{
    private readonly DatabaseService _db;

    public LocalWeighingDataService(DatabaseService db)
    {
        _db = db;
    }

    public async Task<WeighingRecord> CreateAsync(WeighingRecord record)
    {
        using var ctx = _db.CreateContext();
        ctx.WeighingRecords.Add(record);
        await ctx.SaveChangesAsync();
        return record;
    }

    public async Task UpdateAsync(WeighingRecord record)
    {
        using var ctx = _db.CreateContext();
        ctx.WeighingRecords.Update(record);
        await ctx.SaveChangesAsync();
    }

    public async Task<WeighingRecord?> GetByIdAsync(int id)
    {
        using var ctx = _db.CreateContext();
        return await ctx.WeighingRecords
            .Include(w => w.Product)
            .Include(w => w.Company)
            .FirstOrDefaultAsync(w => w.Id == id);
    }

    public async Task<WeighingRecord?> GetByTicketNumberAsync(string ticketNumber)
    {
        using var ctx = _db.CreateContext();
        return await ctx.WeighingRecords
            .Include(w => w.Product)
            .Include(w => w.Company)
            .FirstOrDefaultAsync(w => w.TicketNumber == ticketNumber);
    }

    public async Task<WeighingRecord?> GetActiveByPlateAsync(string plate)
    {
        using var ctx = _db.CreateContext();
        return await ctx.WeighingRecords
            .Include(w => w.Product)
            .Include(w => w.Company)
            .Where(w => w.LicensePlate == plate && w.Status == WeighingStatus.WeighingIn)
            .OrderByDescending(w => w.WeighInTime)
            .FirstOrDefaultAsync();
    }

    public async Task<List<WeighingRecord>> GetActiveAsync()
    {
        using var ctx = _db.CreateContext();
        return await ctx.WeighingRecords
            .Include(w => w.Product)
            .Include(w => w.Company)
            .Where(w => w.Status == WeighingStatus.WeighingIn || w.Status == WeighingStatus.WeighingOut)
            .OrderByDescending(w => w.WeighInTime)
            .ToListAsync();
    }

    public async Task<List<WeighingRecord>> GetTodayAsync()
    {
        using var ctx = _db.CreateContext();
        return await ctx.WeighingRecords
            .Include(w => w.Product)
            .Include(w => w.Company)
            .Where(w => w.WeighInTime >= DateTime.Today)
            .OrderByDescending(w => w.WeighInTime)
            .ToListAsync();
    }

    // Un solo INSERT ... ON CONFLICT incrementa el contador del mes de forma atómica, así que dos puestos
    // que piden un número a la vez nunca reciben el mismo. Si el contador del mes aún no existe (por
    // ejemplo, tras restaurar un respaldo), parte del mayor número ya usado.
    public async Task<string> GetNextTicketAsync()
    {
        using var ctx = _db.CreateContext();
        var prefix = $"{DateTime.Now.Year}-{DateTime.Now.Month:D2}-";

        var connection = ctx.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO "TicketCounters" ("Prefix", "LastNumber")
            VALUES (@prefix, COALESCE((
                SELECT MAX(CAST(substr("TicketNumber", length(@prefix) + 1) AS integer))
                FROM "WeighingRecords"
                WHERE "TicketNumber" ~ ('^' || @prefix || '[0-9]+$')), 0) + 1)
            ON CONFLICT ("Prefix") DO UPDATE SET "LastNumber" = "TicketCounters"."LastNumber" + 1
            RETURNING "LastNumber"
            """;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "prefix";
        parameter.Value = prefix;
        command.Parameters.Add(parameter);

        var next = Convert.ToInt32(await command.ExecuteScalarAsync());
        return $"{prefix}{next:D4}";
    }

    public async Task<List<Product>> GetProductsAsync()
    {
        using var ctx = _db.CreateContext();
        return await ctx.Products.OrderBy(p => p.Name).ToListAsync();
    }

    public async Task<List<Company>> GetCompaniesAsync()
    {
        using var ctx = _db.CreateContext();
        return await ctx.Companies.OrderBy(c => c.Name).ToListAsync();
    }

    public async Task<List<Operator>> GetOperatorsAsync()
    {
        using var ctx = _db.CreateContext();
        return await ctx.Operators.Include(o => o.Company).OrderBy(o => o.FullName).ToListAsync();
    }

    public async Task<List<Driver>> GetDriversAsync()
    {
        using var ctx = _db.CreateContext();
        return await ctx.Drivers.Include(d => d.Company).OrderBy(d => d.FullName).ToListAsync();
    }
}
