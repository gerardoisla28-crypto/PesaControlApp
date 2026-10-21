using PesaControl.Core.Models;

namespace PesaControl.Core.Security;

public static class PasswordPolicy
{
    public const int MaxLength = 128;
    private const int PrivilegedMinLength = 14;
    private const int StandardMinLength = 8;

    public static int MinLength(string role)
        => role is UserRoles.SuperAdmin or UserRoles.Admin ? PrivilegedMinLength : StandardMinLength;

    public static bool IsValid(string role, string password)
        => password.Length >= MinLength(role) && password.Length <= MaxLength;

    public static string Describe(string role)
        => $"La contraseña debe tener entre {MinLength(role)} y {MaxLength} caracteres.";
}
