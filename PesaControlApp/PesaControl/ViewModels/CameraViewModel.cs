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
    private readonly IDvrService _dvrService;
    private Timer? _dvrStreamTimer;

    // ── Existing camera mode ──
    [ObservableProperty] private CameraConfig? _selectedCamera;
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _isPreviewRunning;
    [ObservableProperty] private ImageSource? _currentFrame;
    [ObservableProperty] private string _detectedPlate = "";
    [ObservableProperty] private double _anprConfidence;
    [ObservableProperty] private string _statusMessage = "Listo";

    [ObservableProperty] private bool _isDvrMode;

    // ── DVR mode ──
    [ObservableProperty] private DvrConfig? _selectedDvrConfig;
    [ObservableProperty] private bool _isDvrConnected;
    [ObservableProperty] private bool _isDvrSetupOpen;

    // DVR setup fields
    [ObservableProperty] private string _dvrIpAddress = "192.168.1.100";
    [ObservableProperty] private int _dvrHttpPort = 80;
    [ObservableProperty] private int _dvrRtspPort = 554;
    [ObservableProperty] private string _dvrUsername = "admin";
    [ObservableProperty] private string _dvrPassword = "";
    [ObservableProperty] private string _dvrName = "DVR Dahua";
    [ObservableProperty] private string _dvrSetupStatus = "";
    [ObservableProperty] private bool _isDvrTesting;

    // DVR channel frames
    [ObservableProperty] private ImageSource? _dvrFrame1;
    [ObservableProperty] private ImageSource? _dvrFrame2;
    [ObservableProperty] private ImageSource? _dvrFrame3;
    [ObservableProperty] private ImageSource? _dvrFrame4;
    [ObservableProperty] private string _dvrChannel1Plate = "";
    [ObservableProperty] private string _dvrChannel2Plate = "";
    [ObservableProperty] private string _dvrChannel3Plate = "";
    [ObservableProperty] private string _dvrChannel4Plate = "";
    [ObservableProperty] private string _dvrStatus1 = "Inactivo";
    [ObservableProperty] private string _dvrStatus2 = "Inactivo";
    [ObservableProperty] private string _dvrStatus3 = "Inactivo";
    [ObservableProperty] private string _dvrStatus4 = "Inactivo";
    [ObservableProperty] private bool _dvrChannel1Active;
    [ObservableProperty] private bool _dvrChannel2Active;
    [ObservableProperty] private bool _dvrChannel3Active;
    [ObservableProperty] private bool _dvrChannel4Active;
    [ObservableProperty] private bool _dvrChannel1Preview;
    [ObservableProperty] private bool _dvrChannel2Preview;
    [ObservableProperty] private bool _dvrChannel3Preview;
    [ObservableProperty] private bool _dvrChannel4Preview;
    [ObservableProperty] private int _selectedPtzChannel = 1;

    public ObservableCollection<CameraConfig> Cameras { get; } = [];
    public ObservableCollection<DvrConfig> DvrConfigs { get; } = [];
    public int[] ChannelNumbers => [1, 2, 3, 4];

    public event EventHandler<string>? PlateDetected;

    public CameraViewModel(ICameraService cameraService, IDvrService dvrService, IAnprService? anprService, DatabaseService dbService)
    {
        _cameraService = cameraService;
        _dvrService = dvrService;
        _anprService = anprService;
        _dbService = dbService;
        var ctx = dbService.CreateContext();
        _cameraRepo = new Repository<CameraConfig>(ctx);

        _cameraService.FrameCaptured += OnFrameCaptured;
        _cameraService.ConnectionStatusChanged += OnConnectionStatusChanged;
        _cameraService.ErrorOccurred += (_, err) =>
            App.Current.Dispatcher.Invoke(() => StatusMessage = $"Error: {err}");

        _dvrService.ConnectionStatusChanged += OnDvrConnectionChanged;
        _dvrService.ErrorOccurred += (_, err) =>
            App.Current.Dispatcher.Invoke(() => StatusMessage = $"DVR error: {err}");

        _ = LoadCamerasAsync();
        _ = LoadDvrConfigsAsync();
    }

    // ── Existing camera commands ──

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
    private async Task DisconnectCameraAsync()
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

    [RelayCommand]
    private void SwitchToDvr()
    {
        IsDvrMode = true;
        StatusMessage = "DVR Multicanal";
    }

    [RelayCommand]
    private void SwitchToCamera()
    {
        IsDvrMode = false;
        StatusMessage = "Cámara individual";
    }

    // ── DVR commands ──

    [RelayCommand]
    private async Task ConnectDvrAsync()
    {
        if (SelectedDvrConfig == null)
        {
            StatusMessage = "Seleccione una configuración DVR";
            return;
        }

        try
        {
            StatusMessage = "Conectando al DVR...";
            await _dvrService.ConnectAsync(SelectedDvrConfig);
            IsDvrConnected = true;
            StatusMessage = $"DVR conectado: {SelectedDvrConfig.Name}";
            StartDvrStream();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error DVR: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DisconnectDvrAsync()
    {
        StopDvrStream();
        await _dvrService.DisconnectAsync();
        IsDvrConnected = false;
        DvrFrame1 = DvrFrame2 = DvrFrame3 = DvrFrame4 = null;
        DvrStatus1 = DvrStatus2 = DvrStatus3 = DvrStatus4 = "Inactivo";
        DvrChannel1Preview = DvrChannel2Preview = DvrChannel3Preview = DvrChannel4Preview = false;
        StatusMessage = "DVR desconectado";
    }

    [RelayCommand]
    private void ToggleDvrSetup()
    {
        IsDvrSetupOpen = !IsDvrSetupOpen;
        if (IsDvrSetupOpen)
            _ = LoadDvrConfigsAsync();
    }

    [RelayCommand]
    private async Task TestDvrConnectionAsync()
    {
        IsDvrTesting = true;
        DvrSetupStatus = "Probando...";
        try
        {
            var testConfig = new DvrConfig
            {
                IpAddress = DvrIpAddress,
                HttpPort = DvrHttpPort,
                Username = DvrUsername,
                Password = DvrPassword
            };
            var ok = await _dvrService.TestConnectionAsync(testConfig);
            DvrSetupStatus = ok ? "Conexión exitosa" : "Error: no hay respuesta";
        }
        catch (Exception ex)
        {
            DvrSetupStatus = $"Error: {ex.Message}";
        }
        finally
        {
            IsDvrTesting = false;
        }
    }

    [RelayCommand]
    private async Task SaveDvrConfigAsync()
    {
        try
        {
            var ctx = _dbService.CreateContext();
            var repo = new Repository<DvrConfig>(ctx);

            if (SelectedDvrConfig == null)
            {
                var config = new DvrConfig
                {
                    Name = DvrName,
                    IpAddress = DvrIpAddress,
                    HttpPort = DvrHttpPort,
                    RtspPort = DvrRtspPort,
                    Username = DvrUsername,
                    Password = DvrPassword,
                    MaxChannels = 4,
                    IsActive = true
                };
                await repo.AddAsync(config);
            }
            else
            {
                SelectedDvrConfig.Name = DvrName;
                SelectedDvrConfig.IpAddress = DvrIpAddress;
                SelectedDvrConfig.HttpPort = DvrHttpPort;
                SelectedDvrConfig.RtspPort = DvrRtspPort;
                SelectedDvrConfig.Username = DvrUsername;
                SelectedDvrConfig.Password = DvrPassword;
                await repo.UpdateAsync(SelectedDvrConfig);
            }

            DvrSetupStatus = "Configuración guardada";
            await LoadDvrConfigsAsync();
        }
        catch (Exception ex)
        {
            DvrSetupStatus = $"Error al guardar: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DeleteDvrConfigAsync()
    {
        if (SelectedDvrConfig == null) return;
        try
        {
            var ctx = _dbService.CreateContext();
            var repo = new Repository<DvrConfig>(ctx);
            await repo.DeleteAsync(SelectedDvrConfig);
            SelectedDvrConfig = null;
            DvrSetupStatus = "Configuración eliminada";
            await LoadDvrConfigsAsync();
        }
        catch (Exception ex)
        {
            DvrSetupStatus = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private void SelectDvrConfig(DvrConfig? config)
    {
        if (config == null) return;
        SelectedDvrConfig = config;
        DvrName = config.Name;
        DvrIpAddress = config.IpAddress;
        DvrHttpPort = config.HttpPort;
        DvrRtspPort = config.RtspPort;
        DvrUsername = config.Username;
        DvrPassword = config.Password;
    }

    [RelayCommand]
    private void NewDvrConfig()
    {
        SelectedDvrConfig = null;
        DvrName = "DVR Dahua";
        DvrIpAddress = "192.168.1.100";
        DvrHttpPort = 80;
        DvrRtspPort = 554;
        DvrUsername = "admin";
        DvrPassword = "";
    }

    // ── DVR Channel controls ──

    [RelayCommand]
    private void ToggleDvrChannel(int channel)
    {
        switch (channel)
        {
            case 1: DvrChannel1Active = !DvrChannel1Active; DvrChannel1Preview = DvrChannel1Active; DvrStatus1 = DvrChannel1Active ? "En vivo" : "Inactivo"; break;
            case 2: DvrChannel2Active = !DvrChannel2Active; DvrChannel2Preview = DvrChannel2Active; DvrStatus2 = DvrChannel2Active ? "En vivo" : "Inactivo"; break;
            case 3: DvrChannel3Active = !DvrChannel3Active; DvrChannel3Preview = DvrChannel3Active; DvrStatus3 = DvrChannel3Active ? "En vivo" : "Inactivo"; break;
            case 4: DvrChannel4Active = !DvrChannel4Active; DvrChannel4Preview = DvrChannel4Active; DvrStatus4 = DvrChannel4Active ? "En vivo" : "Inactivo"; break;
        }
    }

    [RelayCommand]
    private void ActivateAllChannels()
    {
        DvrChannel1Active = DvrChannel2Active = DvrChannel3Active = DvrChannel4Active = true;
        DvrChannel1Preview = DvrChannel2Preview = DvrChannel3Preview = DvrChannel4Preview = true;
        DvrStatus1 = DvrStatus2 = DvrStatus3 = DvrStatus4 = "En vivo";
    }

    [RelayCommand]
    private void StopAllDvrChannels()
    {
        DvrChannel1Active = DvrChannel2Active = DvrChannel3Active = DvrChannel4Active = false;
        DvrChannel1Preview = DvrChannel2Preview = DvrChannel3Preview = DvrChannel4Preview = false;
        DvrFrame1 = DvrFrame2 = DvrFrame3 = DvrFrame4 = null;
        DvrStatus1 = DvrStatus2 = DvrStatus3 = DvrStatus4 = "Detenido";
    }

    [RelayCommand]
    private async Task CaptureDvrSnapshotAsync(int channel)
    {
        if (!IsDvrConnected) return;
        try
        {
            var jpeg = await _dvrService.CaptureSnapshotAsync(channel);
            var img = BytesToImageSource(jpeg);
            switch (channel)
            {
                case 1: DvrFrame1 = img; break;
                case 2: DvrFrame2 = img; break;
                case 3: DvrFrame3 = img; break;
                case 4: DvrFrame4 = img; break;
            }
            StatusMessage = $"Snapshot CH{channel} capturado";

            if (_anprService?.IsAvailable == true)
                await RecognizeDvrPlateAsync(jpeg, channel);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error CH{channel}: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task CaptureAllDvrSnapshotsAsync()
    {
        for (int ch = 1; ch <= 4; ch++)
            await CaptureDvrSnapshotAsync(ch);
    }

    [RelayCommand]
    private async Task CaptureSelectedAnprAsync()
    {
        if (!IsDvrConnected || SelectedPtzChannel < 1 || SelectedPtzChannel > 4) return;
        await CaptureDvrSnapshotAsync(SelectedPtzChannel);
    }

    // ── PTZ ──

    [RelayCommand]
    private async Task PtzMoveAsync(string direction)
    {
        if (!IsDvrConnected) return;
        if (!Enum.TryParse<PtzDirection>(direction, out var dir)) return;
        try
        {
            await _dvrService.PtzMoveAsync(SelectedPtzChannel, dir, 32);
            await Task.Delay(250);
            await _dvrService.PtzStopAsync(SelectedPtzChannel);
        }
        catch (Exception ex)
        {
            StatusMessage = $"PTZ error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task PtzGotoPresetAsync(string presetStr)
    {
        if (!IsDvrConnected || !int.TryParse(presetStr, out var preset)) return;
        try
        {
            await _dvrService.PtzGotoPresetAsync(SelectedPtzChannel, preset);
            StatusMessage = $"Preset {preset} - CH{SelectedPtzChannel}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task PtzSetPresetAsync(string presetStr)
    {
        if (!IsDvrConnected || !int.TryParse(presetStr, out var preset)) return;
        try
        {
            await _dvrService.PtzSetPresetAsync(SelectedPtzChannel, preset);
            StatusMessage = $"Preset {preset} guardado";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    // ── Internal ──

    private void StartDvrStream()
    {
        _dvrStreamTimer = new Timer(async _ =>
        {
            if (!IsDvrConnected) return;

            for (int ch = 1; ch <= 4; ch++)
            {
                bool isPreview = ch switch { 1 => DvrChannel1Preview, 2 => DvrChannel2Preview, 3 => DvrChannel3Preview, 4 => DvrChannel4Preview, _ => false };
                if (!isPreview) continue;

                try
                {
                    var jpeg = await _dvrService.CaptureSnapshotAsync(ch);
                    var img = BytesToImageSource(jpeg);
                    App.Current.Dispatcher.Invoke(() =>
                    {
                        switch (ch)
                        {
                            case 1: DvrFrame1 = img; break;
                            case 2: DvrFrame2 = img; break;
                            case 3: DvrFrame3 = img; break;
                            case 4: DvrFrame4 = img; break;
                        }
                    });
                    await Task.Delay(50);
                }
                catch { }
            }
        }, null, 0, 300);
    }

    private void StopDvrStream()
    {
        _dvrStreamTimer?.Dispose();
        _dvrStreamTimer = null;
    }

    private async Task RecognizeDvrPlateAsync(byte[] jpeg, int channel)
    {
        if (_anprService == null) return;
        var result = await _anprService.RecognizeAsync(jpeg);
        App.Current.Dispatcher.Invoke(() =>
        {
            if (result.IsSuccessful)
            {
                var plate = $"{result.Plate} ({result.Confidence:P0})";
                switch (channel)
                {
                    case 1: DvrChannel1Plate = plate; break;
                    case 2: DvrChannel2Plate = plate; break;
                    case 3: DvrChannel3Plate = plate; break;
                    case 4: DvrChannel4Plate = plate; break;
                }
                DetectedPlate = result.Plate;
                AnprConfidence = result.Confidence;
                PlateDetected?.Invoke(this, result.Plate);
                _ = SavePlateRecordAsync(result);
            }
        });
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
        catch { }
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

    private void OnDvrConnectionChanged(object? sender, bool connected)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            IsDvrConnected = connected;
            if (!connected)
            {
                StopDvrStream();
                DvrFrame1 = DvrFrame2 = DvrFrame3 = DvrFrame4 = null;
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

    private async Task LoadDvrConfigsAsync()
    {
        var ctx = _dbService.CreateContext();
        var repo = new Repository<DvrConfig>(ctx);
        var items = await repo.GetAllAsync();
        App.Current.Dispatcher.Invoke(() =>
        {
            DvrConfigs.Clear();
            foreach (var d in items) DvrConfigs.Add(d);
        });
    }
}
