using Microsoft.EntityFrameworkCore;
using PesaControl.Core.Models;
using PesaControl.Core.Security;

namespace PesaControl.Data.Data;

public class DatabaseService
{
    private readonly string _dbPath;
    private readonly IPasswordHasher _passwordHasher = new Argon2idPasswordHasher();

    public DatabaseService()
    {
        _dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pesacontrol.db");
    }

    public DatabaseService(string dbPath)
    {
        _dbPath = dbPath;
    }

    public async Task InitializeAsync()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        await MigrateUserPasswordsAsync(context);

        await EnsureLegacyRawSqlAsync(context);

        await context.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS DvrConfigs (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Manufacturer TEXT NOT NULL DEFAULT 'Dahua',
                Model TEXT NOT NULL DEFAULT 'DH-XVR1B04-I',
                IpAddress TEXT NOT NULL,
                HttpPort INTEGER NOT NULL DEFAULT 80,
                RtspPort INTEGER NOT NULL DEFAULT 554,
                Username TEXT NOT NULL,
                Password TEXT,
                MaxChannels INTEGER NOT NULL DEFAULT 4,
                IsActive INTEGER NOT NULL DEFAULT 1
            )");
        try
        {
            await context.Database.ExecuteSqlRawAsync(@"
                ALTER TABLE CameraConfigs ADD COLUMN DvrConfigId INTEGER NULL REFERENCES DvrConfigs(Id)");
        }
        catch { }
        try
        {
            await context.Database.ExecuteSqlRawAsync(@"
                ALTER TABLE CameraConfigs ADD COLUMN ChannelNumber INTEGER NULL");
        }
        catch { }
        try
        {
            await context.Database.ExecuteSqlRawAsync(@"
                ALTER TABLE CameraConfigs ADD COLUMN Subtype INTEGER NOT NULL DEFAULT 0");
        }
        catch { }

        await MigrateLegacyScaleConfigAsync(context);
        await SeedAsync(context);
    }

    private async Task EnsureLegacyRawSqlAsync(AppDbContext context)
    {
        await context.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS Drivers (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                FullName TEXT NOT NULL,
                Phone TEXT,
                CompanyId INTEGER NOT NULL,
                IsActive INTEGER NOT NULL DEFAULT 1,
                FOREIGN KEY (CompanyId) REFERENCES Companies(Id) ON DELETE CASCADE
            )");
        try
        {
            await context.Database.ExecuteSqlRawAsync(@"
                ALTER TABLE WeighingRecords ADD COLUMN PhotoPathExit TEXT NULL");
        }
        catch { }
        try
        {
            await context.Database.ExecuteSqlRawAsync(@"
                ALTER TABLE WeighingRecords ADD COLUMN EntryScaleName TEXT NULL");
        }
        catch { }
        try
        {
            await context.Database.ExecuteSqlRawAsync(@"
                ALTER TABLE WeighingRecords ADD COLUMN ExitScaleName TEXT NULL");
        }
        catch { }
        try
        {
            await context.Database.ExecuteSqlRawAsync(@"
                ALTER TABLE WeighingRecords ADD COLUMN IsExitWeightManual INTEGER NOT NULL DEFAULT 0");
        }
        catch { }
        await context.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS ScaleConfigs (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                IsActive INTEGER NOT NULL DEFAULT 1,
                SortOrder INTEGER NOT NULL DEFAULT 0,
                ProtocolType TEXT NOT NULL DEFAULT 'Serial',
                PortName TEXT,
                BaudRate INTEGER NOT NULL DEFAULT 9600,
                Parity INTEGER NOT NULL DEFAULT 0,
                DataBits INTEGER NOT NULL DEFAULT 8,
                StopBits INTEGER NOT NULL DEFAULT 1,
                IpAddress TEXT,
                TcpPort INTEGER NOT NULL DEFAULT 502,
                SlaveId INTEGER NOT NULL DEFAULT 1,
                RegisterAddress INTEGER NOT NULL DEFAULT 0,
                ReadIntervalMs INTEGER NOT NULL DEFAULT 200
            )");
    }

    public AppDbContext CreateContext() => new(_dbPath);

    public async Task<bool> HasAdminAsync()
    {
        await using var context = CreateContext();
        return await context.Users.AnyAsync(u => u.Role == UserRoles.Admin);
    }

    public async Task CreateInitialAdminAsync(string username, string password, string fullName = "")
    {
        ValidateCredentials(username, password);
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
        ValidateCredentials(username, password);
        await using var context = CreateContext();
        if (await context.Users.AnyAsync(u => u.Role == UserRoles.SuperAdmin))
            throw new InvalidOperationException("Ya existe un superadministrador. Use el procedimiento de restablecimiento autorizado.");
        if (await context.Users.AnyAsync(u => u.Username == username))
            throw new InvalidOperationException("Ya existe un usuario con ese nombre.");

        context.Users.Add(new User { Username = username, FullName = fullName, Role = UserRoles.SuperAdmin,
            PasswordHash = _passwordHasher.Hash(password), IsActive = true, CreatedAt = DateTime.UtcNow });
        await context.SaveChangesAsync();
    }

    private async Task MigrateUserPasswordsAsync(AppDbContext context)
    {
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Users ADD COLUMN PasswordHash TEXT NULL"); }
        catch { /* New databases already have the column. */ }

        var legacyUsers = new List<(int Id, string Password)>();
        await using var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id, Password FROM Users WHERE (PasswordHash IS NULL OR PasswordHash = '') AND Password <> ''";
            try
            {
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync()) legacyUsers.Add((reader.GetInt32(0), reader.GetString(1)));
            }
            catch { return; } // No legacy Password column in a newly-created database.
        }

        if (legacyUsers.Count == 0) return;
        await using var transaction = await connection.BeginTransactionAsync();
        foreach (var (id, password) in legacyUsers)
        {
            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = "UPDATE Users SET PasswordHash = $hash, Password = '' WHERE Id = $id";
            var hash = update.CreateParameter(); hash.ParameterName = "$hash"; hash.Value = _passwordHasher.Hash(password); update.Parameters.Add(hash);
            var key = update.CreateParameter(); key.ParameterName = "$id"; key.Value = id; update.Parameters.Add(key);
            await update.ExecuteNonQueryAsync();
        }
        await transaction.CommitAsync();
    }

    private static void ValidateCredentials(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username)) throw new ArgumentException("El nombre de usuario es obligatorio.");
        if (password.Length is < 14 or > 128) throw new ArgumentException("La contraseña debe tener entre 14 y 128 caracteres.");
    }

    private async Task SeedAsync(AppDbContext context)
    {
        if (await context.Users.AnyAsync() || await context.SystemConfigs.AnyAsync())
        {
            await SeedMissingAsync(context);
            return;
        }

        context.Products.AddRange(
            new Product { Name = "Áridos", IsActive = true },
            new Product { Name = "Cemento", IsActive = true },
            new Product { Name = "Hierro", IsActive = true },
            new Product { Name = "Madera", IsActive = true },
            new Product { Name = "Combustible", IsActive = true },
            new Product { Name = "Granos", IsActive = true }
        );

        context.Companies.AddRange(
            new Company { Name = "Empresa Demo Ltda.", RUT = "76.123.456-7", Address = "Av. Principal 123", Phone = "+56 9 1234 5678", IsActive = true },
            new Company { Name = "Transportes del Sur SA", RUT = "77.987.654-3", Address = "Ruta 5 Km 345", Phone = "+56 9 9876 5432", IsActive = true }
        );

        // Save companies first so we get their IDs for operator FK
        await context.SaveChangesAsync();

        var empresaDemo = await context.Companies.FirstAsync(c => c.Name == "Empresa Demo Ltda.");
        var transportesSur = await context.Companies.FirstAsync(c => c.Name == "Transportes del Sur SA");

        context.Operators.AddRange(
            new Operator { FullName = "Carlos Muñoz", Phone = "+56 9 1111 1111", CompanyId = empresaDemo.Id, IsActive = true },
            new Operator { FullName = "María González", Phone = "+56 9 2222 2222", CompanyId = empresaDemo.Id, IsActive = true },
            new Operator { FullName = "Pedro Soto", Phone = "+56 9 3333 3333", CompanyId = transportesSur.Id, IsActive = true },
            new Operator { FullName = "Ana López", Phone = "+56 9 4444 4444", CompanyId = transportesSur.Id, IsActive = true },
            new Operator { FullName = "José Ramírez", Phone = "+56 9 5555 5555", CompanyId = transportesSur.Id, IsActive = true }
        );

        context.Drivers.AddRange(
            new Driver { FullName = "Juan Pérez", Phone = "+56 9 6666 6666", CompanyId = empresaDemo.Id, IsActive = true },
            new Driver { FullName = "Pedro López", Phone = "+56 9 7777 7777", CompanyId = empresaDemo.Id, IsActive = true },
            new Driver { FullName = "Luis Martínez", Phone = "+56 9 8888 8888", CompanyId = transportesSur.Id, IsActive = true },
            new Driver { FullName = "Diego Rojas", Phone = "+56 9 9999 9999", CompanyId = transportesSur.Id, IsActive = true },
            new Driver { FullName = "Sergio Castro", Phone = "+56 9 1010 1010", CompanyId = transportesSur.Id, IsActive = true }
        );

        context.SystemConfigs.AddRange(
            new SystemConfig { Key = "company_name", Value = "PesaControl Pro" },
            new SystemConfig { Key = "company_rut", Value = "76.000.000-0" },
            new SystemConfig { Key = "weighing_protocol", Value = "serial" },
            new SystemConfig { Key = "serial_port", Value = "COM1" },
            new SystemConfig { Key = "serial_baudrate", Value = "9600" },
            new SystemConfig { Key = "serial_parity", Value = "0" },
            new SystemConfig { Key = "serial_databits", Value = "8" },
            new SystemConfig { Key = "serial_stopbits", Value = "1" },
            new SystemConfig { Key = "ip_address", Value = "192.168.1.100" },
            new SystemConfig { Key = "tcp_port", Value = "502" },
            new SystemConfig { Key = "modbus_slaveid", Value = "1" },
            new SystemConfig { Key = "modbus_register", Value = "0" },
            new SystemConfig { Key = "read_interval_ms", Value = "200" },
            new SystemConfig { Key = "platerecognizer_api_key", Value = "" },
            new SystemConfig { Key = "receiving_company", Value = "Mi Empresa" }
        );

        context.CameraConfigs.AddRange(
            new CameraConfig { Name = "Cámara USB 1", Url = "0", Type = CameraType.USB, IsActive = true },
            new CameraConfig { Name = "Cámara IP Principal", Url = "http://192.168.1.200/video", Type = CameraType.IP, IsActive = true }
        );

        await context.SaveChangesAsync();
    }

    private async Task MigrateLegacyScaleConfigAsync(AppDbContext context)
    {
        if (await context.ScaleConfigs.AnyAsync())
            return;

        var configs = await context.SystemConfigs.ToListAsync();
        var dict = configs.ToDictionary(c => c.Key, c => c.Value);

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

    private async Task SeedMissingAsync(AppDbContext context)
    {
        if (!await context.Drivers.AnyAsync() && await context.Companies.AnyAsync())
        {
            var empresaDemo = await context.Companies.FirstAsync(c => c.Name == "Empresa Demo Ltda.");
            var transportesSur = await context.Companies.FirstAsync(c => c.Name == "Transportes del Sur SA");

            context.Drivers.AddRange(
                new Driver { FullName = "Juan Pérez", Phone = "+56 9 6666 6666", CompanyId = empresaDemo.Id, IsActive = true },
                new Driver { FullName = "Pedro López", Phone = "+56 9 7777 7777", CompanyId = empresaDemo.Id, IsActive = true },
                new Driver { FullName = "Luis Martínez", Phone = "+56 9 8888 8888", CompanyId = transportesSur.Id, IsActive = true },
                new Driver { FullName = "Diego Rojas", Phone = "+56 9 9999 9999", CompanyId = transportesSur.Id, IsActive = true },
                new Driver { FullName = "Sergio Castro", Phone = "+56 9 1010 1010", CompanyId = transportesSur.Id, IsActive = true }
            );
        }

        if (!await context.SystemConfigs.AnyAsync(c => c.Key == "receiving_company"))
        {
            context.SystemConfigs.Add(new SystemConfig { Key = "receiving_company", Value = "Mi Empresa" });
        }

        await context.SaveChangesAsync();
    }
}
