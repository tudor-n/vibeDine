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

        try
        {
            var client = _httpClientFactory.CreateClient("AiService");
            var response = await client.PostAsJsonAsync("/recommend", requestBody);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<JsonElement>();
                var rawRecs = result.GetProperty("recommendations")
                    .Deserialize<List<RecommendationResponse>>(
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (rawRecs is { Count: > 0 })
                {
                    var validRecs = rawRecs
                        .Where(r => availableIdSet.Contains(r.ProductId))
                        .Take(3)
                        .ToList();

                    if (validRecs.Count > 0)
                        return validRecs;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI service unreachable, falling back to random recommendations.");
        }

        return availableProducts
            .OrderBy(_ => Guid.NewGuid())
            .Take(2)
            .Select(p => new RecommendationResponse(p.Id.ToString(), "pairing", 0.5))
            .ToList();
    }
}