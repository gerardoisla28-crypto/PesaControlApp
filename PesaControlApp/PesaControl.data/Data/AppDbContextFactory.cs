using Microsoft.EntityFrameworkCore.Design;

namespace PesaControl.Data.Data;

// Solo para las herramientas de EF (dotnet ef); no se conecta al generar migraciones.
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
        => new(Environment.GetEnvironmentVariable(DatabaseSettings.EnvironmentVariable)
               ?? "Host=localhost;Database=pesacontrol_design");
}
