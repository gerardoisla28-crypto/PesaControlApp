namespace PesaControl.Data.Data;

public static class DatabaseSettings
{
    public const string PathEnvironmentVariable = "PESACONTROL_DB_PATH";

    // El almacén local del puesto vive fuera de la carpeta del ejecutable, para que sobreviva a las actualizaciones.
    public static string GetDatabasePath()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(PathEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
            return fromEnvironment;

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PesaControl", "pesacontrol.db");
    }
}
