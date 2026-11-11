using System.Collections.ObjectModel;
using System.IO.Ports;
using System.Text.Json;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PesaControl.Core.Models;
using PesaControl.Core.Security;
using PesaControl.Core.Services;
using PesaControl.Data.Data;
using PesaControl.Data.Repositories;
using PesaControl.Data.Services;

namespace PesaControl.UI.ViewModels;

public partial class SettingsViewModel : ObservableObject, IUnsavedChanges
{
    private readonly DatabaseService _db;
    private readonly ScaleManager? _scaleManager;
    private readonly WeighingViewModel? _weighingVm;

    [ObservableProperty] private BackupViewModel? _backupVM;
    [ObservableProperty] private int _selectedTabIndex;

    // Pestañas "Cámaras" y "Control": antes eran secciones del menú; ahora solo las ve quien entra a Configuración.
    [ObservableProperty] private CameraViewModel? _cameraVM;
    [ObservableProperty] private ControlsViewModel? _controlsVM;
    [ObservableProperty] private bool _isControlModuleEnabled = true;

    public SettingsViewModel(DatabaseService db, ScaleManager? scaleManager = null, WeighingViewModel? weighingVm = null, ExportImportService? exportImport = null)
    {
        _db = db;
        _scaleManager = scaleManager;
        _weighingVm = weighingVm;
        if (exportImport != null)
        {
            BackupVM = new BackupViewModel(exportImport);
        }
        else
        {
            BackupVM = new BackupViewModel(new ExportImportService(db));
        }
    }

    // ── Puesto ────────────────────────────────────────────

    [ObservableProperty] private string _stationCode = "";
    [ObservableProperty] private string _stationCodeError = "";
    [ObservableProperty] private string _stationCodeMessage = "";

    partial void OnStationCodeChanged(string value)
    {
        StationCodeError = "";
        StationCodeMessage = "";
    }

    [RelayCommand]
    private async Task SaveStationCodeAsync() => await TrySaveStationCodeAsync();

    private async Task<bool> TrySaveStationCodeAsync()
    {
        var code = StationCodePolicy.Normalize(StationCode);
        if (!StationCodePolicy.IsValid(code))
        {
            StationCodeError = StationCodePolicy.Describe();
            ShowTab(StationTabIndex);
            return false;
        }

        try
        {
            await _db.SetStationCodeAsync(code);
            _savedStationCode = code;
            StationCode = code;
            StationCodeMessage = $"Código guardado. Los próximos tickets usarán la serie {code}-…";
            return true;
        }
        catch (Exception ex)
        {
            StationCodeError = $"No se pudo guardar: {ex.Message}";
            ShowTab(StationTabIndex);
            return false;
        }
    }

    // ── Cambios sin guardar ───────────────────────────────

    private const int StationTabIndex = 1;

    private string _savedStationCode = "";
    private string _savedModeName = "Local";
    private string _savedServerPort = "5000";
    private string _savedServerUrl = "http://192.168.1.100:5000";
    private string _scaleSnapshot = "";

    private static string SnapshotScale(ScaleConfig? scale) => scale is null ? "" : JsonSerializer.Serialize(scale);
    private string SnapshotScale() => SnapshotScale(EditingScale);

    private bool StationCodeDirty => StationCodePolicy.Normalize(StationCode) != _savedStationCode;
    private bool NetworkDirty => SelectedModeName != _savedModeName || ServerPort != _savedServerPort || ServerUrl != _savedServerUrl;
    private bool ScaleDirty => IsScaleEditorOpen && SnapshotScale() != _scaleSnapshot;

    private const int ScalesTabIndex = 0;
    private const int NetworkTabIndex = 2;
    private const int ControlTabIndex = 5;

    private bool IsTabDirty(int tab) => tab switch
    {
        ScalesTabIndex => ScaleDirty,
        StationTabIndex => StationCodeDirty,
        NetworkTabIndex => NetworkDirty,
        ControlTabIndex => ControlsVM?.HasUnsavedChanges == true,
        _ => false
    };

