using Microsoft.EntityFrameworkCore;
using PesaControl.Core.Models;
using PesaControl.Data.Data;

namespace PesaControl.Data.Repositories;

public class WeighingRepository : Repository<WeighingRecord>, IWeighingRepository
{
    public WeighingRepository(AppDbContext context) : base(context) { }

    public async Task<List<WeighingRecord>> GetByDateRangeAsync(DateTime from, DateTime to)
        => await _dbSet
            .Include(w => w.Product)
            .Include(w => w.Company)
            .Where(w => w.WeighInTime >= from && w.WeighInTime <= to)
            .OrderByDescending(w => w.WeighInTime)
            .ToListAsync();

    public async Task<List<WeighingRecord>> GetByLicensePlateAsync(string plate)
        => await _dbSet
            .Include(w => w.Product)
            .Include(w => w.Company)
            .Where(w => w.LicensePlate.Contains(plate))
            .OrderByDescending(w => w.WeighInTime)
            .ToListAsync();

    public async Task<WeighingRecord?> GetByTicketNumberAsync(string ticketNumber)
        => await _dbSet
            .Include(w => w.Product)
            .Include(w => w.Company)
            .FirstOrDefaultAsync(w => w.TicketNumber == ticketNumber);

    public async Task<List<WeighingRecord>> GetActiveWeighingsAsync()
        => await _dbSet
            .Include(w => w.Product)
            .Include(w => w.Company)
            .Where(w => w.Status == WeighingStatus.WeighingIn || w.Status == WeighingStatus.WeighingOut)
            .OrderByDescending(w => w.WeighInTime)
            .ToListAsync();

    public async Task<List<WeighingRecord>> GetTodayWeighingsAsync()
        => await _dbSet
            .Include(w => w.Product)
            .Include(w => w.Company)
            .Where(w => w.WeighInTime >= DateTime.Today)
            .OrderByDescending(w => w.WeighInTime)
            .ToListAsync();

    public async Task<WeighingRecord?> GetActiveWeighingByPlateAsync(string plate)
        => await _dbSet
            .Include(w => w.Product)
            .Include(w => w.Company)
            .Where(w => w.LicensePlate == plate && w.Status == WeighingStatus.WeighingIn)
            .OrderByDescending(w => w.WeighInTime)
            .FirstOrDefaultAsync();

    public override async Task<List<WeighingRecord>> GetAllAsync()
        => await _dbSet
            .Include(w => w.Product)
            .Include(w => w.Company)
            .OrderByDescending(w => w.WeighInTime)
            .ToListAsync();

    public override async Task<WeighingRecord?> GetByIdAsync(int id)
        => await _dbSet
            .Include(w => w.Product)
            .Include(w => w.Company)
            .FirstOrDefaultAsync(w => w.Id == id);

    private IQueryable<WeighingRecord> ApplyFilters(
        IQueryable<WeighingRecord> query,
        string? plate, string? ticket,
        DateTime? from, DateTime? to,
        WeighingStatus? status,
        int? productId, int? companyId,
        ExportFilter exportFilter)
    {
        if (!string.IsNullOrWhiteSpace(plate))
            query = query.Where(w => w.LicensePlate.Contains(plate.Trim().ToUpper()));

        if (!string.IsNullOrWhiteSpace(ticket))
            query = query.Where(w => w.TicketNumber.Contains(ticket.Trim()));

        if (from.HasValue)
            query = query.Where(w => w.WeighInTime >= from.Value);

        if (to.HasValue)
            query = query.Where(w => w.WeighInTime <= to.Value.AddDays(1));

        if (status.HasValue)
            query = query.Where(w => w.Status == status.Value);

        if (productId.HasValue)
            query = query.Where(w => w.ProductId == productId.Value);

        if (companyId.HasValue)
            query = query.Where(w => w.CompanyId == companyId.Value);

        return exportFilter switch
        {
            ExportFilter.Pending => query.Where(w => w.ExportPending),
            ExportFilter.Exported => query.Where(w => !w.ExportPending),
            _ => query
        };
    }

    // Sin seguimiento: el contexto del ViewModel vive mucho tiempo y mostraría el estado de exportación viejo.
    public async Task<PagedResult<WeighingRecord>> GetPagedAsync(
        int page, int pageSize,
        string? plate, string? ticket,
        DateTime? from, DateTime? to,
        WeighingStatus? status,
        int? productId, int? companyId,
        ExportFilter exportFilter = ExportFilter.All)
    {
        var query = ApplyFilters(
            _dbSet.AsNoTracking().Include(w => w.Product).Include(w => w.Company),
            plate, ticket, from, to, status, productId, companyId, exportFilter);

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderByDescending(w => w.WeighInTime)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PagedResult<WeighingRecord>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    // Los Id de todo el filtro, en todas las páginas: para "seleccionar todos".
    public async Task<List<int>> GetFilteredIdsAsync(
        string? plate, string? ticket,
        DateTime? from, DateTime? to,
        WeighingStatus? status,
        int? productId, int? companyId,
        ExportFilter exportFilter = ExportFilter.All)
        => await ApplyFilters(_dbSet.AsNoTracking(), plate, ticket, from, to, status, productId, companyId, exportFilter)
            .OrderByDescending(w => w.WeighInTime)
            .Select(w => w.Id)
            .ToListAsync();
}
