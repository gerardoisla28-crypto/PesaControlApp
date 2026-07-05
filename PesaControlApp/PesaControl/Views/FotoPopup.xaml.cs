using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PesaControl.Core.Models;

namespace PesaControl.UI.Views;

public partial class FotoPopup : Window
{
    public string TicketInfo { get; }
    public bool HasEntryPhoto { get; }
    public bool HasExitPhoto { get; }
    public bool NoPhotos => !HasEntryPhoto && !HasExitPhoto;
    public ImageSource? EntryPhotoSource { get; }
    public ImageSource? ExitPhotoSource { get; }

    public FotoPopup(WeighingRecord record)
    {
        TicketInfo = $"Ticket: {record.TicketNumber} — {record.LicensePlate}";

        if (!string.IsNullOrEmpty(record.PhotoPath) && File.Exists(record.PhotoPath))
        {
            HasEntryPhoto = true;
            EntryPhotoSource = LoadImage(record.PhotoPath);
        }

        if (!string.IsNullOrEmpty(record.PhotoPathExit) && File.Exists(record.PhotoPathExit))
        {
            HasExitPhoto = true;
            ExitPhotoSource = LoadImage(record.PhotoPathExit);
        }

        InitializeComponent();
        DataContext = this;
    }

    private static BitmapImage LoadImage(string path)
    {
        var img = new BitmapImage();
        img.BeginInit();
        img.CacheOption = BitmapCacheOption.OnLoad;
        img.UriSource = new Uri(path, UriKind.Absolute);
        img.EndInit();
        img.Freeze();
        return img;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
