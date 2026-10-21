using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PesaControl.Core.Models;
using PesaControl.UI.ViewModels;

namespace PesaControl.UI.Views;

public partial class RegistrosView : UserControl
{
    public RegistrosView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => ApplyPermissions();
        Loaded += (_, _) => ApplyPermissions();
    }

    // Quien no puede exportar ni importar no ve las columnas de casilla y de estado de exportación.
    private void ApplyPermissions()
    {
        if (DataContext is RegistrosViewModel { CanExchange: false })
        {
            CheckColumn.Width = 0;
            ExportColumn.Width = 0;
        }
    }

    private void ListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Pulsar rápido la casilla no debe abrir el detalle del pesaje.
        if (e.OriginalSource is DependencyObject source && IsInsideCheckBox(source)) return;

        if (DataContext is RegistrosViewModel vm && vm.SelectedRecord is not null)
        {
            vm.ShowDetailCommand.Execute(vm.SelectedRecord);
        }
    }

    private static bool IsInsideCheckBox(DependencyObject element)
    {
        for (var current = element; current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is CheckBox) return true;
        return false;
    }
}
