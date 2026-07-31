using System.Text.RegularExpressions;
using PesaControl.Core.Models;

namespace PesaControl.Core.Services;

public class DahuaDvrService : IDvrService, IDisposable
{
    private readonly DahuaHttpClient _http;
    private DvrConfig? _config;
    private Timer? _eventPollTimer;
    private bool _disposed;

    public string Name => _config?.Name ?? "DVR Dahua";
    public bool IsConnected { get; private set; }
    public DvrConfig? CurrentConfig => _config;

    public event EventHandler<bool>? ConnectionStatusChanged;
    public event EventHandler<DvrEventInfo>? DvrEventOccurred;
    public event EventHandler<string>? ErrorOccurred;

    public DahuaDvrService()
    {
        _http = new DahuaHttpClient();
    }

    public async Task<bool> TestConnectionAsync(DvrConfig config)
    {
        _http.Configure(config.IpAddress, config.HttpPort, config.Username, config.Password);
        return await _http.TestConnectionAsync();
    }

    public async Task ConnectAsync(DvrConfig config)
    {
        await DisconnectAsync();

        _config = config;
        _http.Configure(config.IpAddress, config.HttpPort, config.Username, config.Password);

        var ok = await _http.TestConnectionAsync();
        if (!ok)
            throw new InvalidOperationException($"No se pudo conectar al DVR en {config.IpAddress}:{config.HttpPort}");

        IsConnected = true;
        ConnectionStatusChanged?.Invoke(this, true);
        StartEventPolling();
    }

    public Task DisconnectAsync()
    {
        StopEventPolling();
        IsConnected = false;
        _config = null;
        ConnectionStatusChanged?.Invoke(this, false);
        return Task.CompletedTask;
    }

    public string GetRtspUrl(int channel, int subtype = 0)
    {
        if (_config == null)
            throw new InvalidOperationException("DVR no conectado");

        var user = Uri.EscapeDataString(_config.Username);
        var pass = Uri.EscapeDataString(_config.Password);
        return $"rtsp://{user}:{pass}@{_config.IpAddress}:{_config.RtspPort}/cam/realmonitor?channel={channel}&subtype={subtype}";
    }

    public string GetHttpMjpegUrl(int channel)
    {
        if (_config == null)
            throw new InvalidOperationException("DVR no conectado");

        return $"http://{_config.IpAddress}:{_config.HttpPort}/cgi-bin/mjpg/video.cgi?channel={channel - 1}";
    }

    public async Task<byte[]> CaptureSnapshotAsync(int channel)
    {
        if (_config == null)
            throw new InvalidOperationException("DVR no conectado");

        var path = $"/cgi-bin/snapshot.cgi?chn={channel}&u={_config.Username}&p={_config.Password}";
        return await _http.GetBytesAsync(path);
    }

    public async Task PtzMoveAsync(int channel, PtzDirection direction, int speed = 32)
    {
        var code = direction switch
        {
            PtzDirection.Up => "Up",
            PtzDirection.Down => "Down",
            PtzDirection.Left => "Left",
            PtzDirection.Right => "Right",
            PtzDirection.ZoomIn => "ZoomTele",
            PtzDirection.ZoomOut => "ZoomWide",
            PtzDirection.FocusIn => "FocusNear",
            PtzDirection.FocusOut => "FocusFar",
            PtzDirection.IrisOpen => "IrisOpen",
            PtzDirection.IrisClose => "IrisClose",
            _ => throw new ArgumentOutOfRangeException(nameof(direction))
        };

        await SendPtzCommandAsync(channel, code, speed);
    }

    public async Task PtzStopAsync(int channel)
    {
        await SendPtzCommandAsync(channel, "Up", 0, "stop");
    }

    public async Task PtzGotoPresetAsync(int channel, int preset)
    {
        var path = $"/cgi-bin/ptz.cgi?action=start&channel={channel}&code=GotoPreset&arg1=0&arg2={preset}&arg3=0";
        await _http.GetStringAsync(path);
    }

    public async Task PtzSetPresetAsync(int channel, int preset)
    {
        var path = $"/cgi-bin/ptz.cgi?action=start&channel={channel}&code=SetPreset&arg1=0&arg2={preset}&arg3=0";
        await _http.GetStringAsync(path);
    }

    private async Task SendPtzCommandAsync(int channel, string code, int speed, string action = "start")
    {
        var path = $"/cgi-bin/ptz.cgi?action={action}&channel={channel}&code={code}&arg1=0&arg2={speed}&arg3=0";
        await _http.GetStringAsync(path);
    }

