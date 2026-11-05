using Microsoft.EntityFrameworkCore;
using PesaControl.Core.Models;

namespace PesaControl.Data.Data;

// Almacén local de cada puesto: SQLite embebido, funciona sin servidor ni conexión.
public class LocalDbContext : AppDbContext
{
    public LocalDbContext(string databasePath)
        : base(new DbContextOptionsBuilder<LocalDbContext>().UseSqlite($"Data Source={databasePath}").Options)
    {
    }

    // Todo pesaje que se crea o se guarda en el puesto queda con nueva fecha de modificación y pendiente de
    // sincronizar, sin importar desde qué pantalla o servicio se guardó. La sincronización marca los pesajes
    // como sincronizados con ExecuteUpdate, que no pasa por aquí.
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        MarkWeighingsPending();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        MarkWeighingsPending();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void MarkWeighingsPending()
    {
        var now = DateTime.Now;
        foreach (var entry in ChangeTracker.Entries<WeighingRecord>()
                     .Where(e => e.State is EntityState.Added or EntityState.Modified))
        {
            entry.Entity.ModifiedAt = now;
            entry.Entity.SyncStatus = SyncStatus.Pending;
            entry.Entity.SyncedAt = null;
        }
    }
}
