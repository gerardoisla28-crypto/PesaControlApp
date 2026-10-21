using System.Text.RegularExpressions;

namespace PesaControl.Core.Security;

public static partial class StationCodePolicy
{
    public const int MinLength = 1;
    public const int MaxLength = 4;
    public const string Example = "Santiago = STGO, Región Metropolitana = RM";

    [GeneratedRegex("^[A-Z0-9]{1,4}$")]
    private static partial Regex Allowed();

    // El código va en mayúsculas dentro del número de ticket (STGO-2026-10-0001).
    public static string Normalize(string code) => code.Trim().ToUpperInvariant();

    public static bool IsValid(string code) => Allowed().IsMatch(code);

    public static string Describe()
        => $"El código debe tener entre {MinLength} y {MaxLength} caracteres: letras y números, sin espacios.";
}