    private string TabName(int tab) => tab switch
    {
        ScalesTabIndex => "Básculas",
        StationTabIndex => "Puesto",
        NetworkTabIndex => "Red",
        ControlTabIndex => ControlsVM?.UnsavedChangesDescription ?? "Control",
        _ => ""
    };

    public bool HasUnsavedChanges => IsTabDirty(ScalesTabIndex) || IsTabDirty(StationTabIndex) || IsTabDirty(NetworkTabIndex) || IsTabDirty(ControlTabIndex);

    public string UnsavedChangesDescription
        => string.Join(", ", new[] { ScalesTabIndex, StationTabIndex, NetworkTabIndex, ControlTabIndex }.Where(IsTabDirty).Select(TabName));

    // Al guardar desde el aviso se valida primero; si falla, se queda en la pestaña con el error.
    private async Task<bool> SaveTabAsync(int tab)
    {
        switch (tab)
        {
            case StationTabIndex:
                return await TrySaveStationCodeAsync();
            case NetworkTabIndex:
                await SaveConfigAsync();
                return !NetworkDirty;
            case ScalesTabIndex:
                await SaveScaleAsync();
                return !IsScaleEditorOpen;
            case ControlTabIndex:
                return ControlsVM is null || await ControlsVM.SaveChangesAsync();
            default:
                return true;
        }
    }

    private void DiscardTab(int tab)
    {
        switch (tab)
        {
            case StationTabIndex:
                StationCode = _savedStationCode;
                StationCodeError = "";
                break;
            case NetworkTabIndex:
                SelectedModeName = _savedModeName;
                ServerPort = _savedServerPort;
                ServerUrl = _savedServerUrl;
                break;
            case ScalesTabIndex:
                if (IsScaleEditorOpen) CancelScaleEditor();
                break;
            case ControlTabIndex:
                ControlsVM?.DiscardChanges();
                break;
        }
    }

    public async Task<bool> SaveChangesAsync()
    {
        foreach (var tab in new[] { ScalesTabIndex, StationTabIndex, NetworkTabIndex, ControlTabIndex }.Where(IsTabDirty))
            if (!await SaveTabAsync(tab)) return false;
        return true;
    }

    public void DiscardChanges()
    {
        foreach (var tab in new[] { ScalesTabIndex, StationTabIndex, NetworkTabIndex, ControlTabIndex })
            DiscardTab(tab);
    }

    // Cambiar de pestaña dentro de Configuración también pregunta si la pestaña que se deja tiene cambios.
    public bool IsTabWithUnsavedChanges(int tab) => IsTabDirty(tab);

    public async Task<bool> CanLeaveTabAsync(int tab)
    {
        if (!IsTabDirty(tab)) return true;

        switch (UnsavedChangesDialog.Ask(TabName(tab)))
        {
            case UnsavedChangesChoice.Save:
                return await SaveTabAsync(tab);
            case UnsavedChangesChoice.Discard:
                DiscardTab(tab);
                return true;
            default:
                return false;
        }
    }

    // Evita que un cambio de pestaña hecho por el propio código (por ejemplo, para mostrar un error) vuelva a preguntar.
    public bool SuppressTabGuard { get; private set; }

    private void ShowTab(int tab)
    {
        SuppressTabGuard = true;
        SelectedTabIndex = tab;
        SuppressTabGuard = false;
    }

    // ── Básculas ──────────────────────────────────────────

    public ObservableCollection<ScaleConfig> ScaleConfigs { get; } = [];

    [ObservableProperty] private ScaleConfig? _selectedScale;

    [ObservableProperty] private string _scaleStatusMessage = "";

    // Editing scale (popup fields)
    [ObservableProperty] private bool _isScaleEditorOpen;
    [ObservableProperty] private ScaleConfig? _editingScale;

    [RelayCommand]
    private async Task LoadScalesAsync()
    {
        var ctx = _db.CreateContext();
        var repo = new Repository<ScaleConfig>(ctx);
        var items = await repo.GetAllAsync();
        await _db.LoadScaleOptionsAsync(items);

        App.Current.Dispatcher.Invoke(() =>
        {
            // Recargar la lista vacía la selección: se restaura para que Editar/Eliminar sigan funcionando.
            var selectedId = SelectedScale?.Id;
            ScaleConfigs.Clear();
            foreach (var s in items.OrderBy(x => x.SortOrder))
                ScaleConfigs.Add(s);
            SelectedScale = ScaleConfigs.FirstOrDefault(s => s.Id == selectedId);
        });
    }

