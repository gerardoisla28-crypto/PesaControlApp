using Microsoft.EntityFrameworkCore;
using PesaControl.Core.Models;
using PesaControl.Core.Security;

namespace PesaControl.Data.Data;

public class DatabaseService
{
    private readonly string _databasePath;
    private readonly IPasswordHasher _passwordHasher = new Argon2idPasswordHasher();

    public DatabaseService() : this(DatabaseSettings.GetDatabasePath())
    {
    }

    public DatabaseService(string databasePath)
    {
        _databasePath = databasePath;
    }

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_databasePath))!);

        using var context = CreateContext();
        await context.Database.MigrateAsync();
        // WAL permite leer mientras otro hilo escribe (báscula, cámaras y pantallas usan la base a la vez).
        await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");

        await MigrateLegacyScaleConfigAsync(context);
    }

    public AppDbContext CreateContext() => new LocalDbContext(_databasePath);

    public const string StationCodeKey = "station_code";

    // El código identifica la serie de tickets de este puesto; vive solo en el almacén local.
    public async Task<string?> GetStationCodeAsync()
    {
        await using var context = CreateContext();
        var value = await context.SystemConfigs
            .Where(c => c.Key == StationCodeKey)
            .Select(c => c.Value)
            .FirstOrDefaultAsync();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    public async Task SetStationCodeAsync(string code)
    {
        code = StationCodePolicy.Normalize(code);
        if (!StationCodePolicy.IsValid(code)) throw new ArgumentException(StationCodePolicy.Describe());

        await using var context = CreateContext();
        var config = await context.SystemConfigs.FirstOrDefaultAsync(c => c.Key == StationCodeKey);
        if (config is null)
            context.SystemConfigs.Add(new SystemConfig { Key = StationCodeKey, Value = code });
        else
            config.Value = code;
        await context.SaveChangesAsync();
    }

    // ── Opciones de protocolos configurables ──────────────
    // Se guardan como JSON en SystemConfigs (sin tocar el esquema): clave scale_options_{Id de báscula}.

    private const string ScaleOptionsKeyPrefix = "scale_options_";
    private const int MaxConfigValueLength = 500;

    public async Task LoadScaleOptionsAsync(IEnumerable<ScaleConfig> scales)
    {
        var list = scales.ToList();
        if (list.Count == 0) return;

        var keys = list.Select(s => ScaleOptionsKeyPrefix + s.Id).ToList();
        await using var context = CreateContext();
        var rows = await context.SystemConfigs.Where(c => keys.Contains(c.Key)).ToListAsync();
        var byKey = rows.ToDictionary(r => r.Key, r => r.Value);

        foreach (var scale in list)
            scale.Options = byKey.TryGetValue(ScaleOptionsKeyPrefix + scale.Id, out var json)
                ? ProtocolOptions.FromJson(json)
                : null;
    }

    public async Task SaveScaleOptionsAsync(ScaleConfig scale)
    {
        var key = ScaleOptionsKeyPrefix + scale.Id;
        await using var context = CreateContext();
        var row = await context.SystemConfigs.FirstOrDefaultAsync(c => c.Key == key);

        if (scale.Options is null)
        {
            if (row is not null) context.SystemConfigs.Remove(row);
        }
        else
        {
            var json = scale.Options.ToJson();
            if (json.Length > MaxConfigValueLength)
                throw new InvalidOperationException($"Los parámetros del protocolo son demasiado largos ({json.Length} de {MaxConfigValueLength} caracteres). Acorte los patrones o comandos.");

            if (row is null) context.SystemConfigs.Add(new SystemConfig { Key = key, Value = json });
            else row.Value = json;
        }
        await context.SaveChangesAsync();
    }

    public async Task DeleteScaleOptionsAsync(int scaleId)
    {
        var key = ScaleOptionsKeyPrefix + scaleId;
        await using var context = CreateContext();
        var row = await context.SystemConfigs.FirstOrDefaultAsync(c => c.Key == key);
        if (row is null) return;
        context.SystemConfigs.Remove(row);
        await context.SaveChangesAsync();
    }

    // ── Preferencias simples de este puesto (sí/no) ──────────

    public async Task<bool> GetFlagAsync(string key, bool defaultValue)
    {
        await using var context = CreateContext();
        var value = await context.SystemConfigs
            .Where(c => c.Key == key)
            .Select(c => c.Value)
            .FirstOrDefaultAsync();
        return value is null ? defaultValue : value == "1";
    }

    public async Task SetFlagAsync(string key, bool value)
    {
        await using var context = CreateContext();
        var row = await context.SystemConfigs.FirstOrDefaultAsync(c => c.Key == key);
        if (row is null) context.SystemConfigs.Add(new SystemConfig { Key = key, Value = value ? "1" : "0" });
        else row.Value = value ? "1" : "0";
        await context.SaveChangesAsync();
    }

    public async Task<bool> HasAdminAsync()
    {
        await using var context = CreateContext();
        return await context.Users.AnyAsync(u => u.Role == UserRoles.Admin);
    }

    public async Task CreateInitialAdminAsync(string username, string password, string fullName = "")
    {
        ValidateCredentials(UserRoles.Admin, username, password);
        await using var context = CreateContext();
        if (await context.Users.AnyAsync(u => u.Role == UserRoles.Admin))
            throw new InvalidOperationException("Ya existe un administrador de cliente.");
        if (await context.Users.AnyAsync(u => u.Username == username))
            throw new InvalidOperationException("Ya existe un usuario con ese nombre.");

        context.Users.Add(new User { Username = username, FullName = fullName, Role = UserRoles.Admin,
            PasswordHash = _passwordHasher.Hash(password), IsActive = true, CreatedAt = DateTime.UtcNow });
        await context.SaveChangesAsync();
    }

    public async Task ProvisionSuperAdminAsync(string username, string password, string fullName = "")
    {
        ValidateCredentials(UserRoles.SuperAdmin, username, password);
        await using var context = CreateContext();
        if (await context.Users.AnyAsync(u => u.Role == UserRoles.SuperAdmin))
            throw new InvalidOperationException("Ya existe un superadministrador. Use el procedimiento de restablecimiento autorizado.");
        if (await context.Users.AnyAsync(u => u.Username == username))
            throw new InvalidOperationException("Ya existe un usuario con ese nombre.");

        context.Users.Add(new User { Username = username, FullName = fullName, Role = UserRoles.SuperAdmin,
            PasswordHash = _passwordHasher.Hash(password), IsActive = true, CreatedAt = DateTime.UtcNow });
        await context.SaveChangesAsync();
    }

    private static void ValidateCredentials(string role, string username, string password)
    {
        if (!UsernamePolicy.IsValid(username)) throw new ArgumentException(UsernamePolicy.Describe());
        if (!PasswordPolicy.IsValid(role, password)) throw new ArgumentException(PasswordPolicy.Describe(role));
    }

    private async Task MigrateLegacyScaleConfigAsync(AppDbContext context)
    {
        if (await context.ScaleConfigs.AnyAsync())
            return;

        var configs = await context.SystemConfigs.ToListAsync();
        var dict = configs.ToDictionary(c => c.Key, c => c.Value);

        // Sin configuración antigua no hay nada que migrar: no se crea ninguna báscula por defecto.
        if (!dict.ContainsKey("weighing_protocol") && !dict.ContainsKey("serial_port"))
            return;

        var scale = new ScaleConfig
        {
            Name = "Báscula 1",
            IsActive = true,
            SortOrder = 0,
            ProtocolType = dict.GetValueOrDefault("weighing_protocol") switch
            {
                "modbus_tcp" => ProtocolType.ModbusTcp,
                "modbus_rtu" => ProtocolType.ModbusRtu,
                "sensoc" => ProtocolType.Sensoc,
                _ => ProtocolType.Serial
            },
            PortName = dict.GetValueOrDefault("serial_port", "COM1"),
            BaudRate = int.TryParse(dict.GetValueOrDefault("serial_baudrate"), out var br) ? br : 9600,
            Parity = int.TryParse(dict.GetValueOrDefault("serial_parity"), out var p) ? p : 0,
            DataBits = int.TryParse(dict.GetValueOrDefault("serial_databits"), out var db) ? db : 8,
            StopBits = int.TryParse(dict.GetValueOrDefault("serial_stopbits"), out var sb) ? sb : 1,
            IpAddress = dict.GetValueOrDefault("ip_address", ""),
            TcpPort = int.TryParse(dict.GetValueOrDefault("tcp_port"), out var tp) ? tp : 502,
            SlaveId = (byte)(int.TryParse(dict.GetValueOrDefault("modbus_slaveid"), out var si) ? si : 1),
            RegisterAddress = (ushort)(int.TryParse(dict.GetValueOrDefault("modbus_register"), out var ra) ? ra : 0),
            ReadIntervalMs = int.TryParse(dict.GetValueOrDefault("read_interval_ms"), out var ri) ? ri : 200
        };

        context.ScaleConfigs.Add(scale);
        await context.SaveChangesAsync();
    }
}
