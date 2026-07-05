using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PesaControl.Core.Models;

namespace PesaControl.UI.ViewModels;

public partial class ControlsViewModel : ObservableObject
{
    private bool _isSyncing;

    public BarrierViewModel BarrierVM { get; }
    public TrafficLightViewModel TrafficLightVM { get; }

    public ControlsViewModel(BarrierViewModel barrierVm, TrafficLightViewModel trafficLightVm)
    {
        BarrierVM = barrierVm;
        TrafficLightVM = trafficLightVm;

        BarrierVM.PropertyChanged += OnBarrierPropertyChanged;
    }

    private async void OnBarrierPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(BarrierViewModel.IsConnected):
                if (BarrierVM.IsConnected)
                {
                    // Auto-connect traffic light when barrier connects
                    if (!TrafficLightVM.IsConnected)
                        await TrafficLightVM.ConnectFirstOrDefaultAsync();
                    await SyncTrafficLightAsync();
                }
                else
                {
                    // Disconnect traffic light when barrier disconnects
                    await TrafficLightVM.SetColorSafeAsync(TrafficLightColor.Off);
                    if (TrafficLightVM.IsConnected)
                        TrafficLightVM.DisconnectCommand.Execute(null);
                }
                break;

            case nameof(BarrierViewModel.IsOpen):
                await SyncTrafficLightAsync();
                break;
        }
    }

    private async Task SyncTrafficLightAsync()
    {
        if (_isSyncing) return;
        _isSyncing = true;

        try
        {
            if (BarrierVM.IsConnected)
            {
                var color = BarrierVM.IsOpen
                    ? TrafficLightColor.Green
                    : TrafficLightColor.Red;
                await TrafficLightVM.SetColorSafeAsync(color);
            }
        }
        finally
        {
            _isSyncing = false;
        }
    }
}