    [RelayCommand]
    private void AddScale()
    {
        SelectedProfileName = null;
        EditingScale = new ScaleConfig
        {
            Name = $"Báscula {ScaleConfigs.Count + 1}",
            IsActive = true,
            SortOrder = ScaleConfigs.Count,
            // Siempre hay opciones durante la edición para que los campos avanzados puedan enlazarse.
            Options = new ProtocolOptions()
        };
        SelectedProtocolName = "Serial";
        _scaleSnapshot = SnapshotScale();
        IsScaleEditorOpen = true;
    }

    [RelayCommand]
    private void EditScale()
    {
        if (SelectedScale is null && ScaleConfigs.Count == 1)
            SelectedScale = ScaleConfigs[0];

        if (SelectedScale is null)
        {
            ScaleStatusMessage = "Seleccione una báscula de la lista para editarla.";
            return;
        }

        SelectedProfileName = null;
        EditingScale = new ScaleConfig
        {
            Id = SelectedScale.Id,
            Name = SelectedScale.Name,
            IsActive = SelectedScale.IsActive,
            SortOrder = SelectedScale.SortOrder,
            ProtocolType = SelectedScale.ProtocolType,
            PortName = SelectedScale.PortName,
            BaudRate = SelectedScale.BaudRate,
            Parity = SelectedScale.Parity,
            DataBits = SelectedScale.DataBits,
            StopBits = SelectedScale.StopBits,
            IpAddress = SelectedScale.IpAddress,
            TcpPort = SelectedScale.TcpPort,
            SlaveId = SelectedScale.SlaveId,
            RegisterAddress = SelectedScale.RegisterAddress,
            ReadIntervalMs = SelectedScale.ReadIntervalMs,
            WeightDecimals = SelectedScale.WeightDecimals,
            Options = SelectedScale.Options?.Clone() ?? new ProtocolOptions()
        };
        SelectedProtocolName = ProtocolCatalog.NameOf(SelectedScale.ProtocolType);
        _scaleSnapshot = SnapshotScale();
        IsScaleEditorOpen = true;
    }

    [RelayCommand]
    private async Task SaveScaleAsync()
    {
        if (EditingScale is null) return;

        var saved = EditingScale;

        // Solo los protocolos configurables usan opciones; en los demás no se guardan.
        if (!ProtocolCatalog.IsConfigurable(saved.ProtocolType))
            saved.Options = null;
        else if (saved.Options is { } opts && opts.ToJson().Length > 500)
        {
            ScaleStatusMessage = "Los parámetros del protocolo son demasiado largos (máx. 500 caracteres). Acorte los patrones o comandos.";
            return;
        }

        var ctx = _db.CreateContext();
        var repo = new Repository<ScaleConfig>(ctx);

        // El Id se asigna al guardar: hay que recordar ahora si es nueva, no preguntarlo después.
        var isNew = saved.Id == 0;

        if (isNew)
        {
            await repo.AddAsync(saved);
        }
        else
        {
            await repo.UpdateAsync(saved);
        }
        await _db.SaveScaleOptionsAsync(saved);

        IsScaleEditorOpen = false;
        EditingScale = null;
        await LoadScalesAsync();

        if (_scaleManager != null)
        {
            if (isNew)
                await _scaleManager.AddScaleAsync(saved);
            else
                await _scaleManager.UpdateScaleConfigAsync(saved);
        }

        _weighingVm?.RefreshScaleNames();
        ScaleStatusMessage = "Báscula guardada";
    }

    [RelayCommand]
    private void CancelScaleEditor()
    {
        IsScaleEditorOpen = false;
        EditingScale = null;
    }

