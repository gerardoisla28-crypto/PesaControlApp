using PesaControl.Core.Models;

namespace PesaControl.Core.Services;

public interface IWeighingProtocol
{
    string Name { get; }
    bool IsConnected { get; }

    event EventHandler<WeightReading> WeightChanged;
    event EventHandler<bool> ConnectionStatusChanged;
    event EventHandler<string> ErrorOccurred;

    Task ConnectAsync(CommunicationConfig config);
    Task DisconnectAsync();
    Task<WeightReading> ReadWeightAsync();
    Task<bool> TareAsync();
    Task<bool> ZeroAsync();
}
