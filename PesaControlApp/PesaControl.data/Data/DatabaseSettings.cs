using System.Text.Json;

namespace PesaControl.Data.Data;

public static class DatabaseSettings
{
    public const string EnvironmentVariable = "PESACONTROL_DB";
    public const string FileName = "dbsettings.json";

    public static string GetConnectionString()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
            return fromEnvironment;

        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FileName);
        if (File.Exists(path))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.TryGetProperty("ConnectionString", out var value)
                && !string.IsNullOrWhiteSpace(value.GetString()))
                return value.GetString()!;
        }

        throw new InvalidOperationException(
            $"No hay cadena de conexión a la base de datos. Defina la variable de entorno {EnvironmentVariable} " +
            $"o cree {FileName} junto al ejecutable con la propiedad \"ConnectionString\".");
    }
}
