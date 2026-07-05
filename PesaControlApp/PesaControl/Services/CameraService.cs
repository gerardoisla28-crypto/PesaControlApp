using OpenCvSharp;
using PesaControl.Core.Models;
using PesaControl.Core.Services;

namespace PesaControl.UI.Services;

public class CameraService : ICameraService, IDisposable
{
    private VideoCapture? _capture;
    private CancellationTokenSource? _cts;
    private CameraConfig _config = new();
    private bool _disposed;
    private readonly object _lock = new();

    public string Name => _config.Name;
    public bool IsConnected => _capture?.IsOpened() == true;

    public bool IsPreviewRunning => _cts is { IsCancellationRequested: false };

    public event EventHandler<byte[]>? FrameCaptured;
    public event EventHandler<bool>? ConnectionStatusChanged;
    public event EventHandler<string>? ErrorOccurred;

    public async Task ConnectAsync(CameraConfig config)
    {
        await DisconnectAsync();

        _config = config;
        _capture = new VideoCapture();

        bool opened = config.Type switch
        {
            CameraType.USB => _capture.Open(int.Parse(config.Url), VideoCaptureAPIs.DSHOW),
            CameraType.IP => _capture.Open(config.Url, VideoCaptureAPIs.ANY),
            CameraType.RTSP => _capture.Open(config.Url, VideoCaptureAPIs.ANY),
            _ => throw new NotSupportedException($"Tipo de cámara no soportado: {config.Type}")
        };

        if (!opened)
        {
            var msg = $"No se pudo conectar a la cámara: {config.Name}";
            ErrorOccurred?.Invoke(this, msg);
            throw new InvalidOperationException(msg);
        }

        ConnectionStatusChanged?.Invoke(this, true);
    }

    public Task DisconnectAsync()
    {
        StopPreviewAsync();
        lock (_lock)
        {
            _capture?.Dispose();
            _capture = null;
        }
        ConnectionStatusChanged?.Invoke(this, false);
        return Task.CompletedTask;
    }

    public async Task StartPreviewAsync()
    {
        if (_capture?.IsOpened() != true)
        {
            var msg = "Cámara no conectada";
            ErrorOccurred?.Invoke(this, msg);
            throw new InvalidOperationException(msg);
        }

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        await Task.Run(async () =>
        {
            using var mat = new Mat();
            while (!token.IsCancellationRequested)
            {
                lock (_lock)
                {
                    if (_capture?.IsOpened() == true)
                        _capture.Read(mat);
                }

                if (!mat.Empty())
                {
                    var jpegBytes = mat.ToBytes(".jpg");
                    FrameCaptured?.Invoke(this, jpegBytes);
                }

                try
                {
                    await Task.Delay(66, token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }, token);
    }

    public Task StopPreviewAsync()
    {
        _cts?.Cancel();
        _cts = null;
        return Task.CompletedTask;
    }

    public Task<byte[]> CaptureStillAsync()
    {
        if (_capture?.IsOpened() != true)
        {
            var msg = "Cámara no conectada";
            ErrorOccurred?.Invoke(this, msg);
            throw new InvalidOperationException(msg);
        }

        using var mat = new Mat();
        lock (_lock)
        {
            _capture.Read(mat);
        }

        if (mat.Empty())
        {
            var msg = "No se pudo capturar imagen";
            ErrorOccurred?.Invoke(this, msg);
            throw new InvalidOperationException(msg);
        }

        var jpeg = mat.ToBytes(".jpg");
        return Task.FromResult(jpeg);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts?.Cancel();
        _cts?.Dispose();
        _capture?.Dispose();
    }
}
