namespace PesaControl.Core.Models;

public static class UserRoles
{
    public const string SuperAdmin = "SuperAdmin";
    public const string Admin = "Admin";
    public const string Operator = "Operator";
    public const string Viewer = "Viewer";

    public static bool CanManageUsers(string role) => role is SuperAdmin or Admin;

    // Exportar e importar pesajes entre puestos: quien lleva el pendrive en un puesto sin servidor.
    public static bool CanExchangeData(string role) => role is SuperAdmin or Admin or Operator;
    public static bool CanManageRole(string actorRole, string targetRole) =>
        actorRole == SuperAdmin || (actorRole == Admin && targetRole is Operator or Viewer);
}
