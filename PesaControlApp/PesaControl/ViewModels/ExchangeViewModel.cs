using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PesaControl.Core.Models;
using PesaControl.Data.Services;

namespace PesaControl.UI.ViewModels;

// Un pesaje pendiente en la lista de exportación, con su casilla.
public partial class PendingItem : ObservableObject
{
    public PendingItem(ExchangeCandidate source) => Source = source;

    public ExchangeCandidate Source { get; }
    public int Id => Source.Id;
    public string TicketNumber => Source.TicketNumber;
    public string LicensePlate => Source.LicensePlate;
    public string Kind => Source.Kind;
    public string ActivityText => Source.Activity.ToString("dd-MM-yyyy HH:mm");

    [ObservableProperty] private bool _isSelected = true;
}

// Intercambio de pesajes entre puestos mediante un archivo (por ejemplo, en un pendrive).
public partial class ExchangeViewModel : ObservableObject
{
    private const string FileFilter = "Intercambio de pesajes (*.zip)|*.zip|Todos los archivos (*.*)|*.*";

    private readonly WeighingExchangeService _exchange;
    private readonly Func<Task<string?>> _getStationCode;

    [ObservableProperty] private int _pendingCount;
    [ObservableProperty] private int _visibleCount;
    [ObservableProperty] private int _selectedCount;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _lastActionFailed;
    [ObservableProperty] private int _progressValue;
    [ObservableProperty] private string _progressText = "";
    [ObservableProperty] private string _importSummary = "";

    [ObservableProperty] private DateTime? _filterFrom;
    [ObservableProperty] private DateTime? _filterTo;
    [ObservableProperty] private string _filterKind = AllKinds;

    private const string AllKinds = "Todos";
    public string[] KindOptions { get; } = [AllKinds, "Entradas", "Salidas", "Salidas manuales", "Anulados"];

    public ObservableCollection<PendingItem> Pending { get; } = [];
    public ICollectionView PendingView { get; }
    public ObservableCollection<ExchangeConflict> Conflicts { get; } = [];

    public bool HasConflicts => Conflicts.Count > 0;
    public bool HasImportSummary => !string.IsNullOrEmpty(ImportSummary);

    public string PendingText => PendingCount switch
    {
        0 => "No hay pesajes pendientes de exportar.",
        1 => "Hay 1 pesaje pendiente de exportar.",
        _ => $"Hay {PendingCount} pesajes pendientes de exportar."
    };

    // Lo que se va a exportar es exactamente lo que está marcado y a la vista.
    public string SelectionText => $"{(SelectedCount == 1 ? "1 seleccionado" : $"{SelectedCount} seleccionados")} de {VisibleCount} a la vista";

