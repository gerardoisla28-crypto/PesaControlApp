using System.Windows;
using System.Windows.Media;

namespace PesaControl.UI.Views;

public partial class CameraPreviewPopup : Window
{
    public new string Title { get; }
    public ImageSource FrameSource { get; }

    public CameraPreviewPopup(ImageSource frame, string cameraName)
    {
        Title = $"Cámara — {cameraName}";
        FrameSource = frame;
        InitializeComponent();
        DataContext = this;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