    [RelayCommand]
    private async Task DeleteScaleAsync()
    {
        if (SelectedScale is null)
        {
            ScaleStatusMessage = "Seleccione una báscula de la lista para eliminarla.";
            return;
        }
        var ctx = _db.CreateContext();
        var repo = new Repository<ScaleConfig>(ctx);
        var deletedId = SelectedScale.Id;
        await repo.DeleteAsync(SelectedScale);
        await _db.DeleteScaleOptionsAsync(deletedId);
        await LoadScalesAsync();

        // También se quita del gestor (se desconecta) y de la pantalla principal.
        if (_scaleManager != null)
            await _scaleManager.RemoveScaleAsync(deletedId);
        _weighingVm?.RefreshScaleNames();

        ScaleStatusMessage = "Báscula eliminada";
    }

    // ── Mode de operación ─────────────────────────────────

    [ObservableProperty] private string _selectedModeName = "Local";
    public string[] ModeNames => ["Local", "Servidor", "Cliente"];

    [ObservableProperty] private string _serverPort = "5000";
    [ObservableProperty] private string _serverUrl = "http://192.168.1.100:5000";

    // ── Protocol params (legacy - for editing single scale) ──
    [ObservableProperty] private string _selectedProtocolName = "Serial";
    public string[] ProtocolNames => ProtocolCatalog.Names;

    [ObservableProperty] private int _serialBaudRate = 9600;
    [ObservableProperty] private int _serialParity;
    [ObservableProperty] private int _serialDataBits = 8;
    [ObservableProperty] private int _serialStopBits = 1;

    [ObservableProperty] private int _tcpPort = 502;

    [ObservableProperty] private int _modbusSlaveId = 1;
    [ObservableProperty] private int _modbusRegister;
    [ObservableProperty] private int _readIntervalMs = 200;

    public record WeightDecimalOption(int Value, string Label);
    public WeightDecimalOption[] WeightDecimalOptions { get; } =
    [
        new(-1, "Automático"), new(0, "0 (sin decimales)"), new(1, "1 decimal"), new(2, "2 decimales"), new(3, "3 decimales")
    ];
    public int[] BaudRates { get; } = [300, 600, 1200, 2400, 4800, 9600, 14400, 19200, 38400, 57600, 115200];
    public int[] ParityOptions { get; } = [0, 1, 2];
    public int[] DataBitsOptions { get; } = [5, 6, 7, 8];
    public int[] StopBitsOptions { get; } = [0, 1, 2];

    [ObservableProperty] private string _statusMessage = "";

    [RelayCommand]
    private async Task LoadConfigAsync()
    {
        var ctx = _db.CreateContext();
        var repo = new Repository<SystemConfig>(ctx);
        var configs = await repo.GetAllAsync();
        var dict = configs.ToDictionary(c => c.Key, c => c.Value);
        var stationCode = await _db.GetStationCodeAsync() ?? "";

        App.Current.Dispatcher.Invoke(() =>
        {
            SelectedModeName = dict.GetValueOrDefault("operation_mode") switch
            {
                "server" => "Servidor",
                "client" => "Cliente",
                _ => "Local"
            };

            ServerPort = dict.GetValueOrDefault("server_port", "5000");
            ServerUrl = dict.GetValueOrDefault("server_url", "http://192.168.1.100:5000");

            StationCode = stationCode;
            _savedStationCode = stationCode;
            _savedModeName = SelectedModeName;
            _savedServerPort = ServerPort;
            _savedServerUrl = ServerUrl;
            StationCodeError = "";
            StationCodeMessage = "";

            StatusMessage = "Configuración cargada";
        });

        await LoadScalesAsync();
    }

    [RelayCommand]
    private async Task SaveConfigAsync()
    {
        try
        {
            var ctx = _db.CreateContext();
            var repo = new Repository<SystemConfig>(ctx);

            await UpsertAsync(repo, "operation_mode", SelectedModeName switch
            {
                "Servidor" => "server",
                "Cliente" => "client",
                _ => "local"
            });

            await UpsertAsync(repo, "server_port", ServerPort);
            await UpsertAsync(repo, "server_url", ServerUrl);

            _savedModeName = SelectedModeName;
            _savedServerPort = ServerPort;
            _savedServerUrl = ServerUrl;
            StatusMessage = "Configuración guardada correctamente";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error al guardar: {ex.Message}";
        }
    }