    public async Task<List<RecordingInfo>> FindRecordingsAsync(int channel, DateTime from, DateTime to)
    {
        var results = new List<RecordingInfo>();

        var createResponse = await _http.GetStringAsync("/cgi-bin/mediaFileFind.cgi?action=factory.create");
        var factoryMatch = Regex.Match(createResponse, @"object=(\d+)");
        if (!factoryMatch.Success) return results;

        var objectId = factoryMatch.Groups[1].Value;
        var startStr = from.ToString("yyyy-MM-dd HH:mm:ss");
        var endStr = to.ToString("yyyy-MM-dd HH:mm:ss");

        var findPath = $"/cgi-bin/mediaFileFind.cgi?action=findFile&object={objectId}" +
                       $"&condition.Channel={channel}&condition.StartTime={Uri.EscapeDataString(startStr)}" +
                       $"&condition.EndTime={Uri.EscapeDataString(endStr)}";

        var findResponse = await _http.GetStringAsync(findPath);
        if (!findResponse.Contains("OK")) return results;

        var count = 20;
        while (true)
        {
            var nextPath = $"/cgi-bin/mediaFileFind.cgi?action=findNextFile&object={objectId}&count={count}";
            var nextResponse = await _http.GetStringAsync(nextPath);

            var foundMatch = Regex.Match(nextResponse, @"found=(\d+)");
            if (!foundMatch.Success || int.Parse(foundMatch.Groups[1].Value) == 0)
                break;

            var items = ParseRecordingItems(nextResponse);
            results.AddRange(items);
        }

        try
        {
            await _http.GetStringAsync($"/cgi-bin/mediaFileFind.cgi?action=close&object={objectId}");
            await _http.GetStringAsync($"/cgi-bin/mediaFileFind.cgi?action=destroy&object={objectId}");
        }
        catch
        {
        }

        return results;
    }

    private static List<RecordingInfo> ParseRecordingItems(string response)
    {
        var items = new List<RecordingInfo>();
        var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        var itemGroups = new Dictionary<int, Dictionary<string, string>>();
        var itemPattern = new Regex(@"items\[(\d+)\]\.(\w+)=(.*)");

        foreach (var line in lines)
        {
            var match = itemPattern.Match(line.Trim());
            if (!match.Success) continue;

            var idx = int.Parse(match.Groups[1].Value);
            var key = match.Groups[2].Value;
            var value = match.Groups[3].Value.Trim('\r');

            if (!itemGroups.ContainsKey(idx))
                itemGroups[idx] = new Dictionary<string, string>();

            itemGroups[idx][key] = value;
        }

        foreach (var group in itemGroups.Values)
        {
            if (!group.ContainsKey("FilePath")) continue;

            items.Add(new RecordingInfo
            {
                Channel = int.TryParse(group.GetValueOrDefault("Channel"), out var ch) ? ch : 0,
                FilePath = group["FilePath"],
                StartTime = DateTime.Parse(group.GetValueOrDefault("StartTime", "2000-01-01")),
                EndTime = DateTime.Parse(group.GetValueOrDefault("EndTime", "2000-01-01")),
                SizeBytes = long.TryParse(group.GetValueOrDefault("Length"), out var len) ? len : 0,
                Type = group.GetValueOrDefault("Type", "dav"),
                Flags = group.GetValueOrDefault("Flags[0]", ""),
                EventType = group.GetValueOrDefault("Events[0]", "")
            });
        }

        return items;
    }

    public string GetPlaybackUrl(string filePath)
    {
        if (_config == null)
            throw new InvalidOperationException("DVR no conectado");

        var user = Uri.EscapeDataString(_config.Username);
        var pass = Uri.EscapeDataString(_config.Password);
        var cleanPath = filePath.Replace("\\", "");
        return $"rtsp://{user}:{pass}@{_config.IpAddress}:{_config.RtspPort}{cleanPath}";
    }

    private void StartEventPolling()
    {
        _eventPollTimer = new Timer(async _ =>
        {
            try
            {
                var response = await _http.GetStringAsync("/cgi-bin/eventManager.cgi?action=attach&codes=[All]");
                var events = ParseEvents(response);
                foreach (var evt in events)
                    DvrEventOccurred?.Invoke(this, evt);
            }
            catch
            {
            }
        }, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10));
    }

    private void StopEventPolling()
    {
        _eventPollTimer?.Dispose();
        _eventPollTimer = null;
    }

    private static List<DvrEventInfo> ParseEvents(string response)
    {
        var events = new List<DvrEventInfo>();
        var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            if (!line.Contains("Code=")) continue;

            var codeMatch = Regex.Match(line, @"Code=(\w+)");
            var channelMatch = Regex.Match(line, @"channel=(\d+)");

            events.Add(new DvrEventInfo
            {
                EventType = codeMatch.Success ? codeMatch.Groups[1].Value : "Unknown",
                Channel = channelMatch.Success ? int.Parse(channelMatch.Groups[1].Value) : 0,
                Timestamp = DateTime.UtcNow,
                Description = line.Trim()
            });
        }

        return events;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _eventPollTimer?.Dispose();
        _http.Dispose();
    }
}
