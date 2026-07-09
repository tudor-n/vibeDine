using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantApi.Data;
using RestaurantApi.DTOs.Requests;
using RestaurantApi.DTOs.Responses;

namespace RestaurantApi.Services;

public class RecommendationService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AppDbContext _dbContext;
    private readonly ILogger<RecommendationService> _logger;

    public RecommendationService(
        IHttpClientFactory httpClientFactory,
        AppDbContext dbContext,
        ILogger<RecommendationService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Vibey: lazy, cart-based recommendations. Unlike the old order-based flow,
    /// this does not require a persisted order row — it works straight off the
    /// guest's in-memory cart, so it can be called before the cart is ever
    /// submitted. Any failure (unparseable cart, AI unreachable, zero surviving
    /// recs) degrades to an empty result — never an exception, never a random
    /// fallback.
    /// </summary>
    public async Task<VibeyRecommendationResponse> GetVibeyRecommendationsAsync(
        IReadOnlyList<VibeyCartItem> cartItems, string language)
    {
        try
        {
            var cartProductIds = cartItems
                .Select(i => Guid.TryParse(i.Id, out var g) ? g : (Guid?)null)
                .Where(g => g.HasValue)
                .Select(g => g!.Value)
                .ToHashSet();

            var candidateProducts = await _dbContext.Products
                .Include(p => p.Category)
                .Where(p => p.IsAvailable == true && !cartProductIds.Contains(p.Id))
                .ToListAsync();

            var candidateById = candidateProducts.ToDictionary(p => p.Id);
            var availableIdSet = candidateProducts.Select(p => p.Id.ToString()).ToHashSet();

            var ruleDriven = await GetRuleDrivenRecommendationsAsync(cartProductIds, availableIdSet);

            var requestBody = new
            {
                cart_items = cartItems.Select(i => new
                {
                    product_id = i.Id,
                    name = i.Name,
                    quantity = i.Qty
                }).ToList(),
                table_id = "",
                menu_items = candidateProducts.Select(p => new
                {
                    product_id = p.Id.ToString(),
                    name = p.Name
                }).ToList(),
                order_history = new List<string>(),
                language = language
            };

            List<RecommendationResponse> aiRecs = new();
            var summary = "";

            try
            {
                var client = _httpClientFactory.CreateClient("AiService");
                var response = await client.PostAsJsonAsync("/recommend", requestBody);

                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<JsonElement>();
                    var rawRecs = result.GetProperty("recommendations")
                        .Deserialize<List<RecommendationResponse>>(
                            new JsonSerializerOptions
                            {
                                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                                PropertyNameCaseInsensitive = true,
                            });

                    if (rawRecs is { Count: > 0 })
                    {
                        aiRecs = rawRecs
                            .Where(r => availableIdSet.Contains(r.ProductId))
                            .ToList();
                    }

                    if (result.TryGetProperty("summary", out var summaryEl) &&
                        summaryEl.ValueKind == JsonValueKind.String)
                    {
                        summary = summaryEl.GetString() ?? "";
                    }
                }
                else
                {
                    var body = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning(
                        "AI /recommend returned non-success status {StatusCode}. Body: {Body}",
                        (int)response.StatusCode,
                        body);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AI service unreachable, falling back to rule-driven recommendations.");
            }

            var combined = ruleDriven
                .Concat(aiRecs)
                .GroupBy(r => r.ProductId)
                .Select(g => g.First())
                .Take(3)
                .ToList();

            if (combined.Count == 0) return new VibeyRecommendationResponse("", []);

            var enriched = new List<VibeyRec>();
            foreach (var rec in combined)
            {
                if (!Guid.TryParse(rec.ProductId, out var productId)) continue;
                if (!candidateById.TryGetValue(productId, out var product)) continue;

                enriched.Add(new VibeyRec(
                    product.Id.ToString(),
                    product.Name,
                    product.Category?.Name,
                    product.Price,
                    rec.Reason));
            }

            return enriched.Count == 0
                ? new VibeyRecommendationResponse("", [])
                : new VibeyRecommendationResponse(summary, enriched);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Vibey recommendation generation failed; returning empty result.");
            return new VibeyRecommendationResponse("", []);
        }
    }

    private async Task<List<RecommendationResponse>> GetRuleDrivenRecommendationsAsync(
        HashSet<Guid> cartProductIds,
        HashSet<string> availableIdSet)
    {
        var directives = await _dbContext.RecommendationDirectives
            .Where(d => d.IsActive == true && d.ParsedRules != null)
            .ToListAsync();

        var matches = new List<RecommendationResponse>();
        var seen = new HashSet<string>();

        foreach (var directive in directives)
        {
            JsonElement parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<JsonElement>(directive.ParsedRules!);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Skipping directive {Id} — parsed_rules is not valid JSON", directive.Id);
                continue;
            }

            if (parsed.ValueKind != JsonValueKind.Object) continue;
            if (!parsed.TryGetProperty("rules", out var rulesArray)) continue;
            if (rulesArray.ValueKind != JsonValueKind.Array) continue;

            foreach (var rule in rulesArray.EnumerateArray())
            {
                if (rule.ValueKind != JsonValueKind.Object) continue;

                if (!rule.TryGetProperty("trigger_product_ids", out var triggerArr)
                    || triggerArr.ValueKind != JsonValueKind.Array)
                    continue;

                var triggers = triggerArr.EnumerateArray()
                    .Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() : null)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => Guid.TryParse(s, out var g) ? g : Guid.Empty)
                    .Where(g => g != Guid.Empty)
                    .ToHashSet();

                if (triggers.Count == 0) continue;
                if (!triggers.Any(cartProductIds.Contains)) continue;

                var reason = rule.TryGetProperty("reason", out var r)
                    && r.ValueKind == JsonValueKind.String
                        ? r.GetString() ?? "rule"
                        : "rule";

                var confidence = rule.TryGetProperty("confidence", out var c)
                    && c.ValueKind == JsonValueKind.Number
                        ? c.GetDouble()
                        : 0.85;

                if (!rule.TryGetProperty("recommend_product_ids", out var recArr)
                    || recArr.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var recId in recArr.EnumerateArray())
                {
                    if (recId.ValueKind != JsonValueKind.String) continue;
                    var idStr = recId.GetString();
                    if (string.IsNullOrWhiteSpace(idStr)) continue;
                    if (!availableIdSet.Contains(idStr)) continue;
                    if (!seen.Add(idStr)) continue;

                    matches.Add(new RecommendationResponse(idStr, reason, confidence));
                }
            }
        }

        return matches;
    }
}