    private ProtocolType SelectedProtocolType => ProtocolCatalog.TypeOf(SelectedProtocolName);

    public bool IsSerialProtocol => !ProtocolCatalog.IsTcp(SelectedProtocolType);
    public bool IsTcpProtocol => ProtocolCatalog.IsTcp(SelectedProtocolType);
    public bool IsModbusCfg => ProtocolCatalog.IsModbusCfg(SelectedProtocolType);
    public bool IsAsciiCfg => ProtocolCatalog.IsAsciiCfg(SelectedProtocolType);

    public int[] ModbusFunctions { get; } = [3, 4];
    public ModbusDataType[] ModbusDataTypes { get; } = Enum.GetValues<ModbusDataType>();

    partial void OnSelectedProtocolNameChanged(string value)
    {
        OnPropertyChanged(nameof(IsSerialProtocol));
        OnPropertyChanged(nameof(IsTcpProtocol));
        OnPropertyChanged(nameof(IsModbusCfg));
        OnPropertyChanged(nameof(IsAsciiCfg));
        if (EditingScale is not null)
            EditingScale.ProtocolType = ProtocolCatalog.TypeOf(value);
    }

    // ── Perfiles de equipo ────────────────────────────────

    public string[] ProfileNames { get; } = EquipmentProfile.BuiltIn.Select(p => p.Name).ToArray();
    [ObservableProperty] private string? _selectedProfileName;

    // Elegir un protocolo de la lista rellena de inmediato el protocolo y sus parámetros; no hay botón Aplicar.
    partial void OnSelectedProfileNameChanged(string? value)
    {
        var profile = EquipmentProfile.BuiltIn.FirstOrDefault(p => p.Name == value);
        if (profile is not null && IsScaleEditorOpen)
            ApplyProfile(profile);
    }

    private void ApplyProfile(EquipmentProfile profile)
    {
        if (EditingScale is null) return;

        // ScaleConfig no notifica cambios: se reasigna entero para que la pantalla se refresque.
        EditingScale = profile.ApplyTo(EditingScale);
        SelectedProtocolName = ProtocolCatalog.NameOf(profile.Protocol);
        ScaleStatusMessage = $"{profile.Name}: {profile.Description} Indique el puerto o la IP y pulse Guardar.";
    }

    // Las básculas conectadas ocupan su puerto: se sueltan mientras se detecta o se prueba y se reconectan al terminar.
    private async Task<List<ScaleInstance>> ReleaseConnectedScalesAsync()
    {
        var released = new List<ScaleInstance>();
        if (_scaleManager == null) return released;

        foreach (var s in _scaleManager.Scales.Where(s => s.IsConnected).ToList())
        {
            await s.Service.DisconnectAsync();
            released.Add(s);
        }
        return released;
    }

    private static async Task ReconnectScalesAsync(IEnumerable<ScaleInstance> scales)
    {
        foreach (var s in scales)
        {
            try { await s.Service.ConnectAsync(); } catch { }
        }
    }

    public string[] AvailablePorts => SerialPort.GetPortNames();

    [ObservableProperty] private bool _isDetecting;

