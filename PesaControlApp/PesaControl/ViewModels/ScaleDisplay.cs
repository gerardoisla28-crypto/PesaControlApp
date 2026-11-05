using CommunityToolkit.Mvvm.ComponentModel;

namespace PesaControl.UI.ViewModels;

public partial class ScaleDisplay : ObservableObject
{
    [ObservableProperty] private int _id;
    [ObservableProperty] private string _name = "";

    [ObservableProperty, NotifyPropertyChangedFor(nameof(WeightText))] private decimal _weight;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(WeightText))] private int _decimals;
    public string WeightText => PesaControl.Core.Models.WeightFormat.Format(Weight, Decimals);
    [ObservableProperty] private bool _isStable;
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private string _statusText = "Desconectado";

    public ScaleDisplay(int id, string name)
    {
        _id = id;
        _name = name;
    }
}
