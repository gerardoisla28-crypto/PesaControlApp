using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PesaControl.Core.Models;
using PesaControl.Data.Services;

namespace PesaControl.UI.ViewModels;

// Intercambio de pesajes entre puestos mediante un archivo (por ejemplo, en un pendrive).
public partial class ExchangeViewModel : ObservableObject
{
    private const string FileFilter = "Intercambio de pesajes (*.zip)|*.zip|Todos los archivos (*.*)|*.*";

    private readonly WeighingExchangeService _exchange;
    private readonly Func<Task<string?>> _getStationCode;

    [ObservableProperty] private int _pendingCount;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _lastActionFailed;
    [ObservableProperty] private int _progressValue;
    [ObservableProperty] private string _progressText = "";
    [ObservableProperty] private string _importSummary = "";

    public ObservableCollection<ExchangeConflict> Conflicts { get; } = [];

    public bool HasConflicts => Conflicts.Count > 0;
    public bool HasImportSummary => !string.IsNullOrEmpty(ImportSummary);

    public string PendingText => PendingCount switch
    {
        0 => "No hay pesajes pendientes de exportar.",
        1 => "Hay 1 pesaje pendiente de exportar.",
        _ => $"Hay {PendingCount} pesajes pendientes de exportar."
    };

    public ExchangeViewModel(WeighingExchangeService exchange, Func<Task<string?>> getStationCode)
    {
        _exchange = exchange;
        _getStationCode = getStationCode;
        Conflicts.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasConflicts));
    }

    partial void OnPendingCountChanged(int value) => OnPropertyChanged(nameof(PendingText));

    partial void OnImportSummaryChanged(string value) => OnPropertyChanged(nameof(HasImportSummary));

    [RelayCommand]
    private async Task RefreshPendingAsync()
    {
        try { PendingCount = await _exchange.CountPendingAsync(); }
        catch { /* el contador es informativo: si falla, se deja el valor anterior */ }
    }

    [RelayCommand]
    private Task ExportPendingAsync() => ExportAsync(onlyPending: true);

    [RelayCommand]
    private Task ExportAllAsync() => ExportAsync(onlyPending: false);

    private async Task ExportAsync(bool onlyPending)
    {
        await RefreshPendingAsync();
        if (onlyPending && PendingCount == 0)
        {
            Fail("No hay pesajes pendientes de exportar. Use \"Exportar todo\" si necesita un archivo con todos los pesajes.");
            return;
        }

        var stationCode = await _getStationCode();
        if (stationCode is null)
        {
            Fail("Defina primero el código del puesto en la pestaña Puesto.");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = onlyPending ? "Exportar pesajes pendientes" : "Exportar todos los pesajes",
            Filter = FileFilter,
            DefaultExt = "zip",
            FileName = WeighingExchangeService.SuggestFileName(stationCode)
        };
        if (dialog.ShowDialog() != true) return;

        await RunAsync("Exportando pesajes...", async progress =>
        {
            var result = await Task.Run(() => _exchange.ExportAsync(dialog.FileName, onlyPending, progress));
            var photos = result.Photos == 1 ? "1 foto" : $"{result.Photos} fotos";
            var count = result.Weighings == 1 ? "1 pesaje" : $"{result.Weighings} pesajes";
            Succeed($"Archivo creado: {Path.GetFileName(result.FilePath)} ({count} y {photos}). Llévelo al otro equipo e impórtelo allí.");
        });
        await RefreshPendingAsync();
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        var dialog = new OpenFileDialog { Title = "Importar pesajes de otro puesto", Filter = FileFilter };
        if (dialog.ShowDialog() != true) return;

        await RunAsync("Importando pesajes...", async progress =>
        {
            var result = await Task.Run(() => _exchange.ImportAsync(dialog.FileName, progress));
            ShowImportResult(result);
        });
        await RefreshPendingAsync();
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
