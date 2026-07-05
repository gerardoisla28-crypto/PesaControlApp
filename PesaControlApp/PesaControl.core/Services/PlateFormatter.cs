using System.Text.RegularExpressions;

namespace PesaControl.Core.Services;

public static class PlateFormatter
{
    public static string Format(string plate, string country)
    {
        if (string.IsNullOrWhiteSpace(plate))
            return plate;

        var raw = Regex.Replace(plate, "[^A-Za-z0-9]", "").ToUpper();

        if (string.IsNullOrEmpty(raw))
            return raw;

        return country switch
        {
            "CL" => FormatChile(raw),
            "AR" => FormatArgentina(raw),
            "BR" => FormatBrasil(raw),
            "PY" => FormatParaguay(raw),
            "UY" => FormatUruguay(raw),
            "BO" => FormatBolivia(raw),
            "PE" => FormatPeru(raw),
            "CO" => FormatColombia(raw),
            "MX" => FormatMexico(raw),
            "EC" => FormatEcuador(raw),
            _ => raw
        };
    }

    private static string FormatChile(string raw)
    {
        if (raw.Length == 6)
        {
            if (char.IsLetter(raw[0]) && char.IsLetter(raw[1]) &&
                char.IsDigit(raw[2]) && char.IsDigit(raw[3]) &&
                char.IsDigit(raw[4]) && char.IsDigit(raw[5]))
                return $"{raw[..2]}-{raw[2..]}";

            if (char.IsLetter(raw[0]) && char.IsLetter(raw[1]) &&
                char.IsLetter(raw[2]) && char.IsLetter(raw[3]) &&
                char.IsDigit(raw[4]) && char.IsDigit(raw[5]))
                return $"{raw[..4]}-{raw[4..]}";

            if (char.IsLetter(raw[0]) && char.IsLetter(raw[1]) &&
                char.IsLetter(raw[2]) && char.IsLetter(raw[3]) &&
                char.IsLetter(raw[4]) && char.IsDigit(raw[5]))
                return $"{raw[..5]}-{raw[5..]}";
        }

        return raw;
    }

    private static string FormatArgentina(string raw)
    {
        if (raw.Length == 6 &&
            raw.All(c => char.IsLetter(c) || char.IsDigit(c)) &&
            raw.Take(3).All(char.IsLetter) &&
            raw.Skip(3).All(char.IsDigit))
            return $"{raw[..3]}-{raw[3..]}";

        if (raw.Length == 7 &&
            raw.Take(2).All(char.IsLetter) &&
            raw.Skip(2).Take(3).All(char.IsDigit) &&
            raw.Skip(5).All(char.IsLetter))
            return $"{raw[..2]}-{raw.Substring(2, 3)}-{raw[5..]}";

        return raw;
    }

    private static string FormatBrasil(string raw)
    {
        if (raw.Length == 7)
        {
            if (raw.Take(3).All(char.IsLetter) &&
                raw.Skip(3).All(char.IsDigit))
                return $"{raw[..3]}-{raw[3..]}";

            if (raw.Take(3).All(char.IsLetter) &&
                char.IsDigit(raw[3]) &&
                char.IsLetter(raw[4]) &&
                char.IsDigit(raw[5]) && char.IsDigit(raw[6]))
                return $"{raw[..3]}-{raw[3..]}";
        }

        return raw;
    }

    private static string FormatParaguay(string raw)
    {
        if (raw.Length == 6 &&
            raw.Take(3).All(char.IsLetter) &&
            raw.Skip(3).All(char.IsDigit))
            return $"{raw[..3]}-{raw[3..]}";

        if (raw.Length == 7 &&
            raw.Take(4).All(char.IsLetter) &&
            raw.Skip(4).All(char.IsDigit))
            return $"{raw[..4]}-{raw[4..]}";

        return raw;
    }

    private static string FormatUruguay(string raw)
    {
        if (raw.Length == 6 &&
            raw.Take(3).All(char.IsLetter) &&
            raw.Skip(3).All(char.IsDigit))
            return $"{raw[..3]}-{raw[3..]}";

        if (raw.Length == 7 &&
            raw.Take(3).All(char.IsLetter) &&
            raw.Skip(3).All(char.IsDigit))
            return $"{raw[..3]}-{raw[3..]}";

        return raw;
    }

    private static string FormatBolivia(string raw)
    {
        if (raw.Length == 7 &&
            raw.Take(2).All(char.IsLetter) &&
            raw.Skip(2).All(char.IsDigit))
            return $"{raw[..2]}-{raw[2..]}";

        return raw;
    }

    private static string FormatPeru(string raw)
    {
        if (raw.Length == 6)
        {
            if (raw.Take(3).All(char.IsLetter) &&
                raw.Skip(3).All(char.IsDigit))
                return $"{raw[..3]}-{raw[3..]}";

            if (raw.Take(2).All(char.IsLetter) &&
                raw.Skip(2).All(char.IsDigit))
                return $"{raw[..2]}-{raw[2..]}";
        }

        return raw;
    }

    private static string FormatColombia(string raw)
    {
        if (raw.Length == 6 &&
            raw.Take(3).All(char.IsLetter) &&
            raw.Skip(3).All(char.IsDigit))
            return $"{raw[..3]}-{raw[3..]}";

        return raw;
    }

    private static string FormatMexico(string raw)
    {
        if (raw.Length == 7 &&
            raw.Take(3).All(char.IsLetter) &&
            raw.Skip(3).All(char.IsDigit))
            return $"{raw[..3]}-{raw[3..]}";

        if (raw.Length == 6 &&
            raw.Take(3).All(char.IsLetter) &&
            raw.Skip(3).All(char.IsDigit))
            return $"{raw[..3]}-{raw[3..]}";

        if (raw.Length == 7 &&
            raw.Take(2).All(char.IsLetter) &&
            raw.Skip(2).All(char.IsDigit))
            return $"{raw[..2]}-{raw[2..]}";

        return raw;
    }

    private static string FormatEcuador(string raw)
    {
        if (raw.Length == 7 &&
            raw.Take(3).All(char.IsLetter) &&
            raw.Skip(3).All(char.IsDigit))
            return $"{raw[..3]}-{raw[3..]}";

        if (raw.Length == 6 &&
            raw.Take(3).All(char.IsLetter) &&
            raw.Skip(3).All(char.IsDigit))
            return $"{raw[..3]}-{raw[3..]}";

        return raw;
    }
}
