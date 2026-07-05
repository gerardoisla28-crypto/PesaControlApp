using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PesaControl.Data.Services;

namespace PesaControl.UI.ViewModels;

public partial class BackupViewModel : ObservableObject
{
    private readonly ExportImportService _exportImport;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private int _progressValue;
    [ObservableProperty] private string _progressText = "";

    public ObservableCollection<BackupEntry> RecentBackups { get; } = [];

    public BackupViewModel(ExportImportService exportImport)
    {
        _exportImport = exportImport;
        RefreshRecentBackups();
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Exportar respaldo de datos",
            Filter = "Respaldo PesaControl (*.pesabackup)|*.pesabackup|Todos los archivos (*.*)|*.*",
            DefaultExt = "pesabackup",
            FileName = $"respaldo_{DateTime.Now:yyyy-MM-dd}.pesabackup"
        };

        if (dialog.ShowDialog() != true)
            return;

        IsBusy = true;
        StatusMessage = "Exportando datos...";
        ProgressText = "Recopilando información...";
        ProgressValue = 0;

        try
        {
            var progress = new Progress<int>(value =>
            {
                App.Current.Dispatcher.Invoke(() =>
                {
                    ProgressValue = value;
                    ProgressText = value switch
                    {
                        < 50 => "Recopilando datos...",
                        < 90 => "Empaquetando fotos...",
                        _ => "Finalizando..."
                    };
                });
            });

            await _exportImport.ExportAsync(dialog.FileName, progress);
            StatusMessage = $"Respaldo creado: {Path.GetFileName(dialog.FileName)}";
            RefreshRecentBackups();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error al exportar: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            ProgressValue = 0;
            ProgressText = "";
        }
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Importar respaldo de datos",
            Filter = "Respaldo PesaControl (*.pesabackup)|*.pesabackup|Todos los archivos (*.*)|*.*",
            DefaultExt = "pesabackup"
        };

        if (dialog.ShowDialog() != true)
            return;

        var result = System.Windows.MessageBox.Show(
            "Esta acción reemplazará todos los datos actuales de la aplicación.\n\n" +
            "¿Realizó un respaldo de los datos actuales antes de continuar?\n\n" +
            "¿Desea continuar con la importación?",
            "Confirmar importación",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (result != System.Windows.MessageBoxResult.Yes)
            return;

        IsBusy = true;
        StatusMessage = "Importando datos...";
        ProgressText = "Iniciando importación...";
        ProgressValue = 0;

        try
        {
            var progress = new Progress<int>(value =>
            {
                App.Current.Dispatcher.Invoke(() =>
                {
                    ProgressValue = value;
                    ProgressText = value switch
                    {
                        < 20 => "Preparando base de datos...",
                        < 50 => "Importando registros...",
                        < 80 => "Restaurando fotos...",
                        _ => "Finalizando..."
                    };
                });
            });

            var importResult = await _exportImport.ImportAsync(dialog.FileName, progress);

            if (importResult.Success)
            {
                StatusMessage = importResult.Message;
            }
            else
            {
                StatusMessage = importResult.Message;
                System.Windows.MessageBox.Show(
                    importResult.Message,
                    "Error de importación",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error al importar: {ex.Message}";
            System.Windows.MessageBox.Show(
                $"Error durante la importación:\n\n{ex.Message}",
                "Error",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
            ProgressValue = 0;
            ProgressText = "";
            RefreshRecentBackups();
        }
    }

    private void RefreshRecentBackups()
    {
        RecentBackups.Clear();

        try
        {
            var backupDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PesaControl", "Backups");

            if (!Directory.Exists(backupDir)) return;

            foreach (var file in Directory.GetFiles(backupDir, "*.pesabackup")
                         .OrderByDescending(f => File.GetLastWriteTime(f))
                         .Take(10))
            {
                var info = new FileInfo(file);
                RecentBackups.Add(new BackupEntry
                {
                    FileName = info.Name,
                    FilePath = info.FullName,
                    SizeMB = Math.Round(info.Length / (1024.0 * 1024.0), 1),
                    LastModified = info.LastWriteTime
                });
            }
        }
        catch
        {
            // Ignorar errores al listar backups
        }
    }
}

public class BackupEntry
{
    public string FileName { get; set; } = "";
    public string FilePath { get; set; } = "";
    public double SizeMB { get; set; }
    public DateTime LastModified { get; set; }
    public string LastModifiedDisplay => LastModified.ToString("g");
    public string SizeDisplay => $"{SizeMB} MB";
}
