namespace PesaControl.Core.Models;

/// <summary>
/// Formatea pesos igual que el visor de la báscula: miles con separador de la cultura (36.000) y
/// exactamente los decimales que el visor muestra (0 si no muestra).
/// </summary>
public static class WeightFormat
{
    public const int MaxDecimals = 3;

    public static string Format(decimal value, int decimals)
    {
        decimals = Math.Clamp(decimals, 0, MaxDecimals);
        return Math.Round(value, decimals, MidpointRounding.AwayFromZero).ToString("N" + decimals);
    }

    public static string FormatKg(decimal value, int decimals) => Format(value, decimals) + " kg";
}
