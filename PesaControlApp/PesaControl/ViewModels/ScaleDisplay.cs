using CommunityToolkit.Mvvm.ComponentModel;

namespace PesaControl.UI.ViewModels;

public partial class ScaleDisplay : ObservableObject
{
    [ObservableProperty] private int _id;
    [ObservableProperty] private string _name = "";

    [ObservableProperty] private decimal _weight;
    [ObservableProperty] private bool _isStable;
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private string _statusText = "Desconectado";

    public ScaleDisplay(int id, string name)
    {
        _id = id;
        _name = name;
    }
}
