using Microsoft.EntityFrameworkCore.Design;

namespace PesaControl.Data.Data;

// Solo para las herramientas de EF (dotnet ef); no se conectan al generar migraciones.
public class LocalDbContextFactory : IDesignTimeDbContextFactory<LocalDbContext>
{
    public LocalDbContext CreateDbContext(string[] args) => new("design-time.db");
}

public class CentralDbContextFactory : IDesignTimeDbContextFactory<CentralDbContext>
{
    public CentralDbContext CreateDbContext(string[] args)
        => new(Environment.GetEnvironmentVariable("PESACONTROL_CENTRAL_DB") ?? "Host=localhost;Database=pesacontrol_design");
}
