using System.Net.Http.Headers;
using System.Net.Http.Json;
using RestaurantApi.DTOs.Responses;

namespace RestaurantApi.Services;

public class MenuExtractionClient
{
    private readonly HttpClient _http;

    private static readonly System.Text.Json.JsonSerializerOptions SnakeCaseJson = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower,
    };

    public MenuExtractionClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<MenuScanResponse> ExtractAsync(
        IReadOnlyList<(string FileName, string ContentType, byte[] Bytes)> photos,
        CancellationToken cancellationToken)
    {
        using var form = new MultipartFormDataContent();
        foreach (var p in photos)
        {
            var content = new ByteArrayContent(p.Bytes);
            content.Headers.ContentType = new MediaTypeHeaderValue(p.ContentType);
            form.Add(content, "photos", p.FileName);
        }

        using var response = await _http.PostAsync("/menu/extract", form, cancellationToken);
        response.EnsureSuccessStatusCode();

        var parsed = await response.Content.ReadFromJsonAsync<PythonExtractResponse>(
            SnakeCaseJson, cancellationToken: cancellationToken);
        if (parsed is null) throw new InvalidOperationException("Empty response from /menu/extract.");

        var items = parsed.Items
            .Select(i => new ExtractedMenuItemResponse(
                i.Name, (decimal)i.Price, i.SourcePage, i.SourceIndex,
                i.Description, i.CategoryHint, i.Confidence))
            .ToList();
        var failed = parsed.FailedPages
            .Select(f => new FailedPageResponse(f.SourcePage, f.Reason))
            .ToList();

        return new MenuScanResponse(items, failed, parsed.Model, parsed.SchemaVersion);
    }

    // Internal mirror of Python's snake_case shape — deserialized with SnakeCaseJson options.
    private record PythonExtractResponse
    {
        public List<PythonItem> Items { get; init; } = new();
        public List<PythonFailedPage> FailedPages { get; init; } = new();
        public string Model { get; init; } = "";
        public int SchemaVersion { get; init; }
    }

    private record PythonItem
    {
        public string Name { get; init; } = "";
        public double Price { get; init; }
        public int SourcePage { get; init; }
        public int SourceIndex { get; init; }
        public string? Description { get; init; }
        public string? CategoryHint { get; init; }
        public double? Confidence { get; init; }
    }

    private record PythonFailedPage
    {
        public int SourcePage { get; init; }
        public string Reason { get; init; } = "";
    }
}
