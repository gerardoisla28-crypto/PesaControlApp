using Microsoft.EntityFrameworkCore;

namespace PesaControl.Data.Data;

// Almacén local de cada puesto: SQLite embebido, funciona sin servidor ni conexión.
public class LocalDbContext : AppDbContext
{
    public LocalDbContext(string databasePath)
        : base(new DbContextOptionsBuilder<LocalDbContext>().UseSqlite($"Data Source={databasePath}").Options)
    {
    }
}
