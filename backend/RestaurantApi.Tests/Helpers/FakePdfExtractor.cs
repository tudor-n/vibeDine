using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RestaurantApi.Services;

namespace RestaurantApi.Tests.Helpers;

/// <summary>
/// Deterministic IPdfExtractor test double.
/// Returns a canned MenuExtractionResponse JSON keyed by (RestaurantId, JobId).
/// Optionally delays to allow cancellation races.
/// </summary>
public sealed class FakePdfExtractor : IPdfExtractor
{
    private readonly TimeSpan _delay;
    private readonly string? _cannedJson;
    private readonly List<PdfJobPayload> _calls = new();

    /// <summary>
    /// Default: returns a response with one item and no failed pages.
    /// </summary>
    public FakePdfExtractor(TimeSpan delay = default, string? cannedJson = null)
    {
        _delay = delay;
        _cannedJson = cannedJson;
    }

    /// <summary>
    /// Optional canonical hierarchy payload to include in the canned response under
    /// the "hierarchy" key. When null (default), the key is omitted — preserving
    /// backwards compatibility for tests written before Task 12.
    /// </summary>
    public object? Hierarchy { get; set; }

    /// <summary>All calls received by this extractor (for assertion).</summary>
    public IReadOnlyList<PdfJobPayload> Calls => _calls;

    /// <summary>
    /// Optional item_photos payload included in the canned response — mirrors the
    /// Python extractor's sibling array of base64 dish crops. When null (default)
    /// the key is omitted.
    /// </summary>
    public object? ItemPhotos { get; set; }

    public async Task<JsonElement> ExtractPdfAsync(PdfJobPayload payload, CancellationToken ct)
    {
        lock (_calls) _calls.Add(payload);

        if (_delay > TimeSpan.Zero)
            await Task.Delay(_delay, ct);

        var json = _cannedJson ?? BuildDefaultCannedJson();
        using var doc = JsonDocument.Parse(json);
        // Must return a clone outside the document's lifetime.
        return doc.RootElement.Clone();
    }

    private string BuildDefaultCannedJson()
    {
        // We build a dictionary so we can conditionally include "hierarchy"
        // — JsonSerializer.Serialize on an anonymous object writes every property,
        // including nulls, which the C# side would surface as a `null` hierarchy.
        var payload = new Dictionary<string, object?>
        {
            ["items"] = new[]
            {
                new
                {
                    name = "Test Item",
                    price = 12.50,
                    source_page = 0,
                    source_index = 0,
                    category_hint = "TEST",
                }
            },
            ["failed_pages"] = Array.Empty<object>(),
            ["model"] = "fake-extractor",
            ["schema_version"] = 2,
        };

        if (Hierarchy is not null)
            payload["hierarchy"] = Hierarchy;

        if (ItemPhotos is not null)
            payload["item_photos"] = ItemPhotos;

        return JsonSerializer.Serialize(payload);
    }
}
