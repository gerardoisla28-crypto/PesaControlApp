using Microsoft.EntityFrameworkCore;
using PesaControl.Core.Models;
using PesaControl.Core.Security;
using PesaControl.Core.Services;

namespace PesaControl.Data.Data;

/// <summary>
/// Llena una base de datos con información ficticia chilena para demostraciones y videos.
/// Solo se ejecuta sobre una base indicada con PESACONTROL_DB_PATH, nunca sobre la base real del puesto.
/// </summary>
public static class DemoDataSeeder
{
    public const string EnableEnvironmentVariable = "PESACONTROL_DEMO";

    // Credenciales de la base de demostración. No sirven en una instalación real.
    public const string AdminUser = "admin";
    public const string AdminPassword = "AdminDemo2026-Chile";
    public const string OperatorUser = "operador";
    public const string OperatorPassword = "OperadorDemo2026";
    public const string SecondOperatorUser = "operadora";
    public const string SecondOperatorPassword = "OperadoraDemo2026";

    private const string MarkerKey = "demo_data_seeded";
    private const string StationCode = "STGO";
    private const string PlantName = "Planta Central Maipú";

    public static bool IsRequested() =>
        Environment.GetEnvironmentVariable(EnableEnvironmentVariable) == "1";

    public static async Task SeedAsync(DatabaseService database)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(DatabaseSettings.PathEnvironmentVariable)))
            throw new InvalidOperationException(
                $"El modo demostración exige definir {DatabaseSettings.PathEnvironmentVariable} con una base aparte, para no alterar la base real.");

        await using var context = database.CreateContext();
        if (await context.SystemConfigs.AnyAsync(c => c.Key == MarkerKey))
            return;

        var random = new Random(20261104);

        await SeedUsersAsync(context);
        await SeedSettingsAsync(context);
        var companies = await SeedCatalogsAsync(context);
        await context.SaveChangesAsync();

        await SeedWeighingsAsync(context, random, companies);

        context.SystemConfigs.Add(new SystemConfig { Key = MarkerKey, Value = "1" });
        await context.SaveChangesAsync();
    }

    private static async Task SeedUsersAsync(AppDbContext context)
    {
        var hasher = new Argon2idPasswordHasher();
        var users = new[]
        {
            (AdminUser, AdminPassword, "Administrador Demo", UserRoles.Admin),
            (OperatorUser, OperatorPassword, "Carlos Muñoz", UserRoles.Operator),
            (SecondOperatorUser, SecondOperatorPassword, "María González", UserRoles.Operator),
        };
        foreach (var (username, password, fullName, role) in users)
        {
            if (await context.Users.AnyAsync(u => u.Username == username)) continue;
            context.Users.Add(new User
            {
                Username = username,
                FullName = fullName,
                Role = role,
                PasswordHash = hasher.Hash(password),
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
        }
    }

    private static async Task SeedSettingsAsync(AppDbContext context)
    {
        await SetConfigAsync(context, DatabaseService.StationCodeKey, StationCode);
        await SetConfigAsync(context, "receiving_company", PlantName);

        // Báscula virtual y sin cámaras: la demostración no depende de hardware.
        var scales = await context.ScaleConfigs.ToListAsync();
        // La base ya no trae una báscula por defecto: la demostración crea la suya, simulada.
        if (scales.Count == 0)
        {
            var simulated = new ScaleConfig { Name = "Báscula 1", IsActive = true, SortOrder = 0 };
            context.ScaleConfigs.Add(simulated);
            scales.Add(simulated);
        }
        foreach (var scale in scales)
        {
            scale.Name = "Báscula 1";
            scale.ProtocolType = ProtocolType.Simulator;
            scale.PortName = "";
            scale.ReadIntervalMs = 200;
        }
        foreach (var camera in await context.CameraConfigs.ToListAsync())
            camera.IsActive = false;
    }

    private static async Task SetConfigAsync(AppDbContext context, string key, string value)
    {
        var config = await context.SystemConfigs.FirstOrDefaultAsync(c => c.Key == key);
        if (config is null) context.SystemConfigs.Add(new SystemConfig { Key = key, Value = value });
        else config.Value = value;
    }

    private static async Task<List<DemoCompany>> SeedCatalogsAsync(AppDbContext context)
    {
        // La base ya no trae productos por defecto: la demostración crea todos los suyos.
        foreach (var name in new[] { "Áridos", "Cemento", "Hierro", "Madera", "Combustible", "Granos", "Cal", "Fertilizantes", "Concentrado de cobre" })
            if (!await context.Products.AnyAsync(p => p.Name == name))
                context.Products.Add(new Product { Name = name, IsActive = true });

        // Las empresas de ejemplo originales pasan a tener nombres que se ven reales en pantalla.
        var renames = new Dictionary<string, (string Name, int Rut, string Address)>
        {
            ["Empresa Demo Ltda."] = ("Constructora Pehuén SA", 76_218_340, "Av. Américo Vespucio 1850, Pudahuel"),
            ["Transportes del Sur SA"] = ("Transportes Los Boldos Ltda.", 77_412_905, "Ruta 5 Sur Km 62, Buin"),
        };
        foreach (var (oldName, (name, rut, address)) in renames)
        {
            var company = await context.Companies.FirstOrDefaultAsync(c => c.Name == oldName);
            if (company is null) continue;
            company.Name = name;
            company.RUT = FormatRut(rut);
            company.Address = address;
        }

        // Las dos primeras ya no vienen de la base inicial, así que la demo las crea si no existen.
        var extra = new (string Name, int Rut, string Address, string Phone)[]
        {
            ("Constructora Pehuén SA", 76_218_340, "Av. Américo Vespucio 1850, Pudahuel", "+56 9 1234 5678"),
            ("Transportes Los Boldos Ltda.", 77_412_905, "Ruta 5 Sur Km 62, Buin", "+56 9 9876 5432"),
            ("Áridos Maipo SpA", 76_903_118, "Camino a Melipilla 4520, Maipú", "+56 9 8412 3376"),
            ("Agrícola Valle Central SpA", 77_068_552, "Ruta H-30 Km 8, Rancagua", "+56 9 7355 0914"),
            ("Fletes Cordillera Ltda.", 76_540_287, "Av. Los Libertadores 910, Los Andes", "+56 9 6120 4478"),
            ("Fertilizantes del Sur SpA", 77_231_064, "Camino Longitudinal Km 190, Talca", "+56 9 9047 2285"),
        };
        foreach (var (name, rut, address, phone) in extra)
            if (!await context.Companies.AnyAsync(c => c.Name == name))
                context.Companies.Add(new Company { Name = name, RUT = FormatRut(rut), Address = address, Phone = phone, IsActive = true });
        await context.SaveChangesAsync();

        var drivers = new (string Name, string Phone, string Company)[]
        {
            ("Juan Pérez", "+56 9 6612 4031", "Constructora Pehuén SA"),
            ("Pedro López", "+56 9 7703 1186", "Constructora Pehuén SA"),
            ("Luis Martínez", "+56 9 8821 5540", "Transportes Los Boldos Ltda."),
            ("Diego Rojas", "+56 9 9930 7712", "Transportes Los Boldos Ltda."),
            ("Sergio Castro", "+56 9 5518 2209", "Transportes Los Boldos Ltda."),
            ("Marcelo Fuentes", "+56 9 6674 3350", "Áridos Maipo SpA"),
            ("Héctor Valdés", "+56 9 7141 9068", "Áridos Maipo SpA"),
            ("Rodrigo Tapia", "+56 9 8260 4417", "Agrícola Valle Central SpA"),
            ("Cristián Araya", "+56 9 9382 6025", "Agrícola Valle Central SpA"),
            ("Patricio Núñez", "+56 9 6405 1873", "Fletes Cordillera Ltda."),
            ("Jorge Vergara", "+56 9 7519 3342", "Fletes Cordillera Ltda."),
            ("Mauricio Espinoza", "+56 9 8803 7596", "Fertilizantes del Sur SpA"),
        };
        var companies = await context.Companies.ToListAsync();
        foreach (var (name, phone, companyName) in drivers)
        {
            var company = companies.First(c => c.Name == companyName);
            if (!await context.Drivers.AnyAsync(d => d.FullName == name))
                context.Drivers.Add(new Driver { FullName = name, Phone = phone, CompanyId = company.Id, IsActive = true });
        }
        await context.SaveChangesAsync();

        // Cada empresa tiene una flota fija: la misma patente vuelve con la misma tara, como en la realidad.
        var products = await context.Products.ToListAsync();
        var profiles = new Dictionary<string, string[]>
        {
            ["Constructora Pehuén SA"] = ["Cemento", "Hierro", "Áridos"],
            ["Transportes Los Boldos Ltda."] = ["Áridos", "Granos", "Madera"],
            ["Áridos Maipo SpA"] = ["Áridos", "Cal"],
            ["Agrícola Valle Central SpA"] = ["Granos", "Fertilizantes"],
            ["Fletes Cordillera Ltda."] = ["Concentrado de cobre", "Combustible"],
            ["Fertilizantes del Sur SpA"] = ["Fertilizantes", "Cal"],
        };
        var result = new List<DemoCompany>();
        foreach (var company in companies.Where(c => profiles.ContainsKey(c.Name)))
        {
            var companyDrivers = await context.Drivers.Where(d => d.CompanyId == company.Id).ToListAsync();
            result.Add(new DemoCompany(company,
                profiles[company.Name].Select(n => products.First(p => p.Name == n)).ToList(),
                companyDrivers.Select(d => d.FullName).ToList()));
        }
        return result;
    }

    private static async Task SeedWeighingsAsync(AppDbContext context, Random random, List<DemoCompany> companies)
    {
        if (await context.WeighingRecords.AnyAsync()) return;

        // Patentes chilenas nuevas: cuatro consonantes y dos dígitos (sin vocales ni M, N, Ñ, Q).
        const string letters = "BCDFGHJKLPRSTVWXYZ";
        string NewPlate() => PlateFormatter.Format(
            string.Concat(Enumerable.Range(0, 4).Select(_ => letters[random.Next(letters.Length)])) + random.Next(10, 100),
            "CL");

        var fleets = companies.ToDictionary(c => c.Company.Id, c =>
            Enumerable.Range(0, 4).Select(_ => (Plate: NewPlate(), Tare: (decimal)Math.Round(random.Next(12800, 17600) / 10.0) * 10m)).ToList());

        var operators = new[] { "Carlos Muñoz", "María González" };
        var now = DateTime.Now;
        var drafts = new List<WeighingRecord>();

        WeighingRecord Build(DateTime entry, bool completed)
        {
            var company = companies[random.Next(companies.Count)];
            var truck = fleets[company.Company.Id][random.Next(4)];
            var product = company.Products[random.Next(company.Products.Count)];
            var (min, max) = NetRange(product.Name);
            var net = Math.Round(random.Next(min, max) / 10m) * 10m;
            var tare = truck.Tare + random.Next(-40, 41);
            var record = new WeighingRecord
            {
                WeighInTime = entry,
                GrossWeight = tare + net,
                LicensePlate = truck.Plate,
                PlateCountry = "CL",
                OperatorName = entry.Hour < 13 ? operators[0] : operators[1],
                DriverName = company.Drivers[random.Next(company.Drivers.Count)],
                ProductId = product.Id,
                CompanyId = company.Company.Id,
                EntryScaleName = "Báscula 1",
                Status = WeighingStatus.WeighingIn,
                Notes = random.Next(6) == 0 ? $"Guía de despacho N° {random.Next(41000, 49999)}" : ""
            };
            if (completed)
            {
                record.WeighOutTime = entry.AddMinutes(random.Next(9, 36));
                record.TareWeight = tare;
                record.NetWeight = Math.Abs(record.GrossWeight - tare);
                record.ExitScaleName = "Báscula 1";
                record.Status = WeighingStatus.Completed;
            }
            return record;
        }

        for (var daysAgo = 30; daysAgo >= 0; daysAgo--)
        {
            var date = now.Date.AddDays(-daysAgo);
            if (date.DayOfWeek == DayOfWeek.Sunday) continue;
            var count = date.DayOfWeek == DayOfWeek.Saturday ? random.Next(1, 4) : random.Next(3, 8);
            for (var i = 0; i < count; i++)
            {
                var entry = date.AddHours(7.5).AddMinutes(random.Next(0, 10 * 60));
                // Hoy solo hay pesajes ya terminados antes de la hora actual.
                if (entry > now.AddMinutes(-45)) continue;
                drafts.Add(Build(entry, completed: true));
            }
        }

        // Unos pocos tickets anulados, repartidos en el período.
        foreach (var record in drafts.Where(d => d.WeighInTime < now.AddDays(-1)).OrderBy(_ => random.Next()).Take(3))
        {
            record.Status = WeighingStatus.Cancelled;
            record.WeighOutTime = null;
            record.TareWeight = 0;
            record.NetWeight = 0;
            record.ExitScaleName = null;
            record.Notes = "Anulado: camión rechazado en el control de acceso";
        }

        // Dos camiones dentro de la planta, esperando la pesada de salida.
        drafts.Add(Build(now.AddMinutes(-26), completed: false));
        drafts.Add(Build(now.AddMinutes(-11), completed: false));

        // Numeración por mes en orden de ingreso, igual que la que asigna la app, y contadores al día.
        var counters = new Dictionary<string, int>();
        foreach (var record in drafts.OrderBy(d => d.WeighInTime))
        {
            var prefix = $"{StationCode}-{record.WeighInTime.Year}-{record.WeighInTime.Month:D2}-";
            counters[prefix] = counters.GetValueOrDefault(prefix) + 1;
            record.StationCode = StationCode;
            record.TicketNumber = $"{prefix}{counters[prefix]:D4}";
        }
        context.WeighingRecords.AddRange(drafts.OrderBy(d => d.WeighInTime));
        foreach (var (prefix, last) in counters)
        {
            var counter = await context.TicketCounters.FirstOrDefaultAsync(c => c.Prefix == prefix);
            if (counter is null) context.TicketCounters.Add(new TicketCounter { Prefix = prefix, LastNumber = last });
            else counter.LastNumber = Math.Max(counter.LastNumber, last);
        }
    }

    // Peso neto típico de un camión de 3 o 4 ejes según la carga, en kilos.
    private static (int Min, int Max) NetRange(string product) => product switch
    {
        "Cemento" => (24000, 28500),
        "Hierro" => (17000, 26000),
        "Madera" => (20000, 28000),
        "Combustible" => (21000, 30000),
        "Granos" => (24000, 31000),
        "Concentrado de cobre" => (27000, 33000),
        "Cal" or "Fertilizantes" => (22000, 28000),
        _ => (21000, 30000)
    };

    // Dígito verificador chileno (módulo 11) y formato 12.345.678-5.
    private static string FormatRut(int body)
    {
        var sum = 0;
        var factor = 2;
        for (var n = body; n > 0; n /= 10)
        {
            sum += n % 10 * factor;
            factor = factor == 7 ? 2 : factor + 1;
        }
        var dv = 11 - sum % 11;
        var check = dv == 11 ? "0" : dv == 10 ? "K" : dv.ToString();
        return body.ToString("N0", System.Globalization.CultureInfo.InvariantCulture).Replace(",", ".") + "-" + check;
    }

    private sealed record DemoCompany(Company Company, List<Product> Products, List<string> Drivers);
}
