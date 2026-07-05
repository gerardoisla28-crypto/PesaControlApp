namespace PesaControl.Core.Models;

public struct WeightReading
{
    public decimal Value { get; init; }
    public bool IsStable { get; init; }
    public DateTime Timestamp { get; init; }
    public string RawData { get; init; }
}
