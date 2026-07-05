using Microsoft.EntityFrameworkCore;
using PesaControl.Core.Models;

namespace PesaControl.Data.Data;

public class DatabaseService
{
    private readonly string _dbPath;

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
        catch
        {
            // Column already exists — ignore
        }
        await SeedAsync(context);
    }

    public AppDbContext CreateContext() => new(_dbPath);

    private async Task SeedAsync(AppDbContext context)
    {
        if (await context.Users.AnyAsync())
        {
            await SeedMissingAsync(context);
            return;
        }

        context.Users.Add(new User
        {
            Username = "admin",
            Password = "1234",
            FullName = "Administrador",
            Email = "admin@pesacontrol.cl",
            Role = "Admin",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });

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
