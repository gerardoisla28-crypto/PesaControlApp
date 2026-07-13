using PesaControl.Core.Models;

namespace PesaControl.Core.Services;

public interface IWeighingDataService
{
    Task<WeighingRecord> CreateAsync(WeighingRecord record);
    Task UpdateAsync(WeighingRecord record);
    Task<WeighingRecord?> GetByIdAsync(int id);
    Task<WeighingRecord?> GetByTicketNumberAsync(string ticketNumber);
    Task<WeighingRecord?> GetActiveByPlateAsync(string plate);
    Task<List<WeighingRecord>> GetActiveAsync();
    Task<List<WeighingRecord>> GetTodayAsync();
    Task<string> GetNextTicketAsync();

    Task<List<Product>> GetProductsAsync();
    Task<List<Company>> GetCompaniesAsync();
    Task<List<Operator>> GetOperatorsAsync();
    Task<List<Driver>> GetDriversAsync();
}
