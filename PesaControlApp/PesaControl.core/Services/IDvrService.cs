using PesaControl.Core.Models;

namespace PesaControl.Core.Services;

public interface IDvrService
{
    string Name { get; }
    bool IsConnected { get; }
    DvrConfig? CurrentConfig { get; }

    event EventHandler<bool> ConnectionStatusChanged;
    event EventHandler<DvrEventInfo> DvrEventOccurred;
    event EventHandler<string> ErrorOccurred;

    Task<bool> TestConnectionAsync(DvrConfig config);
    Task ConnectAsync(DvrConfig config);
    Task DisconnectAsync();

    string GetRtspUrl(int channel, int subtype = 0);
    string GetHttpMjpegUrl(int channel);

    Task<byte[]> CaptureSnapshotAsync(int channel);

    Task PtzMoveAsync(int channel, PtzDirection direction, int speed = 32);
    Task PtzStopAsync(int channel);
    Task PtzGotoPresetAsync(int channel, int preset);
    Task PtzSetPresetAsync(int channel, int preset);

    Task<List<RecordingInfo>> FindRecordingsAsync(int channel, DateTime from, DateTime to);
    string GetPlaybackUrl(string filePath);
}