    // Prueba los puertos y velocidades habituales y rellena la báscula con lo que encuentre.
    [RelayCommand]
    private async Task DetectEquipmentAsync()
    {
        if (IsDetecting || EditingScale is null) return;

        IsDetecting = true;
        var progress = new Progress<string>(m => ScaleStatusMessage = m);
        var reconnect = new List<ScaleInstance>();

        try
        {
            reconnect = await ReleaseConnectedScalesAsync();

            var ports = AvailablePorts.OrderBy(p => p == EditingScale.PortName ? 0 : 1).ThenBy(p => p).ToList();
            if (ports.Count == 0)
            {
                ScaleStatusMessage = "No hay puertos serie en este equipo. Conecte el cable o adaptador USB-serie.";
                return;
            }

            var result = await Task.Run(() => new EquipmentDetector().DetectAsync(ports, progress));

            if (result is null)
            {
                ScaleStatusMessage = "No se detectó el equipo. Verifique cable y alimentación, que ningún otro programa use el puerto, " +
                                     "y que el visor transmita en continuo o esté en Modbus RTU (Kronos: COM > RS-232 > PC > PROTOC = MOD.RTU).";
                return;
            }

            var detected = new ScaleConfig
            {
                Id = EditingScale.Id,
                Name = EditingScale.Name,
                IsActive = EditingScale.IsActive,
                SortOrder = EditingScale.SortOrder,
                ProtocolType = result.Protocol,
                PortName = result.PortName,
                BaudRate = result.BaudRate,
                Parity = result.Parity,
                DataBits = result.DataBits,
                StopBits = result.StopBits,
                IpAddress = EditingScale.IpAddress,
                TcpPort = EditingScale.TcpPort,
                SlaveId = result.SlaveId,
                RegisterAddress = EditingScale.RegisterAddress,
                ReadIntervalMs = EditingScale.ReadIntervalMs,
                Options = result.Options ?? EditingScale.Options ?? new ProtocolOptions()
            };

            // ScaleConfig no notifica cambios: se reasigna entero para que la pantalla se refresque.
            EditingScale = detected;
            SelectedProtocolName = ProtocolCatalog.NameOf(result.Protocol);

            ScaleStatusMessage = $"Detectado: {result.Description} en {result.PortName} a {result.BaudRate} baud. " +
                                 "Revise los datos y pulse Guardar.";
        }
        catch (Exception ex)
        {
            ScaleStatusMessage = $"Error al detectar: {ex.Message}";
        }
        finally
        {
            await ReconnectScalesAsync(reconnect);
            IsDetecting = false;
        }
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        if (EditingScale is null) return;

        ScaleStatusMessage = "Probando conexión...";
        var reconnect = new List<ScaleInstance>();

        try
        {
            reconnect = await ReleaseConnectedScalesAsync();

            // Se prueba exactamente lo que hay en el editor (incluye esclavo, registro y opciones).
            var scale = EditingScale;
            var config = scale.ToCommunicationConfig();
            var protocol = ScaleManager.CreateProtocol(scale);
            var where = ProtocolCatalog.IsTcp(scale.ProtocolType) ? $"{config.IpAddress}:{config.TcpPort}" : config.PortName;

            var service = new WeighingService(protocol, config);
            try
            {
                if (ProtocolCatalog.IsEventDriven(scale.ProtocolType))
                {
                    WeightReading? reading = null;
                    protocol.WeightChanged += (_, r) => reading = r;
                    string? lastError = null;
                    protocol.ErrorOccurred += (_, e) => lastError = e;

                    await service.ConnectAsync();
                    await Task.Delay(3000);

                    if (!protocol.IsConnected)
                        ScaleStatusMessage = lastError ?? $"No se pudo abrir {where}. Verifique que exista y que ningún otro programa lo use.";
                    else if (reading is { } r)
                        ScaleStatusMessage = $"Conexión exitosa — peso: {r.Value} kg{(r.IsStable ? "" : " (inestable)")}";
                    else
                        ScaleStatusMessage = lastError ?? "Conectado, pero sin respuesta del equipo. Verifique protocolo, esclavo, baudios y formato.";
                }
                else
                {
                    await service.ConnectAsync();
                    await Task.Delay(500);
                    await service.RequestWeightAsync();

                    ScaleStatusMessage = service.LastReading is { } last && last.Value != 0
                        ? $"Conexión exitosa — peso: {last.Value} kg"
                        : protocol.IsConnected ? "Conexión exitosa" : $"No se pudo abrir {where}.";
                }
            }
            finally
            {
                await service.DisconnectAsync();
            }
        }
        catch (Exception ex)
        {
            ScaleStatusMessage = $"Error de conexión: {ex.Message}";
        }
        finally
        {
            await ReconnectScalesAsync(reconnect);
        }
    }

    private static async Task UpsertAsync(IRepository<SystemConfig> repo, string key, string value)
    {
        var existing = await repo.FindAsync(c => c.Key == key);
        var entry = existing.FirstOrDefault();

        if (entry is not null)
        {
            entry.Value = value;
            await repo.UpdateAsync(entry);
        }
        else
        {
            await repo.AddAsync(new SystemConfig { Key = key, Value = value });
        }
    }
}
