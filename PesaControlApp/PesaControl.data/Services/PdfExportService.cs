using PesaControl.Core.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PesaControl.Data.Services;

public class PdfExportService
{
    static PdfExportService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public async Task<byte[]> ExportAsync(List<WeighingRecord> records)
    {
        return await Task.Run(() =>
        {
            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.Margin(20);
                    page.DefaultTextStyle(x => x.FontSize(9));

                    page.Header().Element(c => ComposeHeader(c, records.Count));
                    page.Content().Element(c => ComposeContent(c, records));
                    page.Footer().AlignCenter().Text(text =>
                    {
                        text.Span("PesaControl - ");
                        text.CurrentPageNumber();
                        text.Span(" / ");
                        text.TotalPages();
                    });
                });
            });

            using var stream = new MemoryStream();
            document.GeneratePdf(stream);
            return stream.ToArray();
        });
    }

    private static void ComposeHeader(IContainer container, int totalRecords)
    {
        container.Column(col =>
        {
            col.Item().Text("Historial de Pesajes")
                .FontSize(16).Bold().FontColor(Colors.Blue.Darken3);

            col.Item().Text($"Total de registros: {totalRecords} — Generado: {DateTime.Now:dd/MM/yyyy HH:mm}")
                .FontSize(9).FontColor(Colors.Grey.Darken2);

            col.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
        });
    }

    private static void ComposeContent(IContainer container, List<WeighingRecord> records)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(cols =>
            {
                cols.RelativeColumn(1.2f);
                cols.RelativeColumn(1.5f);
                cols.RelativeColumn(1.5f);
                cols.RelativeColumn(1.2f);
                cols.RelativeColumn(1.5f);
                cols.RelativeColumn(1.5f);
                cols.RelativeColumn(1.2f);
                cols.RelativeColumn(1.2f);
                cols.RelativeColumn(1.2f);
                cols.RelativeColumn(1.2f);
                cols.RelativeColumn(1.2f);
                cols.RelativeColumn(1.2f);
            });

            string[] headers =
            [
                "Ticket", "Fecha Entrada", "Fecha Salida", "Patente",
                "Producto", "Empresa", "Operador", "Conductor",
                "Peso Ent.", "Peso Sal.", "Peso Neto", "Estado"
            ];

            foreach (var header in headers)
            {
                table.Cell().Element(CellStyle).Text(header).Bold().FontSize(8);
            }

            foreach (var r in records)
            {
                table.Cell().Element(DataCellStyle).Text(r.TicketNumber).FontSize(8);
                table.Cell().Element(DataCellStyle).Text(r.WeighInTime.ToString("dd/MM/yyyy HH:mm")).FontSize(8);
                table.Cell().Element(DataCellStyle).Text(r.WeighOutTime?.ToString("dd/MM/yyyy HH:mm") ?? "-").FontSize(8);
                table.Cell().Element(DataCellStyle).Text(r.LicensePlate).FontSize(8);
                table.Cell().Element(DataCellStyle).Text(r.Product?.Name ?? "-").FontSize(8);
                table.Cell().Element(DataCellStyle).Text(r.Company?.Name ?? "-").FontSize(8);
                table.Cell().Element(DataCellStyle).Text(r.OperatorName).FontSize(8);
                table.Cell().Element(DataCellStyle).Text(r.DriverName).FontSize(8);
                table.Cell().Element(DataCellStyle).Text(r.GrossWeight.ToString("N3")).FontSize(8);
                table.Cell().Element(DataCellStyle).Text(r.TareWeight.ToString("N3")).FontSize(8);
                table.Cell().Element(DataCellStyle).Text(r.NetWeight.ToString("N3")).FontSize(8);
                table.Cell().Element(DataCellStyle).Text(StatusText(r.Status)).FontSize(8);
            }
        });
    }

    private static string StatusText(WeighingStatus status) => status switch
    {
        WeighingStatus.WeighingIn => "Pesando entrada",
        WeighingStatus.WeighingOut => "Pesando salida",
        WeighingStatus.Completed => "Completado",
        WeighingStatus.Cancelled => "Cancelado",
        _ => status.ToString()
    };

    private static IContainer CellStyle(IContainer container)
    {
        return container.Border(0.5f).BorderColor(Colors.Grey.Lighten2)
            .Padding(3).Background(Colors.Blue.Lighten5);
    }

    private static IContainer DataCellStyle(IContainer container)
    {
        return container.Border(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(3);
    }
}