    public ExchangeViewModel(WeighingExchangeService exchange, Func<Task<string?>> getStationCode)
    {
        _exchange = exchange;
        _getStationCode = getStationCode;
        PendingView = CollectionViewSource.GetDefaultView(Pending);
        PendingView.Filter = item => Matches((PendingItem)item);
        Conflicts.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasConflicts));
    }

    partial void OnPendingCountChanged(int value) => OnPropertyChanged(nameof(PendingText));
    partial void OnImportSummaryChanged(string value) => OnPropertyChanged(nameof(HasImportSummary));
    partial void OnSelectedCountChanged(int value) => OnPropertyChanged(nameof(SelectionText));
    partial void OnVisibleCountChanged(int value) => OnPropertyChanged(nameof(SelectionText));
    partial void OnFilterFromChanged(DateTime? value) => ApplyFilter();
    partial void OnFilterToChanged(DateTime? value) => ApplyFilter();
    partial void OnFilterKindChanged(string value) => ApplyFilter();

    // ── Lista y filtros ──

    private bool Matches(PendingItem item)
    {
        var day = item.Source.Activity.Date;
        if (FilterFrom is { } from && day < from.Date) return false;
        if (FilterTo is { } to && day > to.Date) return false;
        return FilterKind switch
        {
            "Entradas" => item.Kind == "Entrada",
            "Salidas" => item.Kind is "Salida" or "Salida manual",
            "Salidas manuales" => item.Kind == "Salida manual",
            "Anulados" => item.Kind == "Anulado",
            _ => true
        };
    }

    private void ApplyFilter()
    {
        PendingView.Refresh();
        UpdateCounts();
    }

    private IEnumerable<PendingItem> VisibleItems => PendingView.Cast<PendingItem>();

    private void UpdateCounts()
    {
        PendingCount = Pending.Count;
        VisibleCount = VisibleItems.Count();
        SelectedCount = VisibleItems.Count(i => i.IsSelected);
    }

    [RelayCommand]
    private async Task RefreshPendingAsync()
    {
        try
        {
            var previous = Pending.ToDictionary(p => p.Id, p => p.IsSelected);
            var items = await _exchange.ListPendingAsync();

            foreach (var item in Pending) item.PropertyChanged -= OnItemChanged;
            Pending.Clear();
            foreach (var candidate in items)
            {
                // Lo que ya estaba en la lista conserva su casilla; lo nuevo llega marcado.
                var item = new PendingItem(candidate) { IsSelected = previous.GetValueOrDefault(candidate.Id, true) };
                item.PropertyChanged += OnItemChanged;
                Pending.Add(item);
            }
            UpdateCounts();
        }
        catch { /* la lista es informativa: si falla, se deja la anterior */ }
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PendingItem.IsSelected)) UpdateCounts();
    }

    [RelayCommand]
    private void SelectVisible()
    {
        foreach (var item in VisibleItems) item.IsSelected = true;
    }

    [RelayCommand]
    private void ClearVisible()
    {
        foreach (var item in VisibleItems) item.IsSelected = false;
    }

    [RelayCommand]
    private void ClearFilters()
    {
        FilterFrom = null;
        FilterTo = null;
        FilterKind = AllKinds;
    }

    // ── Exportar ──

    [RelayCommand]
    private async Task ExportSelectedAsync()
    {
        await RefreshPendingAsync();
        if (SelectedCount == 0)
        {
            Fail(PendingCount == 0
                ? "No hay pesajes pendientes de exportar. Use \"Exportar todo\" si necesita un archivo con todos los pesajes."
                : "No hay pesajes seleccionados para exportar. Marque los que quiera llevar.");
            return;
        }

        var path = await AskSavePathAsync("Exportar pesajes seleccionados");
        if (path is not null) await ExportSelectedToAsync(path);
    }

    [RelayCommand]
    private async Task ExportAllAsync()
    {
        var path = await AskSavePathAsync("Exportar todos los pesajes");
        if (path is not null) await ExportAllToAsync(path);
    }

    private async Task<string?> AskSavePathAsync(string title)
    {
        var stationCode = await _getStationCode();
        if (stationCode is null)
        {
            Fail("Defina primero el código del puesto en la pestaña Puesto.");
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
        var ids = VisibleItems.Where(i => i.IsSelected).Select(i => i.Id).ToList();
        if (ids.Count == 0)
        {
            Fail("No hay pesajes seleccionados para exportar. Marque los que quiera llevar.");
            return;
        }

        await RunAsync("Exportando pesajes...", async progress =>
        {
            var result = await Task.Run(() => _exchange.ExportSelectedAsync(path, ids, progress));
            await RefreshPendingAsync();
            Succeed(ExportedMessage(result) + RemainingText());
        });
    }

    public async Task ExportAllToAsync(string path)
    {
        await RunAsync("Exportando pesajes...", async progress =>
        {
            var result = await Task.Run(() => _exchange.ExportAsync(path, onlyPending: false, progress));
            await RefreshPendingAsync();
            Succeed(ExportedMessage(result));
        });
    }

    private static string ExportedMessage(ExchangeExportResult result)
    {
        var count = result.Weighings == 1 ? "1 pesaje" : $"{result.Weighings} pesajes";
        var photos = result.Photos == 1 ? "1 foto" : $"{result.Photos} fotos";
        return $"Archivo creado: {Path.GetFileName(result.FilePath)} ({count} y {photos}). Llévelo al otro equipo e impórtelo allí.";
    }

    private string RemainingText() => PendingCount switch
    {
        0 => " No quedan pesajes pendientes.",
        1 => " Queda 1 pesaje pendiente.",
        _ => $" Quedan {PendingCount} pesajes pendientes."
    };

    // ── Importar ──

    [RelayCommand]
    private async Task ImportAsync()
    {
        var dialog = new OpenFileDialog { Title = "Importar pesajes de otro puesto", Filter = FileFilter };
        if (dialog.ShowDialog() == true) await ImportFromAsync(dialog.FileName);
    }

    public async Task ImportFromAsync(string path)
    {
        await RunAsync("Importando pesajes...", async progress =>
        {
            var result = await Task.Run(() => _exchange.ImportAsync(path, progress));
            ShowImportResult(result);
            await RefreshPendingAsync();
        });
    }

    private void ShowImportResult(ExchangeImportResult result)
    {
        Conflicts.Clear();
        if (!result.Success)
        {
            ImportSummary = "";
            Fail(result.Message);
            return;
        }

        foreach (var conflict in result.Conflicts) Conflicts.Add(conflict);
        ImportSummary = result.Message;
        if (Conflicts.Count > 0)
            Succeed("Importación terminada con conflictos. Se conservó el valor de este equipo; revise la lista de abajo.");
        else
            Succeed("Importación terminada sin conflictos.");
    }

    // ── Comunes ──

    private async Task RunAsync(string text, Func<IProgress<int>, Task> action)
    {
        IsBusy = true;
        LastActionFailed = false;
        StatusMessage = text;
        ProgressValue = 0;
        ProgressText = text;
        try
        {
            await action(new Progress<int>(value => ProgressValue = value));
        }
        catch (Exception ex)
        {
            Fail(ex is InvalidOperationException ? ex.Message : $"No se pudo completar la operación: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
            ProgressValue = 0;
            ProgressText = "";
        }
    }

    private void Succeed(string message)
    {
        LastActionFailed = false;
        StatusMessage = message;
    }

    private void Fail(string message)
    {
        LastActionFailed = true;
        StatusMessage = message;
    }
}
