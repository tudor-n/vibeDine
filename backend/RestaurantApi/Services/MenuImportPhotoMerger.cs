using System.Text.Json;
using System.Text.Json.Nodes;

namespace RestaurantApi.Services;

/// <summary>
/// Post-processes a completed PDF-extraction result: persists each base64 dish
/// crop in <c>item_photos</c> to disk, stamps the resulting URL onto the matching
/// <c>items[]</c> entry as <c>image_url</c>, and always strips <c>item_photos</c>
/// so base64 never lands in the job's stored ResultJson.
///
/// Operates on the JsonNode tree to leave every other (snake_case) field that the
/// frontend reads untouched. Best-effort throughout: a single bad photo is skipped,
/// and any outer failure falls back to stripping <c>item_photos</c> only.
/// </summary>
public class MenuImportPhotoMerger
{
    private readonly ItemPhotoStorage _storage;
    private readonly ILogger<MenuImportPhotoMerger> _logger;

    public MenuImportPhotoMerger(ItemPhotoStorage storage, ILogger<MenuImportPhotoMerger> logger)
    {
        _storage = storage;
        _logger = logger;
    }

    public async Task<JsonElement> MergeAsync(Guid restaurantId, JsonElement result, CancellationToken ct)
    {
        JsonObject? root;
        try
        {
            root = JsonNode.Parse(result.GetRawText()) as JsonObject;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Item photo merge: result JSON unparseable; returning as-is");
            return result;
        }
        if (root is null) return result;

        try
        {
            if (root["item_photos"] is JsonArray photos && root["items"] is JsonArray items)
            {
                var urlByRef = new Dictionary<(int Page, int Index), string>();
                foreach (var node in photos)
                {
                    try
                    {
                        if (node is not JsonObject photo) continue;
                        var page = (int)photo["source_page"]!;
                        var index = (int)photo["source_index"]!;
                        var base64 = (string?)photo["image_base64"];
                        if (string.IsNullOrEmpty(base64)) continue;

                        byte[] bytes;
                        try { bytes = Convert.FromBase64String(base64); }
                        catch (FormatException) { continue; }

                        var url = await _storage.SaveAsync(restaurantId, bytes, ct);
                        if (url is not null) urlByRef[(page, index)] = url;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Item photo merge: skipping a malformed photo entry");
                    }
                }

                foreach (var node in items)
                {
                    if (node is not JsonObject item) continue;
                    var page = (int?)item["source_page"];
                    var index = (int?)item["source_index"];
                    if (page is int p && index is int i && urlByRef.TryGetValue((p, i), out var url))
                        item["image_url"] = url;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Item photo merge failed for restaurant {RestaurantId}; stripping base64 only", restaurantId);
        }
        finally
        {
            // Always strip — base64 crops must never be persisted in ResultJson.
            root.Remove("item_photos");
        }

        return JsonSerializer.Deserialize<JsonElement>(root.ToJsonString());
    }
}
