using CommunityToolkit.Mvvm.ComponentModel;

namespace PesaControl.UI.ViewModels;

public partial class PlaceholderViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    private string _message = "Próximamente disponible";
}
