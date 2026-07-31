using System.Text;
using PesaControl.Core.Models;

namespace PesaControl.Core.Services;

public class ScaleInstance
{
    public ScaleConfig Config { get; }
    public WeighingService Service { get; }

    public decimal CurrentWeight => Service.CurrentWeight;
    public bool IsStable => Service.IsStable;
    public bool IsConnected => Service.IsConnected;
    public string Name => Config.Name;

    public ScaleInstance(ScaleConfig config, WeighingService service)
    {
        Config = config;
        Service = service;
    }
}

public class ScaleManager : IDisposable
{
    private readonly List<ScaleInstance> _scales = [];

    public IReadOnlyList<ScaleInstance> Scales => _scales;
    public int Count => _scales.Count;

    public event EventHandler<ScaleInstance>? ScaleAdded;

    public async Task InitializeAsync(IEnumerable<ScaleConfig> configs)
    {
        foreach (var config in configs.Where(c => c.IsActive).OrderBy(c => c.SortOrder))
        {
            var protocol = CreateProtocol(config);
            var weighingService = new WeighingService(protocol, config.ToCommunicationConfig());
            var instance = new ScaleInstance(config, weighingService);
            _scales.Add(instance);
            ScaleAdded?.Invoke(this, instance);
        }

        LogConfigs();
    }

    public async Task ConnectAllAsync()
    {
        var usedPorts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var scale in _scales)
        {
            var port = scale.Config.PortName;
            if (!string.IsNullOrEmpty(port) && !usedPorts.Add(port))
            {
                LogDebug($"WARN", $"Báscula '{scale.Name}' en {port} — puerto ya usado por otra báscula, se omite");
                continue;
            }
            await scale.Service.ConnectAsync();
        }
    }

    public async Task DisconnectAllAsync()
    {
        foreach (var scale in _scales)
        {
            await scale.Service.DisconnectAsync();
        }
    }

    public ScaleInstance? GetById(int id) =>
        _scales.FirstOrDefault(s => s.Config.Id == id);

    public ScaleInstance? GetByName(string name) =>
        _scales.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public ScaleInstance? GetByIndex(int index) =>
        index >= 0 && index < _scales.Count ? _scales[index] : null;

    public async Task UpdateScaleConfigAsync(ScaleConfig updated)
    {
        var instance = _scales.FirstOrDefault(s => s.Config.Id == updated.Id);
        if (instance != null)
        {
            instance.Config.Name = updated.Name;
            instance.Config.IsActive = updated.IsActive;
            instance.Config.SortOrder = updated.SortOrder;
            instance.Config.ProtocolType = updated.ProtocolType;
            instance.Config.PortName = updated.PortName;
            instance.Config.BaudRate = updated.BaudRate;
            instance.Config.Parity = updated.Parity;
            instance.Config.DataBits = updated.DataBits;
            instance.Config.StopBits = updated.StopBits;
            instance.Config.IpAddress = updated.IpAddress;
            instance.Config.TcpPort = updated.TcpPort;
            instance.Config.SlaveId = updated.SlaveId;
            instance.Config.RegisterAddress = updated.RegisterAddress;
            instance.Config.ReadIntervalMs = updated.ReadIntervalMs;

            var newProtocol = CreateProtocol(updated);
            await instance.Service.SetProtocol(newProtocol, updated.ToCommunicationConfig());
        }
    }

    public async Task AddScaleAsync(ScaleConfig config)
    {
        var protocol = CreateProtocol(config);
        var weighingService = new WeighingService(protocol, config.ToCommunicationConfig());
        var instance = new ScaleInstance(config, weighingService);
        _scales.Add(instance);
        ScaleAdded?.Invoke(this, instance);
        await Task.CompletedTask;
    }

    public ScaleInstance? FirstOrDefault =>
        _scales.FirstOrDefault();

    private static IWeighingProtocol CreateProtocol(ScaleConfig config)
    {
        return config.ProtocolType switch
        {
            ProtocolType.ModbusTcp => new ModbusTcpProtocol(),
            ProtocolType.ModbusRtu => new ModbusRtuProtocol(),
            ProtocolType.Sensoc => new SensocProtocol(),
            ProtocolType.SensocarA12 => new SensocarA12Protocol(),
            _ => new SerialProtocol()
        };
    }

    private void LogConfigs()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[ScaleManager] {_scales.Count} báscula(s) configurada(s):");
        foreach (var s in _scales)
        {
            var protoName = s.Service.Protocol.Name;
            var port = s.Config.PortName;
            sb.AppendLine($"  - '{s.Config.Name}': {protoName} en {port}");
        }
        var msg = sb.ToString();
        try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "PesaControl_debug.log"), msg); } catch { }
    }

    private static void LogDebug(string label, string data)
    {
        var msg = $"[{DateTime.Now:HH:mm:ss.fff}] [ScaleManager] {label} {data}{Environment.NewLine}";
        try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "PesaControl_debug.log"), msg); } catch { }
    }

    public void Dispose()
    {
        foreach (var scale in _scales)
        {
            (scale.Service as IDisposable)?.Dispose();
            (scale.Service.Protocol as IDisposable)?.Dispose();
        }
        _scales.Clear();
    }
}
