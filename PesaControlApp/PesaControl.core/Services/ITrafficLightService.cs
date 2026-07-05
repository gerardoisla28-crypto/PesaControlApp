using PesaControl.Core.Models;

namespace PesaControl.Core.Services;

public interface ITrafficLightService
{
    string Name { get; }
    bool IsConnected { get; }

    event EventHandler<bool> ConnectionStatusChanged;
    event EventHandler<string> ErrorOccurred;

    Task ConnectAsync(TrafficLightConfig config);
    Task DisconnectAsync();
    Task SetColorAsync(TrafficLightColor color);
    Task<TrafficLightColor> GetColorAsync();
}
