using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PesaControl.Core.Models;
using PesaControl.Data.Data;
using PesaControl.Data.Repositories;
using PesaControl.Data.Services;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.Painting.Effects;
using SkiaSharp;

namespace PesaControl.UI.ViewModels;

public partial class ReportesViewModel : ObservableObject
{
    private readonly DatabaseService _dbService;
    private List<WeighingRecord> _allRecords = [];

    [ObservableProperty] private DateTime? _filterFrom;
    [ObservableProperty] private DateTime? _filterTo;
    [ObservableProperty] private Product? _filterProduct;
    [ObservableProperty] private Company? _filterCompany;
    [ObservableProperty] private string _statusMessage = "Listo";

    public ObservableCollection<Product> Products { get; } = [];
    public ObservableCollection<Company> Companies { get; } = [];

    [ObservableProperty] private string _totalRecords = "0";
    [ObservableProperty] private string _totalNetWeight = "0 kg";
    [ObservableProperty] private string _avgNetWeight = "0 kg";
    [ObservableProperty] private string _periodText = "";

    [ObservableProperty] private ISeries[] _dailySeries = [];
    [ObservableProperty] private Axis[] _dailyXAxes = [];
    [ObservableProperty] private Axis[] _dailyYAxes = [];

    [ObservableProperty] private ISeries[] _productSeries = [];
    [ObservableProperty] private Axis[] _productXAxes = [];
    [ObservableProperty] private Axis[] _productYAxes = [];

    [ObservableProperty] private ISeries[] _statusSeries = [];

    [ObservableProperty] private ISeries[] _companySeries = [];
    [ObservableProperty] private Axis[] _companyXAxes = [];
    [ObservableProperty] private Axis[] _companyYAxes = [];

    public ReportesViewModel(DatabaseService dbService)
    {
        _dbService = dbService;
        StatusMessage = "Cargando datos...";
        _ = LoadInitialAsync();
    }

    partial void OnFilterProductChanged(Product? value) => RefreshCharts();
    partial void OnFilterCompanyChanged(Company? value) => RefreshCharts();
    partial void OnFilterFromChanged(DateTime? value) => RefreshCharts();
    partial void OnFilterToChanged(DateTime? value) => RefreshCharts();

