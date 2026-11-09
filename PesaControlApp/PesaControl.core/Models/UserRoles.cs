namespace PesaControl.Core.Models;

public static class UserRoles
{
    public const string SuperAdmin = "SuperAdmin";
    public const string Admin = "Admin";
    public const string Operator = "Operator";

    // Nombre que se muestra en pantalla. El valor guardado en la base no cambia ("Operator"), así los usuarios existentes siguen válidos.
    public static string DisplayName(string? role) => role switch
    {
        Operator => "Operador",
        _ => role ?? ""
    };

    public static bool CanManageUsers(string role) => role is SuperAdmin or Admin;

    // Exportar e importar pesajes entre puestos: quien lleva el pendrive en un puesto sin servidor.
    public static bool CanExchangeData(string role) => role is SuperAdmin or Admin or Operator;
    public static bool CanManageRole(string actorRole, string targetRole) =>
        actorRole == SuperAdmin || (actorRole == Admin && targetRole == Operator);
}
