using ClosedXML.Excel;
using PesaControl.Core.Models;

namespace PesaControl.Data.Services;

public class ExcelExportService
{
    public async Task<byte[]> ExportAsync(List<WeighingRecord> records)
    {
        return await Task.Run(() =>
        {
            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("Registros");

            ws.Cell(1, 1).Value = "Ticket";
            ws.Cell(1, 2).Value = "Fecha Entrada";
            ws.Cell(1, 3).Value = "Fecha Salida";
            ws.Cell(1, 4).Value = "Patente";
            ws.Cell(1, 5).Value = "Producto";
            ws.Cell(1, 6).Value = "Empresa";
            ws.Cell(1, 7).Value = "Operador";
            ws.Cell(1, 8).Value = "Conductor";
            ws.Cell(1, 9).Value = "Peso Entrada (kg)";
            ws.Cell(1, 10).Value = "Peso Salida (kg)";
            ws.Cell(1, 11).Value = "Peso Neto (kg)";
            ws.Cell(1, 12).Value = "Estado";

            var headerRange = ws.Range(1, 1, 1, 12);
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Fill.BackgroundColor = XLColor.FromArgb(0x1E3A5F);
            headerRange.Style.Font.FontColor = XLColor.White;
            headerRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            headerRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;

            int row = 2;
            foreach (var r in records)
            {
                ws.Cell(row, 1).Value = r.TicketNumber;
                ws.Cell(row, 2).Value = r.WeighInTime.ToString("dd/MM/yyyy HH:mm");
                ws.Cell(row, 3).Value = r.WeighOutTime?.ToString("dd/MM/yyyy HH:mm") ?? "";
                ws.Cell(row, 4).Value = r.LicensePlate;
                ws.Cell(row, 5).Value = r.Product?.Name ?? "";
                ws.Cell(row, 6).Value = r.Company?.Name ?? "";
                ws.Cell(row, 7).Value = r.OperatorName;
                ws.Cell(row, 8).Value = r.DriverName;
                ws.Cell(row, 9).Value = (double)r.GrossWeight;
                ws.Cell(row, 10).Value = (double)r.TareWeight;
                ws.Cell(row, 11).Value = (double)r.NetWeight;
                ws.Cell(row, 12).Value = r.Status switch
                {
                    WeighingStatus.WeighingIn => "Pesando entrada",
                    WeighingStatus.WeighingOut => "Pesando salida",
                    WeighingStatus.Completed => "Completado",
                    WeighingStatus.Cancelled => "Cancelado",
                    _ => r.Status.ToString()
                };

                var dataRange = ws.Range(row, 1, row, 12);
                dataRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                dataRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;

                if (row % 2 == 0)
                    dataRange.Style.Fill.BackgroundColor = XLColor.FromArgb(0xF2F2F2);

                row++;
            }

            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        });
    }
}
