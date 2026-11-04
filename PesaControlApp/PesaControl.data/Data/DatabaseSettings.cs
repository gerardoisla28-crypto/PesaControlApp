namespace PesaControl.Data.Data;

public static class DatabaseSettings
{
    public const string PathEnvironmentVariable = "PESACONTROL_DB_PATH";
    public const string PhotosEnvironmentVariable = "PESACONTROL_PHOTOS_PATH";

    // El almacén local del puesto vive fuera de la carpeta del ejecutable, para que sobreviva a las actualizaciones.
    public static string GetDatabasePath()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(PathEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
            return fromEnvironment;

        return Path.Combine(AppDataDirectory(), "pesacontrol.db");
    }

    public static string GetPhotosDirectory()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(PhotosEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
            return fromEnvironment;

        return Path.Combine(AppDataDirectory(), "Photos");
    }

    private static string AppDataDirectory()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PesaControl");
}
