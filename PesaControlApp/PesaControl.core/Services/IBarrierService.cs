using PesaControl.Core.Models;

namespace PesaControl.Core.Services;

public interface IBarrierService
{
    string Name { get; }
    bool IsConnected { get; }

    event EventHandler<bool> ConnectionStatusChanged;
    event EventHandler<string> ErrorOccurred;

    Task ConnectAsync(BarrierConfig config);
    Task DisconnectAsync();
    Task OpenAsync();
    Task CloseAsync();
    Task<bool> GetStatusAsync();
}
