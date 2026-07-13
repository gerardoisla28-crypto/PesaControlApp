using System.Windows.Controls;
using System.Windows.Input;
using PesaControl.Core.Models;
using PesaControl.UI.ViewModels;

namespace PesaControl.UI.Views;

public partial class RegistrosView : UserControl
{
    public RegistrosView()
    {
        InitializeComponent();
    }

    private void ListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is RegistrosViewModel vm && vm.SelectedRecord is not null)
        {
            vm.ShowDetailCommand.Execute(vm.SelectedRecord);
        }
    }
}
