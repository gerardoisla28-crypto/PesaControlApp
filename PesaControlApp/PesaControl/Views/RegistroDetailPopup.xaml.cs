using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PesaControl.Core.Models;
using PesaControl.UI.Services;
using PesaControl.UI.Views;

namespace PesaControl.UI.Views;

public partial class RegistroDetailPopup : Window
{
    public string TicketInfo { get; }
    public string TicketNumber { get; }
    public string LicensePlate { get; }
    public string ProductName { get; }
    public string CompanyName { get; }
    public string OperatorName { get; }
    public string DriverName { get; }
    public string GrossWeight { get; }
    public string TareWeight { get; }
    public string NetWeight { get; }
    public string EntryDate { get; }
    public string ExitDate { get; }
    public string Notes { get; }
    public string StatusText { get; }
    public Brush StatusColor { get; }
    public WeighingRecord Record { get; }
    public bool HasPhotos =>
        (!string.IsNullOrEmpty(Record.PhotoPath) && File.Exists(Record.PhotoPath)) ||
        (!string.IsNullOrEmpty(Record.PhotoPathExit) && File.Exists(Record.PhotoPathExit));

    public RegistroDetailPopup(WeighingRecord record)
    {
        Record = record;
        TicketInfo = $"Ticket: {record.TicketNumber}";
        TicketNumber = record.TicketNumber;
        LicensePlate = record.LicensePlate;
        ProductName = record.Product?.Name ?? "-";
        CompanyName = record.Company?.Name ?? "-";
        OperatorName = string.IsNullOrWhiteSpace(record.OperatorName) ? "-" : record.OperatorName;
        DriverName = string.IsNullOrWhiteSpace(record.DriverName) ? "-" : record.DriverName;
        GrossWeight = record.GrossWeight.ToString("N3");
        TareWeight = record.TareWeight.ToString("N3");
        NetWeight = record.NetWeight.ToString("N3");
        EntryDate = record.WeighInTime.ToString("dd/MM/yyyy HH:mm");
        ExitDate = record.WeighOutTime?.ToString("dd/MM/yyyy HH:mm") ?? "Pendiente";
        Notes = string.IsNullOrWhiteSpace(record.Notes) ? "Sin notas" : record.Notes;

        StatusText = record.Status switch
        {
            WeighingStatus.WeighingIn => "Pesando entrada",
            WeighingStatus.WeighingOut => "Pesando salida",
            WeighingStatus.Completed => "Completado",
            WeighingStatus.Cancelled => "Cancelado",
            _ => record.Status.ToString()
        };

        StatusColor = record.Status switch
        {
            WeighingStatus.Completed => new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)),
            WeighingStatus.WeighingIn => new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00)),
            WeighingStatus.WeighingOut => new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3)),
            WeighingStatus.Cancelled => new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36)),
            _ => new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA))
        };

        InitializeComponent();
        DataContext = this;
    }

    private void PrintTicket_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new PrintDialog();
        if (dlg.ShowDialog() == true)
        {
            var printer = new WindowsPrinter(dlg.PrintQueue.Name);
            _ = printer.PrintAsync(Record, new TicketTemplate { Header = "PesaControl" });
        }
    }

    private void ShowPhotos_Click(object sender, RoutedEventArgs e)
    {
        var fotoPopup = new FotoPopup(Record);
        fotoPopup.Owner = this;
        fotoPopup.ShowDialog();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
