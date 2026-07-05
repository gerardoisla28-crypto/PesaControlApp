using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PesaControl.Core.Models;
using PesaControl.Core.Services;
using PesaControl.Data.Data;
using PesaControl.Data.Repositories;

namespace PesaControl.UI.ViewModels;

public partial class CameraViewModel : ObservableObject
{
    private readonly ICameraService _cameraService;
    private readonly IAnprService? _anprService;
    private readonly DatabaseService _dbService;
    private readonly IRepository<CameraConfig> _cameraRepo;

    [ObservableProperty] private CameraConfig? _selectedCamera;
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _isPreviewRunning;
    [ObservableProperty] private ImageSource? _currentFrame;
    [ObservableProperty] private string _detectedPlate = "";
    [ObservableProperty] private double _anprConfidence;
    [ObservableProperty] private string _statusMessage = "Listo";

    public ObservableCollection<CameraConfig> Cameras { get; } = new();

    public event EventHandler<string>? PlateDetected;

    public CameraViewModel(ICameraService cameraService, IAnprService? anprService, DatabaseService dbService)
    {
        _cameraService = cameraService;
        _anprService = anprService;
        _dbService = dbService;
        var ctx = dbService.CreateContext();
        _cameraRepo = new Repository<CameraConfig>(ctx);

        _cameraService.FrameCaptured += OnFrameCaptured;
        _cameraService.ConnectionStatusChanged += OnConnectionStatusChanged;
        _cameraService.ErrorOccurred += (_, err) =>
            App.Current.Dispatcher.Invoke(() => StatusMessage = $"Error: {err}");

        _ = LoadCamerasAsync();
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (SelectedCamera == null)
        {
            StatusMessage = "Seleccione una cámara";
            return;
        }

        try
        {
            StatusMessage = "Conectando...";
            await _cameraService.ConnectAsync(SelectedCamera);
            await _cameraService.StartPreviewAsync();
            IsPreviewRunning = true;
            StatusMessage = $"Conectado a {SelectedCamera.Name}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        await _cameraService.DisconnectAsync();
        IsPreviewRunning = false;
        CurrentFrame = null;
        DetectedPlate = "";
        StatusMessage = "Desconectado";
    }

    [RelayCommand]
    private async Task CaptureStillAsync()
    {
        try
        {
            var jpeg = await _cameraService.CaptureStillAsync();
            CurrentFrame = BytesToImageSource(jpeg);
            StatusMessage = "Foto capturada";

            if (_anprService?.IsAvailable == true)
                await RecognizePlateAsync(jpeg);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error al capturar: {ex.Message}";
        }
    }

    private async Task RecognizePlateAsync(byte[] jpeg)
    {
        if (_anprService == null) return;

        var result = await _anprService.RecognizeAsync(jpeg);
        App.Current.Dispatcher.Invoke(() =>
        {
            if (result.IsSuccessful)
            {
                DetectedPlate = result.Plate;
                AnprConfidence = result.Confidence;
                StatusMessage = $"Patente: {result.Plate} ({result.Confidence:P0})";
                PlateDetected?.Invoke(this, result.Plate);

                _ = SavePlateRecordAsync(result);
            }
            else
            {
                StatusMessage = result.ErrorMessage ?? "No se reconoció patente";
            }
        });
    }

    private async Task SavePlateRecordAsync(PlateRecognitionResult result)
    {
        try
        {
            var ctx = _dbService.CreateContext();
            var repo = new Repository<PlateRecord>(ctx);
            await repo.AddAsync(new PlateRecord
            {
                Plate = result.Plate,
                Region = result.Region,
                Confidence = result.Confidence,
                Source = _anprService?.Name ?? "ANPR",
                Timestamp = DateTime.UtcNow,
                CameraConfigId = SelectedCamera?.Id
            });
        }
        catch
        {
            // Silently ignore save failures for plate history
        }
    }

    private void OnFrameCaptured(object? sender, byte[] jpeg)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            CurrentFrame = BytesToImageSource(jpeg);
        });
    }

    private void OnConnectionStatusChanged(object? sender, bool connected)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            IsConnected = connected;
            if (!connected)
            {
                IsPreviewRunning = false;
                CurrentFrame = null;
            }
        });
    }

    private static ImageSource? BytesToImageSource(byte[] bytes)
    {
        if (bytes.Length == 0) return null;
        using var ms = new MemoryStream(bytes);
        var img = new BitmapImage();
        img.BeginInit();
        img.CacheOption = BitmapCacheOption.OnLoad;
        img.StreamSource = ms;
        img.EndInit();
        img.Freeze();
        return img;
    }

    private async Task LoadCamerasAsync()
    {
        var cameras = await _cameraRepo.FindAsync(c => c.IsActive);
        App.Current.Dispatcher.Invoke(() =>
        {
            Cameras.Clear();
            foreach (var c in cameras) Cameras.Add(c);
        });
    }
}
