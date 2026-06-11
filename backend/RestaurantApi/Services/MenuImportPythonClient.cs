using System.Net.Http.Json;
using System.Text.Json;

namespace RestaurantApi.Services;

/// <summary>Abstraction used by MenuImportBackgroundService to call the Python extraction endpoint.</summary>
public interface IPdfExtractor
{
    Task<JsonElement> ExtractPdfAsync(PdfJobPayload payload, CancellationToken ct);
}

public class MenuImportPythonClient : IPdfExtractor
{
    private readonly HttpClient _http;

    public MenuImportPythonClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<JsonElement> ExtractPdfAsync(PdfJobPayload payload, CancellationToken ct)
    {
        var snakeCaseJson = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        };
        var resp = await _http.PostAsJsonAsync("/menu/extract-pdf", payload, snakeCaseJson, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<JsonElement>(snakeCaseJson, ct);
    }
}

public sealed record PdfJobPayload(
    Guid JobId,
    Guid RestaurantId,
    string StoragePath,
    string ProgressCallbackUrl,
    string HmacSecret,
    string Provider = "gemini");
