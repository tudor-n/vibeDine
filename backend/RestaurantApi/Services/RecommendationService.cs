using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantApi.Data;
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

    public async Task<List<RecommendationResponse>> GetRecommendationsAsync(Guid orderId)
    {
        var order = await _dbContext.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId);

        if (order == null) return [];

        var cartProductIds = order.Items.Select(i => i.ProductId).ToHashSet();

        var availableProducts = await _dbContext.Products
            .Where(p => p.IsAvailable == true && !cartProductIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Name })
            .ToListAsync();

        var availableIdSet = availableProducts.Select(p => p.Id.ToString()).ToHashSet();

        var ruleDriven = await GetRuleDrivenRecommendationsAsync(cartProductIds, availableIdSet);

        var requestBody = new
        {
            cart_items = order.Items.Select(i => new
            {
                product_id = i.ProductId.ToString(),
                quantity = i.Quantity
            }).ToList(),
            table_id = order.TableId.ToString(),
            menu_items = availableProducts.Select(p => new
            {
                product_id = p.Id.ToString(),
                name = p.Name
            }).ToList(),
            order_history = new List<string>()
        };

        List<RecommendationResponse> aiRecs = new();

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
            _logger.LogWarning(ex, "AI service unreachable, falling back to rule-driven / random recommendations.");
        }

        var combined = ruleDriven
            .Concat(aiRecs)
            .GroupBy(r => r.ProductId)
            .Select(g => g.First())
            .Take(3)
            .ToList();

        if (combined.Count > 0) return combined;

        return availableProducts
            .OrderBy(_ => Guid.NewGuid())
            .Take(2)
            .Select(p => new RecommendationResponse(p.Id.ToString(), "pairing", 0.5))
            .ToList();
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
