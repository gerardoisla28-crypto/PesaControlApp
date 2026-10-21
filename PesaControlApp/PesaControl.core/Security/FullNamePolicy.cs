namespace PesaControl.Core.Security;

public static class FullNamePolicy
{
    public const int MaxLength = 100;

    public static string Normalize(string fullName)
        => string.Join(' ', fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries));

    public static bool HasNameAndSurname(string normalizedName) => normalizedName.Split(' ').Length >= 2;

    public static bool IsTooLong(string fullName) => fullName.Length > MaxLength;

    public static bool HasOnlyValidChars(string fullName)
        => fullName.All(c => char.IsLetter(c) || c is ' ' or '\'' or '-' or '.');

    public static string DescribeInvalidChars() => "Solo se permiten letras, espacios, apóstrofes, guiones y puntos.";

    public static string DescribeTooLong() => $"El nombre no puede superar los {MaxLength} caracteres.";
}
