using PesaControl.Core.Models;

namespace PesaControl.Data.Repositories;

public interface IWeighingRepository : IRepository<WeighingRecord>
{
    Task<List<WeighingRecord>> GetByDateRangeAsync(DateTime from, DateTime to);
    Task<List<WeighingRecord>> GetByLicensePlateAsync(string plate);
    Task<WeighingRecord?> GetByTicketNumberAsync(string ticketNumber);
    Task<List<WeighingRecord>> GetActiveWeighingsAsync();
    Task<List<WeighingRecord>> GetTodayWeighingsAsync();
    Task<WeighingRecord?> GetActiveWeighingByPlateAsync(string plate);
    Task<string> GetNextTicketNumberAsync();
}
