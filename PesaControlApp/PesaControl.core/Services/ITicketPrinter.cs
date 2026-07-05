using PesaControl.Core.Models;

namespace PesaControl.Core.Services;

public interface ITicketPrinter
{
    string Name { get; }
    bool IsAvailable { get; }
    Task<bool> PrintAsync(WeighingRecord record, TicketTemplate? template);
    Task<bool> TestAsync();
}
