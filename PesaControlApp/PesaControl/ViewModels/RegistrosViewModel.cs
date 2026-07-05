using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.EntityFrameworkCore;
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
    [ObservableProperty] private WeighingRecord? _selectedRecord;
    [ObservableProperty] private string _statusMessage = "Listo";

    public ObservableCollection<WeighingRecord> Records { get; } = new();
    public ObservableCollection<WeighingStatus> Statuses { get; } = new(Enum.GetValues<WeighingStatus>());

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

            var query = _weighingRepo.GetAllAsync();

            var all = await query;

            var filtered = all.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(SearchPlate))
                filtered = filtered.Where(w => w.LicensePlate.Contains(SearchPlate.Trim().ToUpper()));

            if (!string.IsNullOrWhiteSpace(SearchTicket))
                filtered = filtered.Where(w => w.TicketNumber.Contains(SearchTicket.Trim()));

            if (FilterFrom.HasValue)
                filtered = filtered.Where(w => w.WeighInTime >= FilterFrom.Value);

            if (FilterTo.HasValue)
                filtered = filtered.Where(w => w.WeighInTime <= FilterTo.Value.AddDays(1));

            if (FilterStatus.HasValue)
                filtered = filtered.Where(w => w.Status == FilterStatus.Value);

            var result = filtered.OrderByDescending(w => w.WeighInTime).ToList();

            App.Current.Dispatcher.Invoke(() =>
            {
                Records.Clear();
                foreach (var r in result) Records.Add(r);
                StatusMessage = $"{result.Count} registro(s) encontrado(s)";
            });
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ClearFiltersAsync()
    {
        SearchPlate = "";
        SearchTicket = "";
        FilterFrom = null;
        FilterTo = null;
        FilterStatus = null;
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

    private async Task LoadInitialAsync()
    {
        await SearchAsync();
    }
}
