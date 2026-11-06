using System.Text.Json;
using System.Text.Json.Serialization;

namespace PesaControl.Core.Models;

public enum ModbusDataType
{
    Int16,
    UInt16,
    Int32,
    UInt32,
    Float32,
    /// <summary>32 bits: bit 31 = signo, resto = magnitud (formato Sensocar Kronos).</summary>
    SignMagnitude32
}

/// <summary>
/// Parámetros editables de los protocolos configurables (Modbus RTU/ASCII/TCP y ASCII serie/TCP).
/// Se guardan como JSON compacto (la columna de configuración admite 500 caracteres).
/// En Modbus la dirección de inicio va en ScaleConfig.RegisterAddress y el esclavo en ScaleConfig.SlaveId.
/// </summary>
public class ProtocolOptions
{
    public const string DefaultWeightPattern = @"[-+]?\s*\d+(?:[.,]\d+)?";

    /// <summary>peso = valor leído / Divisor (Modbus y ASCII). Ej.: 1000 si el equipo envía gramos o peso x1000.</summary>
    public decimal Divisor { get; set; } = 1m;

    // ── Modbus ────────────────────────────────────────────
    /// <summary>3 = registros mantenidos, 4 = registros de entrada.</summary>
    public int Function { get; set; } = 3;
    public ModbusDataType DataType { get; set; } = ModbusDataType.Int32;
    /// <summary>En 32 bits: el registro bajo viene primero.</summary>
    public bool LowWordFirst { get; set; }
    /// <summary>Intercambia los dos bytes de cada registro.</summary>
    public bool SwapBytes { get; set; }
    /// <summary>Dirección del registro de estado; -1 = no usar.</summary>
    public int StatusRegister { get; set; } = -1;
    public int StableBit { get; set; }
    public bool StableWhenSet { get; set; } = true;
    /// <summary>Dirección del registro de comando (FC 06) para tara/cero; -1 = no disponible.</summary>
    public int CommandRegister { get; set; } = -1;
    public int TareValue { get; set; } = 2;
    public int ZeroValue { get; set; } = 1;

    // ── ASCII ─────────────────────────────────────────────
    /// <summary>Fin de trama. Admite \r, \n y \t escritos como texto.</summary>
    public string? Terminator { get; set; } = "\\r";
    /// <summary>Comando para pedir el peso; vacío si el equipo transmite solo.</summary>
    public string? PollCommand { get; set; }
    /// <summary>Expresión regular que localiza el peso dentro de la trama.</summary>
    public string? WeightPattern { get; set; } = DefaultWeightPattern;
    /// <summary>Si la trama coincide con esto, el peso se considera inestable (p. ej. "US|MOT").</summary>
    public string? UnstablePattern { get; set; }
    public string? TareCommand { get; set; }
    public string? ZeroCommand { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = false
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static ProtocolOptions? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<ProtocolOptions>(json, JsonOptions); }
        catch (JsonException) { return null; }
    }

    public ProtocolOptions Clone() => FromJson(ToJson()) ?? new ProtocolOptions();

    /// <summary>Convierte "\r\n" escrito como texto en los caracteres reales.</summary>
    public static string DecodeEscapes(string? text)
        => string.IsNullOrEmpty(text) ? "" : text.Replace("\\r", "\r").Replace("\\n", "\n").Replace("\\t", "\t");
}
