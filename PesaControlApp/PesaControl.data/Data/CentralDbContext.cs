using Microsoft.EntityFrameworkCore;

namespace PesaControl.Data.Data;

// Servidor central: PostgreSQL, donde se consolidan los datos de los puestos cuando hay conexión.
public class CentralDbContext : AppDbContext
{
    public CentralDbContext(string connectionString)
        : base(new DbContextOptionsBuilder<CentralDbContext>().UseNpgsql(connectionString).Options)
    {
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
        configurationBuilder.Properties<DateTime>().HaveColumnType("timestamp without time zone");
        configurationBuilder.Properties<DateTime?>().HaveColumnType("timestamp without time zone");
    }
}
