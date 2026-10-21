using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PesaControl.Core.Models;
using PesaControl.Data.Data;
using PesaControl.Data.Services;

namespace PesaControl.UI.ViewModels;

// Intercambio de pesajes entre puestos (pendrive) desde la pantalla de Registros: se marcan los pesajes con
// casillas, se filtra con los filtros de siempre y se exporta; también se importa un archivo de otro puesto.
public partial class RegistrosViewModel
{
    private const string FileFilter = "Intercambio de pesajes (*.zip)|*.zip|Todos los archivos (*.*)|*.*";
    private const string AllExports = "Todos";

    private WeighingExchangeService _exchange = null!;
    private DatabaseService _db = null!;

    // La selección vive aquí y no en las filas: sobrevive al cambio de página.
    private readonly HashSet<int> _selectedIds = [];
    private List<int> _filterIds = [];
    private List<WeighingRecord> _attached = [];

    public bool CanExchange { get; private set; }
    public string[] ExportOptions { get; } = [AllExports, "Pendientes", "Ya exportados"];

    [ObservableProperty] private string _filterExport = AllExports;
    [ObservableProperty] private int _pendingCount;
    [ObservableProperty] private int _selectedInFilterCount;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private int _progressValue;

    public string SelectionText
        => $"{(SelectedInFilterCount == 1 ? "1 seleccionado" : $"{SelectedInFilterCount} seleccionados")} de {TotalRecords}";


    partial void OnSelectedInFilterCountChanged(int value) => OnPropertyChanged(nameof(SelectionText));
    partial void OnTotalRecordsChanged(int value) => OnPropertyChanged(nameof(SelectionText));

    private void InitializeExchange(DatabaseService db, string role)
    {
        _db = db;
        _exchange = new WeighingExchangeService(db);
        CanExchange = UserRoles.CanExchangeData(role);
    }

    private ExportFilter CurrentExportFilter() => FilterExport switch
    {
        "Pendientes" => ExportFilter.Pending,
        "Ya exportados" => ExportFilter.Exported,
        _ => ExportFilter.All
    };

    // ── Selección ──

    private void AttachSelection(IEnumerable<WeighingRecord> records)
    {
        _attached = records.ToList();
        foreach (var record in _attached)
        {
            record.IsSelectedForExport = _selectedIds.Contains(record.Id);
            record.PropertyChanged += OnRecordChanged;
        }
    }

    private void DetachSelection()
    {
        foreach (var record in _attached) record.PropertyChanged -= OnRecordChanged;
        _attached = [];
    }

