using System.Net.Http.Headers;
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
        // The Python service shares no filesystem with this API (separate hosts in
        // production), so the PDF is streamed from the locally-resolved StoragePath
        // as a multipart upload instead of sending the path across the wire.
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(payload.JobId.ToString()), "job_id");
        form.Add(new StringContent(payload.RestaurantId.ToString()), "restaurant_id");
        form.Add(new StringContent(payload.ProgressCallbackUrl), "progress_callback_url");
        form.Add(new StringContent(payload.HmacSecret), "hmac_secret");
        form.Add(new StringContent(payload.Provider), "provider");
        form.Add(new StringContent(payload.Locale), "locale");
        // Only append when opted in — keeps the off-path request byte-identical.
        if (payload.ExtractItemPhotos)
            form.Add(new StringContent("true"), "extract_item_photos");

        await using var pdfStream = File.OpenRead(payload.StoragePath);
        var fileContent = new StreamContent(pdfStream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(fileContent, "file", $"{payload.JobId:N}.pdf");

        using var resp = await _http.PostAsync("/menu/extract-pdf", form, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
    }
}

public sealed record PdfJobPayload(
    Guid JobId,
    Guid RestaurantId,
    string StoragePath,
    string ProgressCallbackUrl,
    string HmacSecret,
    string Provider = "gemini",
    string Locale = "ro-RO",
    bool ExtractItemPhotos = false);
