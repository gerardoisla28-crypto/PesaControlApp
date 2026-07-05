using System.Windows.Controls;

namespace PesaControl.UI.Views;

public partial class WeighingView : UserControl
{
    public WeighingView()
    {
        InitializeComponent();
    }

    public WeighingView(object viewModel) : this()
    {
        DataContext = viewModel;
    }
}
