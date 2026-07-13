using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using PesaControl.Core.Models;
using PesaControl.Data.Data;
using PesaControl.Data.Repositories;
using PesaControl.UI.Services;
using PesaControl.UI.Views;

namespace PesaControl.UI.ViewModels;

public partial class RegistrosViewModel : ObservableObject
{
    private readonly DatabaseService _dbService;
    private readonly IWeighingRepository _weighingRepo;

    [ObservableProperty] private string _searchPlate = "";
    [ObservableProperty] private string _searchTicket = "";
    [ObservableProperty] private DateTime? _filterFrom;
    [ObservableProperty] private DateTime? _filterTo;
    [ObservableProperty] private WeighingStatus? _filterStatus;
    [ObservableProperty] private Product? _filterProduct;
    [ObservableProperty] private Company? _filterCompany;
    [ObservableProperty] private WeighingRecord? _selectedRecord;
    [ObservableProperty] private string _statusMessage = "Listo";

    [ObservableProperty] private int _currentPage = 1;
    [ObservableProperty] private int _totalPages = 1;
    [ObservableProperty] private int _pageSize = 20;
    [ObservableProperty] private int _totalRecords;
    [ObservableProperty] private bool _hasPreviousPage;
    [ObservableProperty] private bool _hasNextPage;
    [ObservableProperty] private string _pageInfo = "";

    public ObservableCollection<WeighingRecord> Records { get; } = new();
    public ObservableCollection<WeighingStatus> Statuses { get; } = new(Enum.GetValues<WeighingStatus>());
    public ObservableCollection<Product> Products { get; } = new();
    public ObservableCollection<Company> Companies { get; } = new();

    public RegistrosViewModel(DatabaseService dbService)
    {
        _dbService = dbService;
        var ctx = dbService.CreateContext();
        _weighingRepo = new WeighingRepository(ctx);

        _ = LoadInitialAsync();

        WeakReferenceMessenger.Default.Register<WeighingDataChangedMessage>(this, (r, m) =>
        {
            _ = SearchAsync();
        });
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        try
        {
            StatusMessage = "Buscando...";
            CurrentPage = 1;
            await FetchPageAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task GoToPageAsync(object? pageObj)
    {
        int page = pageObj switch
        {
            int i => i,
            string s when int.TryParse(s, out var p) => p,
            _ => 1
        };
        if (page < 1 || page > TotalPages) return;
        CurrentPage = page;
        await FetchPageAsync();
    }

    [RelayCommand]
    private async Task NextPageAsync()
    {
        if (!HasNextPage) return;
        CurrentPage++;
        await FetchPageAsync();
    }

    [RelayCommand]
    private async Task PreviousPageAsync()
    {
        if (!HasPreviousPage) return;
        CurrentPage--;
        await FetchPageAsync();
    }

    private async Task FetchPageAsync()
    {
        var result = await _weighingRepo.GetPagedAsync(
            CurrentPage, PageSize,
            string.IsNullOrWhiteSpace(SearchPlate) ? null : SearchPlate,
            string.IsNullOrWhiteSpace(SearchTicket) ? null : SearchTicket,
            FilterFrom, FilterTo, FilterStatus,
            FilterProduct?.Id, FilterCompany?.Id);

        App.Current.Dispatcher.Invoke(() =>
        {
            Records.Clear();
            foreach (var r in result.Items) Records.Add(r);

            TotalRecords = result.TotalCount;
            TotalPages = result.TotalPages;
            HasPreviousPage = CurrentPage > 1;
            HasNextPage = CurrentPage < TotalPages;
            PageInfo = $"Pág {CurrentPage} de {TotalPages} ({TotalRecords} registros)";
            StatusMessage = $"{TotalRecords} registro(s) encontrado(s)";
        });
    }

    [RelayCommand]
    private async Task ClearFiltersAsync()
    {
        SearchPlate = "";
        SearchTicket = "";
        FilterFrom = null;
        FilterTo = null;
        FilterStatus = null;
        FilterProduct = null;
        FilterCompany = null;
        await SearchAsync();
    }

    [RelayCommand]
    private async Task PrintTicketAsync()
    {
        if (SelectedRecord == null)
        {
            StatusMessage = "Seleccione un registro para imprimir";
            return;
        }

        try
        {
            StatusMessage = "Preparando impresión...";

            var ctx = _dbService.CreateContext();
            var fullRecord = await new WeighingRepository(ctx).GetByIdAsync(SelectedRecord.Id);
            if (fullRecord == null) return;

            var configs = await new Repository<SystemConfig>(ctx).GetAllAsync();
            var header = configs.FirstOrDefault(c => c.Key == "receiving_company")?.Value ?? "PesaControl";
            var template = new TicketTemplate { Header = header };

            var dlg = new PrintDialog();
            if (dlg.ShowDialog() != true) return;

            var printer = new WindowsPrinter(dlg.PrintQueue.Name);
            var success = await printer.PrintAsync(fullRecord, template);
            StatusMessage = success ? "Ticket impreso" : "Error al imprimir";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error al imprimir: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ShowPhotos(WeighingRecord? record)
    {
        if (record == null) return;

        bool hasEntryPhoto = !string.IsNullOrEmpty(record.PhotoPath) && File.Exists(record.PhotoPath);
        bool hasExitPhoto = !string.IsNullOrEmpty(record.PhotoPathExit) && File.Exists(record.PhotoPathExit);

        if (!hasEntryPhoto && !hasExitPhoto)
        {
            StatusMessage = "No hay fotos para este registro";
            return;
        }

        var dialog = new FotoPopup(record);
        dialog.Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
        dialog.ShowDialog();
    }

    [RelayCommand]
    private void ShowDetail(WeighingRecord? record)
    {
        if (record == null) return;

        var ctx = _dbService.CreateContext();
        var fullRecord = new WeighingRepository(ctx).GetByIdAsync(record.Id).GetAwaiter().GetResult();
        if (fullRecord == null) return;

        var dialog = new RegistroDetailPopup(fullRecord);
        dialog.Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
        dialog.ShowDialog();
    }

    private async Task LoadInitialAsync()
    {
        await SearchAsync();

        using var ctx = _dbService.CreateContext();
        var products = await new Repository<Product>(ctx).GetAllAsync();
        var companies = await new Repository<Company>(ctx).GetAllAsync();

        App.Current.Dispatcher.Invoke(() =>
        {
            Products.Clear();
            foreach (var p in products.OrderBy(x => x.Name)) Products.Add(p);

            Companies.Clear();
            foreach (var c in companies.OrderBy(x => x.Name)) Companies.Add(c);
        });
    }
}
