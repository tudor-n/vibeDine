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
    private readonly string _cannedJson;
    private readonly List<PdfJobPayload> _calls = new();

    /// <summary>
    /// Default: returns a response with one item and no failed pages.
    /// </summary>
    public FakePdfExtractor(TimeSpan delay = default, string? cannedJson = null)
    {
        _delay = delay;
        _cannedJson = cannedJson ?? JsonSerializer.Serialize(new
        {
            items = new[]
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
            failed_pages = Array.Empty<object>(),
            model = "fake-extractor",
            schema_version = 1,
        });
    }

    /// <summary>All calls received by this extractor (for assertion).</summary>
    public IReadOnlyList<PdfJobPayload> Calls => _calls;

    public async Task<JsonElement> ExtractPdfAsync(PdfJobPayload payload, CancellationToken ct)
    {
        lock (_calls) _calls.Add(payload);

        if (_delay > TimeSpan.Zero)
            await Task.Delay(_delay, ct);

        using var doc = JsonDocument.Parse(_cannedJson);
        // Must return a clone outside the document's lifetime.
        return doc.RootElement.Clone();
    }
}
