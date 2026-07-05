namespace PesaControl.Core.Models;

public class PlateRecognitionResult
{
    public string Plate { get; init; } = "";
    public double Confidence { get; init; }
    public string Region { get; init; } = "";
    public bool IsSuccessful { get; init; }
    public string? ErrorMessage { get; init; }
}
