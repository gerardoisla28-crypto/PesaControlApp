using System.Runtime.InteropServices;
using System.Text;
using PesaControl.Core.Models;

namespace PesaControl.Core.Services;

public class EscPosPrinter : ITicketPrinter
{
    private readonly string _printerName;

    public string Name => $"ESC/POS ({_printerName})";
    public bool IsAvailable
    {
        get
        {
            if (!OpenPrinter(_printerName, out var hPrinter, IntPtr.Zero))
                return false;
            ClosePrinter(hPrinter);
            return true;
        }
    }

    public EscPosPrinter(string printerName = "LPT1")
    {
        _printerName = printerName;
    }

    public async Task<bool> PrintAsync(WeighingRecord record, TicketTemplate? template)
    {
        var data = BuildTicketData(record, template);
        return await Task.Run(() => SendRawData(data));
    }

    public async Task<bool> TestAsync()
    {
        var test = Encoding.ASCII.GetBytes("\x1B\x40\x1B\x61\x01TEST PRINT\x0A\x1D\x56\x00");
        return await Task.Run(() => SendRawData(test));
    }

    private bool SendRawData(byte[] data)
    {
        if (!OpenPrinter(_printerName, out var hPrinter, IntPtr.Zero))
            return false;

        try
        {
            var docInfo = new DOC_INFO_1
            {
                pDocName = "PesaControl Ticket",
                pOutputFile = null,
                pDatatype = "RAW"
            };

            if (!StartDocPrinter(hPrinter, 1, ref docInfo))
                return false;

            if (!StartPagePrinter(hPrinter))
                return false;

            var pBytes = Marshal.AllocHGlobal(data.Length);
            try
            {
                Marshal.Copy(data, 0, pBytes, data.Length);
                if (!WritePrinter(hPrinter, pBytes, data.Length, out var written))
                    return false;
                return written == data.Length;
            }
            finally
            {
                Marshal.FreeHGlobal(pBytes);
                EndPagePrinter(hPrinter);
                EndDocPrinter(hPrinter);
            }
        }
        finally
        {
            ClosePrinter(hPrinter);
        }
    }

    private static byte[] BuildTicketData(WeighingRecord record, TicketTemplate? template)
    {
        var sb = new StringBuilder();

        sb.Append("\x1B\x40");

        sb.Append("\x1B\x61\x01");
        sb.AppendLine(template?.Header ?? "PESACONTROL PRO");
        sb.AppendLine(new string('=', 32));

        sb.Append("\x1B\x61\x00");
        sb.AppendLine($"Ticket:  {record.TicketNumber}");
        sb.AppendLine($"Fecha:   {record.WeighInTime:dd/MM/yyyy HH:mm}");
        sb.AppendLine($"Patente: {record.LicensePlate}");
        sb.AppendLine($"Pa\u00eds:    {record.PlateCountry}");
        sb.AppendLine($"Operador:  {record.OperatorName}");
        sb.AppendLine($"Conductor: {record.DriverName}");
        sb.AppendLine($"Producto: {record.Product?.Name ?? "-"}");
        sb.AppendLine($"Empresa: {record.Company?.Name ?? "-"}");
        sb.AppendLine(new string('-', 32));

        sb.Append("\x1B\x61\x01");
        sb.AppendLine("PESOS");
        sb.Append("\x1B\x61\x00");
            sb.AppendLine($"Peso entrada: {record.GrossWeight,10:F3} kg");
            sb.AppendLine($"Peso salida:  {record.TareWeight,10:F3} kg");
            sb.AppendLine($"Total:        {record.NetWeight,10:F3} kg");
        sb.AppendLine(new string('=', 32));

        sb.Append("\x1B\x61\x01");
        sb.AppendLine(template?.Footer ?? "Gracias por su preferencia");

        sb.Append("\x1D\x56\x00");

        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, IntPtr pDefault);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool StartDocPrinter(IntPtr hPrinter, int level, ref DOC_INFO_1 pDocInfo);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool EndDocPrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool StartPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool EndPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool WritePrinter(IntPtr hPrinter, IntPtr pBytes, int dwCount, out int dwWritten);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DOC_INFO_1
    {
        public string pDocName;
        public string? pOutputFile;
        public string pDatatype;
    }
}
