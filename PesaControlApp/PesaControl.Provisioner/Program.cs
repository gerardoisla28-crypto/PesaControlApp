using PesaControl.Data.Data;

Console.WriteLine("PesaControl - Provisionamiento de SuperAdmin");
Console.Write("Ruta de la base de datos local (Enter para la predeterminada): ");
var databasePath = Console.ReadLine();
Console.Write("Usuario de superadmin: ");
var username = Console.ReadLine()?.Trim() ?? "";
Console.Write("Contraseña de superadmin: ");
var password = ReadPassword();
Console.WriteLine();

var database = string.IsNullOrWhiteSpace(databasePath) ? new DatabaseService() : new DatabaseService(databasePath);
try
{
    await database.InitializeAsync();
    await database.ProvisionSuperAdminAsync(username, password);
    Console.WriteLine("Superadmin creado. Guarde la contraseña en un gestor de contraseñas seguro.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 1;
}

static string ReadPassword()
{
    var value = new System.Text.StringBuilder();
    for (ConsoleKeyInfo key; (key = Console.ReadKey(intercept: true)).Key != ConsoleKey.Enter;)
    {
        if (key.Key == ConsoleKey.Backspace && value.Length > 0) value.Length--;
        else if (!char.IsControl(key.KeyChar)) value.Append(key.KeyChar);
    }
    return value.ToString();
}
