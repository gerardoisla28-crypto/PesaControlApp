using System.Text.RegularExpressions;

namespace PesaControl.Core.Security;

public static partial class UsernamePolicy
{
    public const int MinLength = 3;
    public const int MaxLength = 50;

    [GeneratedRegex(@"^[A-Za-z0-9._-]{3,50}$")]
    private static partial Regex Allowed();

    public static bool IsValid(string username) => Allowed().IsMatch(username);

    public static string Describe()
        => $"El usuario debe tener entre {MinLength} y {MaxLength} caracteres: letras, números, punto, guion o guion bajo, sin espacios.";
}