    private void OnRecordChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(WeighingRecord.IsSelectedForExport) || sender is not WeighingRecord record) return;
        if (record.IsSelectedForExport) _selectedIds.Add(record.Id);
        else _selectedIds.Remove(record.Id);
        UpdateSelectedCount();
    }

    private void UpdateSelectedCount() => SelectedInFilterCount = _filterIds.Count(_selectedIds.Contains);

    // Los Id de todo el filtro (todas las páginas) y cuántos pendientes hay en total.
    private async Task RefreshFilterIdsAsync()
    {
        _filterIds = await _weighingRepo.GetFilteredIdsAsync(
            string.IsNullOrWhiteSpace(SearchPlate) ? null : SearchPlate,
            string.IsNullOrWhiteSpace(SearchTicket) ? null : SearchTicket,
            FilterFrom, FilterTo, FilterStatus,
            FilterProduct?.Id, FilterCompany?.Id, CurrentExportFilter());
        UpdateSelectedCount();
        await RefreshPendingCountAsync();
    }

    private async Task RefreshPendingCountAsync()
    {
        try { PendingCount = await _exchange.CountPendingAsync(); }
        catch { /* el contador es informativo */ }
    }

    [RelayCommand]
    private void SelectAllFiltered()
    {
        foreach (var id in _filterIds) _selectedIds.Add(id);
        foreach (var record in Records) record.IsSelectedForExport = true;
        UpdateSelectedCount();
    }

    [RelayCommand]
    private void ClearSelection()
    {
        _selectedIds.Clear();
        foreach (var record in Records) record.IsSelectedForExport = false;
        UpdateSelectedCount();
    }

    // Lo que se exporta es lo marcado dentro del filtro actual: es lo que dice el contador.
    private List<int> SelectedIdsInFilter() => _filterIds.Where(_selectedIds.Contains).ToList();

    // ── Exportar ──

    [RelayCommand]
    private async Task ExportSelectedAsync()
    {
        if (SelectedIdsInFilter().Count == 0)
        {
            StatusMessage = "No hay pesajes seleccionados. Marque los que quiera llevar o use \"Seleccionar todos\".";
            return;
        }

        var path = await AskSavePathAsync("Exportar pesajes seleccionados");
        if (path is not null) await ExportSelectedToAsync(path);
    }

    private async Task<string?> AskSavePathAsync(string title)
    {
        var stationCode = await _db.GetStationCodeAsync();
        if (stationCode is null)
        {
            StatusMessage = "Defina primero el código del puesto en Configuración > Puesto.";
            return null;
        }

        var dialog = new SaveFileDialog
        {
            Title = title,
            Filter = FileFilter,
            DefaultExt = "zip",
            FileName = WeighingExchangeService.SuggestFileName(stationCode)
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public async Task ExportSelectedToAsync(string path)
    {
        var ids = SelectedIdsInFilter();
        if (ids.Count == 0)
        {
            StatusMessage = "No hay pesajes seleccionados.";
            return;
        }

        await RunExchangeAsync("Exportando pesajes...", async progress =>
        {
            var result = await Task.Run(() => _exchange.ExportSelectedAsync(path, ids, progress));
            foreach (var id in ids) _selectedIds.Remove(id);
            await SearchAsync();
            StatusMessage = ExportedMessage(result);
        });
    }

    private string ExportedMessage(ExchangeExportResult result)
    {
        var count = result.Weighings == 1 ? "1 pesaje" : $"{result.Weighings} pesajes";
        var photos = result.Photos == 1 ? "1 foto" : $"{result.Photos} fotos";
        var left = PendingCount switch { 0 => "No quedan pesajes pendientes.", 1 => "Queda 1 pesaje pendiente.", _ => $"Quedan {PendingCount} pesajes pendientes." };
        return $"Archivo creado: {Path.GetFileName(result.FilePath)} ({count} y {photos}). {left} Llévelo al otro equipo e impórtelo allí.";
    }

    // ── Importar ──

    [RelayCommand]
    private async Task ImportFileAsync()
    {
        var dialog = new OpenFileDialog { Title = "Importar pesajes de otro puesto", Filter = FileFilter };
        if (dialog.ShowDialog() == true) await ImportFromAsync(dialog.FileName);
    }

    public async Task<ExchangeImportResult?> ImportFromAsync(string path)
    {
        ExchangeImportResult? result = null;
        await RunExchangeAsync("Importando pesajes...", async progress =>
        {
            result = await Task.Run(() => _exchange.ImportAsync(path, progress));
            await SearchAsync();
            StatusMessage = result.Message;
        });

        if (result is not null) ShowImportResult(result);
        return result;
    }

    // Se puede reemplazar en pruebas para no abrir una ventana.
    public Action<ExchangeImportResult> ShowImportResult { get; set; } = ImportResultDialog.Show;

    // ── Comunes ──

    private async Task RunExchangeAsync(string text, Func<IProgress<int>, Task> action)
    {
        IsBusy = true;
        ProgressValue = 0;
        StatusMessage = text;
        try
        {
            await action(new Progress<int>(value => ProgressValue = value));
        }
        catch (Exception ex)
        {
            StatusMessage = ex is InvalidOperationException ? ex.Message : $"No se pudo completar la operación: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            ProgressValue = 0;
        }
    }
}
