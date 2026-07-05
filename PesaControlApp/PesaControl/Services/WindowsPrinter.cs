using System.Drawing;
using System.Drawing.Printing;
using System.Text;
using PesaControl.Core.Models;
using PesaControl.Core.Services;

namespace PesaControl.UI.Services;

public class WindowsPrinter : ITicketPrinter
{
    private readonly string _printerName;

    public string Name => $"Windows ({_printerName})";
    public bool IsAvailable => PrinterSettings.InstalledPrinters.Cast<string>().Contains(_printerName);

    public WindowsPrinter(string printerName)
    {
        _printerName = printerName;
    }

    public Task<bool> PrintAsync(WeighingRecord record, TicketTemplate? template)
    {
        try
        {
            var doc = new PrintDocument
            {
                PrinterSettings = new PrinterSettings { PrinterName = _printerName },
                DocumentName = $"Ticket-{record.TicketNumber}"
            };

            var content = BuildTicketText(record, template);
            doc.PrintPage += (s, e) =>
            {
                var font = new Font("Courier New", 10);
                var y = e.MarginBounds.Top;
                foreach (var line in content.Split('\n'))
                {
                    e.Graphics?.DrawString(line.TrimEnd('\r'), font, Brushes.Black, e.MarginBounds.Left, y);
                    y += font.Height + 2;
                }
                e.HasMorePages = false;
            };

            doc.Print();
            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    public Task<bool> TestAsync()
    {
        try
        {
            var doc = new PrintDocument
            {
                PrinterSettings = new PrinterSettings { PrinterName = _printerName },
                DocumentName = "PesaControl Test"
            };

            doc.PrintPage += (s, e) =>
            {
                var font = new Font("Courier New", 12);
                e.Graphics?.DrawString("PESACONTROL PRO - Test de impresión", font, Brushes.Black, 10, 10);
                e.HasMorePages = false;
            };

            doc.Print();
            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    private static string BuildTicketText(WeighingRecord record, TicketTemplate? template)
    {
        var sb = new StringBuilder();
        sb.AppendLine(template?.Header ?? "PESACONTROL PRO");
        sb.AppendLine(new string('=', 40));
        sb.AppendLine($"Ticket:    {record.TicketNumber}");
        sb.AppendLine($"Fecha:     {record.WeighInTime:dd/MM/yyyy HH:mm}");
        sb.AppendLine($"Patente:   {record.LicensePlate}");
        sb.AppendLine($"País:      {record.PlateCountry}");
        sb.AppendLine($"Operador:  {record.OperatorName}");
        sb.AppendLine($"Conductor: {record.DriverName}");
        sb.AppendLine($"Producto:  {record.Product?.Name ?? "-"}");
        sb.AppendLine($"Empresa:   {record.Company?.Name ?? "-"}");
        sb.AppendLine(new string('-', 40));
            sb.AppendLine($"Peso entrada: {record.GrossWeight,10:F3} kg");
            sb.AppendLine($"Peso salida:  {record.TareWeight,10:F3} kg");
            sb.AppendLine($"Total:        {record.NetWeight,10:F3} kg");
        sb.AppendLine(new string('=', 40));
        sb.AppendLine(template?.Footer ?? "Gracias por su preferencia");
        return sb.ToString();
    }
}
