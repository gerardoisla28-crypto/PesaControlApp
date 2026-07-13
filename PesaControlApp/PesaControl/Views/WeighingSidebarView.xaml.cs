using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PesaControl.UI.ViewModels;

namespace PesaControl.UI.Views;

public partial class WeighingSidebarView : UserControl
{
    public WeighingSidebarView()
    {
        InitializeComponent();
    }

    private void CameraPreview_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not WeighingViewModel vm || vm.CameraVM?.CurrentFrame == null)
            return;

        var popup = new CameraPreviewPopup(vm.CameraVM.CurrentFrame, vm.CameraVM.SelectedCamera?.Name ?? "Cámara");
        popup.Owner = Window.GetWindow(this);
        popup.ShowDialog();
    }
}
