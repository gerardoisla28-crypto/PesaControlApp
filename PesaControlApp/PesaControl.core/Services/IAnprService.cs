using PesaControl.Core.Models;

namespace PesaControl.Core.Services;

public interface IAnprService
{
    string Name { get; }
    bool IsAvailable { get; }
    Task<PlateRecognitionResult> RecognizeAsync(byte[] imageData);
}