    private async Task LoadInitialAsync()
    {
        try
        {
            using var ctx = _dbService.CreateContext();

            var products = await new Repository<Product>(ctx).GetAllAsync();
            var companies = await new Repository<Company>(ctx).GetAllAsync();

            App.Current.Dispatcher.Invoke(() =>
            {
                Products.Clear();
                Products.Add(new Product { Id = 0, Name = "Todos" });
                foreach (var p in products.OrderBy(x => x.Name)) Products.Add(p);

                Companies.Clear();
                Companies.Add(new Company { Id = 0, Name = "Todas" });
                foreach (var c in companies.OrderBy(x => x.Name)) Companies.Add(c);
            });

            var repo = new WeighingRepository(ctx);
            _allRecords = await repo.GetAllAsync();

            App.Current.Dispatcher.Invoke(() =>
            {
                RefreshCharts();
                StatusMessage = $"Listo — {_allRecords.Count} registros cargados";
            });
        }
        catch (Exception ex)
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                StatusMessage = $"Error al cargar datos: {ex.Message}";
            });
        }
    }

    [RelayCommand]
    private void RefreshReport()
    {
        StatusMessage = "Actualizando...";
        RefreshCharts();
        StatusMessage = "Listo";
    }

    private void RefreshCharts()
    {
        var filtered = _allRecords.AsEnumerable();

        if (FilterProduct != null && FilterProduct.Id != 0)
            filtered = filtered.Where(r => r.ProductId == FilterProduct.Id);

        if (FilterCompany != null && FilterCompany.Id != 0)
            filtered = filtered.Where(r => r.CompanyId == FilterCompany.Id);

        if (FilterFrom.HasValue)
            filtered = filtered.Where(r => r.WeighInTime >= FilterFrom.Value);

        if (FilterTo.HasValue)
            filtered = filtered.Where(r => r.WeighInTime <= FilterTo.Value.AddDays(1));

        var records = filtered.OrderBy(r => r.WeighInTime).ToList();
        var completed = records.Where(r => r.Status == WeighingStatus.Completed).ToList();

        var totalKg = completed.Sum(r => (double)r.NetWeight);
        TotalRecords = records.Count.ToString("N0");
        TotalNetWeight = $"{totalKg:N0} kg";
        AvgNetWeight = completed.Count > 0
            ? $"{completed.Average(r => (double)r.NetWeight):N1} kg"
            : "0 kg";

        var minDate = records.Count > 0 ? records.Min(r => r.WeighInTime.Date) : DateTime.Today.AddDays(-30);
        var maxDate = records.Count > 0 ? records.Max(r => r.WeighInTime.Date) : DateTime.Today;
        PeriodText = $"{minDate:dd/MM/yy} - {maxDate:dd/MM/yy}";

        DailySeries = BuildDailySeries(records);
        DailyXAxes =
        [
            new Axis
            {
                LabelsPaint = new SolidColorPaint(SKColors.LightGray),
                SeparatorsPaint = new SolidColorPaint(new SKColor(60, 60, 60)),
                Labeler = value =>
                {
                    try { return new DateTime((long)value).ToString("dd/MMM"); }
                    catch { return ""; }
                },
                LabelsRotation = 45,
                TextSize = 11
            }
        ];
        DailyYAxes =
        [
            new Axis
            {
                LabelsPaint = new SolidColorPaint(SKColors.LightGray),
                SeparatorsPaint = new SolidColorPaint(new SKColor(60, 60, 60)) { StrokeThickness = 1, PathEffect = new DashEffect([3, 3]) },
                MinLimit = 0,
                TextSize = 11
            }
        ];

        var productLabels = BuildLabels(completed, isProduct: true);
        ProductSeries = BuildBarSeries(completed, isProduct: true,
            new SKColor(33, 150, 243), new SKColor(0, 150, 136));
        ProductXAxes =
        [
            new Axis
            {
                LabelsPaint = new SolidColorPaint(SKColors.LightGray),
                SeparatorsPaint = new SolidColorPaint(new SKColor(60, 60, 60)) { StrokeThickness = 1, PathEffect = new DashEffect([3, 3]) },
                MinLimit = 0,
                TextSize = 11
            }
        ];
        ProductYAxes =
        [
            new Axis
            {
                LabelsPaint = new SolidColorPaint(SKColors.LightGray),
                SeparatorsPaint = new SolidColorPaint(new SKColor(60, 60, 60)),
                TextSize = 11,
                Labeler = value =>
                {
                    var idx = (int)Math.Round(value);
                    return idx >= 0 && idx < productLabels.Length ? productLabels[idx] : "";
                }
            }
        ];

        var companyLabels = BuildLabels(completed, isProduct: false);
        CompanySeries = BuildBarSeries(completed, isProduct: false,
            new SKColor(156, 39, 176), new SKColor(33, 150, 243));
        CompanyXAxes =
        [
            new Axis
            {
                LabelsPaint = new SolidColorPaint(SKColors.LightGray),
                SeparatorsPaint = new SolidColorPaint(new SKColor(60, 60, 60)) { StrokeThickness = 1, PathEffect = new DashEffect([3, 3]) },
                MinLimit = 0,
                TextSize = 11
            }
        ];
        CompanyYAxes =
        [
            new Axis
            {
                LabelsPaint = new SolidColorPaint(SKColors.LightGray),
                SeparatorsPaint = new SolidColorPaint(new SKColor(60, 60, 60)),
                TextSize = 11,
                Labeler = value =>
                {
                    var idx = (int)Math.Round(value);
                    return idx >= 0 && idx < companyLabels.Length ? companyLabels[idx] : "";
                }
            }
        ];

        StatusSeries = BuildStatusSeries(records);
    }

    private static ISeries[] BuildDailySeries(List<WeighingRecord> records)
    {
        var daily = records
            .GroupBy(r => r.WeighInTime.Date)
            .OrderBy(g => g.Key)
            .Select(g => new DateTimePoint(g.Key, g.Count()))
            .ToList();

        return
        [
            new ColumnSeries<DateTimePoint>
            {
                Values = daily,
                Mapping = (point, index) => new(point.DateTime.Ticks, point.Value ?? 0),
                Fill = new SolidColorPaint(new SKColor(30, 136, 229)),
                Stroke = null,
                Name = "Pesajes"
            }
        ];
    }

    private static ISeries[] BuildBarSeries(List<WeighingRecord> completed, bool isProduct, SKColor startColor, SKColor endColor)
    {
        var grouped = completed
            .Where(r => isProduct ? r.Product != null : r.Company != null);

        var byKey = isProduct
            ? grouped.GroupBy(r => r.Product!.Name)
            : grouped.GroupBy(r => r.Company!.Name);

        var items = byKey
            .Select(g => new { Name = g.Key, TotalKg = g.Sum(r => (double)r.NetWeight) })
            .OrderByDescending(x => x.TotalKg)
            .Take(10)
            .ToList();

        var values = items.Select(x => x.TotalKg).ToList();

        return
        [
            new RowSeries<double>
            {
                Values = values,
                Fill = new LinearGradientPaint(startColor, endColor),
                Stroke = null,
                Name = "Kilogramos",
                DataLabelsPosition = DataLabelsPosition.Right,
                DataLabelsPaint = new SolidColorPaint(SKColors.White),
                DataLabelsSize = 12,
                DataLabelsFormatter = point => $"{point.Model:N0} kg"
            }
        ];
    }

    private static PieSeries<double>[] BuildStatusSeries(List<WeighingRecord> records)
    {
        var byStatus = records
            .GroupBy(r => r.Status)
            .ToDictionary(g => g.Key, g => g.Count());

        var colors = new Dictionary<WeighingStatus, SKColor>
        {
            [WeighingStatus.Completed] = new(76, 175, 80),
            [WeighingStatus.WeighingIn] = new(255, 152, 0),
            [WeighingStatus.WeighingOut] = new(33, 150, 243),
            [WeighingStatus.Cancelled] = new(244, 67, 54)
        };

        var total = records.Count;

        return byStatus.Select(kvp => new PieSeries<double>
        {
            Values = [kvp.Value],
            Name = kvp.Key switch
            {
                WeighingStatus.Completed => "Completado",
                WeighingStatus.WeighingIn => "Pesando entrada",
                WeighingStatus.WeighingOut => "Pesando salida",
                WeighingStatus.Cancelled => "Cancelado",
                _ => kvp.Key.ToString()
            },
            Fill = new SolidColorPaint(colors.GetValueOrDefault(kvp.Key, SKColors.Gray)),
            Stroke = new SolidColorPaint(new SKColor(37, 37, 38)) { StrokeThickness = 3 },
            DataLabelsPosition = PolarLabelsPosition.Outer,
            DataLabelsPaint = new SolidColorPaint(SKColors.LightGray),
            DataLabelsSize = 13,
            DataLabelsFormatter = point =>
            {
                var pct = total > 0 ? (double)point.Model / total * 100 : 0;
                return $"{point.Model} ({pct:N1}%)";
            },
            HoverPushout = 12
        }).ToArray();
    }

    private static string[] BuildLabels(List<WeighingRecord> completed, bool isProduct)
    {
        var grouped = completed
            .Where(r => isProduct ? r.Product != null : r.Company != null);

        var byKey = isProduct
            ? grouped.GroupBy(r => r.Product!.Name)
            : grouped.GroupBy(r => r.Company!.Name);

        return byKey
            .Select(g => new { Name = g.Key, TotalKg = g.Sum(r => (double)r.NetWeight) })
            .OrderByDescending(x => x.TotalKg)
            .Take(10)
            .Select(x => x.Name.Length > 20 ? x.Name[..20] + ".." : x.Name)
            .ToArray();
    }

    [RelayCommand]
    private async Task ExportToExcelAsync()
    {
        try
        {
            StatusMessage = "Exportando a Excel...";
            var records = GetFilteredRecords();
            if (records.Count == 0)
            {
                StatusMessage = "No hay registros para exportar";
                return;
            }

            var service = new ExcelExportService();
            var bytes = await service.ExportAsync(records);

            var dlg = new SaveFileDialog
            {
                FileName = $"Reporte_{DateTime.Now:yyyyMMdd_HHmm}.xlsx",
                Filter = "Excel Files (*.xlsx)|*.xlsx"
            };
            if (dlg.ShowDialog() != true) return;

            await File.WriteAllBytesAsync(dlg.FileName, bytes);
            StatusMessage = $"Exportado a {Path.GetFileName(dlg.FileName)}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error al exportar: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ExportToPdfAsync()
    {
        try
        {
            StatusMessage = "Exportando a PDF...";
            var records = GetFilteredRecords();
            if (records.Count == 0)
            {
                StatusMessage = "No hay registros para exportar";
                return;
            }

            var service = new PdfExportService();
            var bytes = await service.ExportAsync(records);

            var dlg = new SaveFileDialog
            {
                FileName = $"Reporte_{DateTime.Now:yyyyMMdd_HHmm}.pdf",
                Filter = "PDF Files (*.pdf)|*.pdf"
            };
            if (dlg.ShowDialog() != true) return;

            await File.WriteAllBytesAsync(dlg.FileName, bytes);
            StatusMessage = $"Exportado a {Path.GetFileName(dlg.FileName)}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error al exportar: {ex.Message}";
        }
    }

    private List<WeighingRecord> GetFilteredRecords()
    {
        var filtered = _allRecords.AsEnumerable();

        if (FilterProduct != null && FilterProduct.Id != 0)
            filtered = filtered.Where(r => r.ProductId == FilterProduct.Id);

        if (FilterCompany != null && FilterCompany.Id != 0)
            filtered = filtered.Where(r => r.CompanyId == FilterCompany.Id);

        if (FilterFrom.HasValue)
            filtered = filtered.Where(r => r.WeighInTime >= FilterFrom.Value);

        if (FilterTo.HasValue)
            filtered = filtered.Where(r => r.WeighInTime <= FilterTo.Value.AddDays(1));

        return filtered.OrderByDescending(r => r.WeighInTime).ToList();
    }
}
