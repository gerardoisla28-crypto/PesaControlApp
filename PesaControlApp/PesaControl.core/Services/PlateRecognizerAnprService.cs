using System.Net.Http.Headers;
using System.Text.Json;
using PesaControl.Core.Models;

namespace PesaControl.Core.Services;

public class PlateRecognizerAnprService : IAnprService, IDisposable
{
    private readonly HttpClient _http;
    private readonly string _apiKey;

    public PlateRecognizerAnprService(string apiKey)
    {
        _apiKey = apiKey;
        _http = new HttpClient
        {
            BaseAddress = new Uri("https://api.platerecognizer.com/")
        };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Token", _apiKey);
    }

    public string Name => "PlateRecognizer Cloud";
    public bool IsAvailable => !string.IsNullOrWhiteSpace(_apiKey);

    public async Task<PlateRecognitionResult> RecognizeAsync(byte[] imageData)
    {
        try
        {
            using var content = new MultipartFormDataContent();
            content.Add(new ByteArrayContent(imageData), "upload", "plate.jpg");

            var response = await _http.PostAsync("v1/plate-reader/", content);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var results = doc.RootElement.GetProperty("results");

            if (results.GetArrayLength() == 0)
                return new PlateRecognitionResult { IsSuccessful = false, ErrorMessage = "No se detectó patente" };

            var first = results[0];
            var plate = first.GetProperty("plate").GetString() ?? "";
            var confidence = first.GetProperty("confidence").GetDouble();

            string region = "";
            if (first.TryGetProperty("region", out var regionProp) && regionProp.ValueKind == JsonValueKind.Object)
                region = regionProp.GetProperty("code").GetString() ?? "";

            return new PlateRecognitionResult
            {
                Plate = plate,
                Confidence = confidence,
                Region = region,
                IsSuccessful = confidence >= 0.5
            };
        }
        catch (Exception ex)
        {
            return new PlateRecognitionResult
            {
                IsSuccessful = false,
                ErrorMessage = $"Error ANPR: {ex.Message}"
            };
        }
    }

    public void Dispose() => _http.Dispose();
}
