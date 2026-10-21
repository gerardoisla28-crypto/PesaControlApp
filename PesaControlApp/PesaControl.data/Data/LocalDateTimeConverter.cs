using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace PesaControl.Data.Data;

// Npgsql no acepta DateTime en UTC en columnas "timestamp without time zone".
// Los valores en UTC se pasan a hora local al guardar, para que toda la base use la misma referencia.
public class LocalDateTimeConverter : ValueConverter<DateTime, DateTime>
{
    public LocalDateTimeConverter()
        : base(v => v.Kind == DateTimeKind.Utc ? v.ToLocalTime() : v, v => v)
    {
    }
}
