using PesaControl.Core.Models;

namespace PesaControl.Core.Services;

public interface ICameraService
{
    string Name { get; }
    bool IsConnected { get; }
    bool IsPreviewRunning { get; }

    event EventHandler<byte[]> FrameCaptured;
    event EventHandler<bool> ConnectionStatusChanged;
    event EventHandler<string> ErrorOccurred;

    Task ConnectAsync(CameraConfig config);
    Task DisconnectAsync();
    Task StartPreviewAsync();
    Task StopPreviewAsync();
    Task<byte[]> CaptureStillAsync();
}
