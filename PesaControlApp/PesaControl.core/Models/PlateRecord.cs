namespace PesaControl.Core.Models;

public class PlateRecord
{
    public int Id { get; set; }
    public string Plate { get; set; } = "";
    public string Region { get; set; } = "";
    public double Confidence { get; set; }
    public string Source { get; set; } = ""; // PlateRecognizer, PaddleOCR, Manual
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public int? CameraConfigId { get; set; }
    public int? WeighingRecordId { get; set; }

    public CameraConfig? Camera { get; set; }
    public WeighingRecord? WeighingRecord { get; set; }
}
