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

    public async Task<string> GetNextTicketNumberAsync()
    {
        var prefix = $"{DateTime.Now.Year}-{DateTime.Now.Month:D2}-";
        var last = await _dbSet
            .Where(w => w.TicketNumber.StartsWith(prefix))
            .OrderByDescending(w => w.TicketNumber)
            .FirstOrDefaultAsync();

        int nextNumber = 1;
        if (last != null && int.TryParse(last.TicketNumber[prefix.Length..], out int lastNum))
            nextNumber = lastNum + 1;

        return $"{prefix}{nextNumber:D4}";
    }

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

    public async Task<PagedResult<WeighingRecord>> GetPagedAsync(
        int page, int pageSize,
        string? plate, string? ticket,
        DateTime? from, DateTime? to,
        WeighingStatus? status,
        int? productId, int? companyId)
    {
        var query = _dbSet
            .Include(w => w.Product)
            .Include(w => w.Company)
            .AsQueryable();

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
}
