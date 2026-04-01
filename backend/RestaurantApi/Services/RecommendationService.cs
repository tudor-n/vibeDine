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

    public RecommendationService(IHttpClientFactory httpClientFactory, AppDbContext dbContext, ILogger<RecommendationService> logger)
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

        if (order == null) return new List<RecommendationResponse>();

        var cartItems = order.Items.Select(i => new {
            product_id = i.ProductId.ToString(),
            quantity = i.Quantity
        }).ToList();

        var requestBody = new {
            cart_items = cartItems,
            table_id = order.TableId.ToString(),
            order_history = new List<string>()
        };

        try
        {
            var client = _httpClientFactory.CreateClient("AiService");
            var response = await client.PostAsJsonAsync("/recommend", requestBody);
            
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<JsonElement>();
                var recommendations = result.GetProperty("recommendations")
                    .Deserialize<List<RecommendationResponse>>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                
                if (recommendations != null) return recommendations;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch recommendations from AI service. Falling back to static recommendations.");
        }

        var randomProducts = await _dbContext.Products
            .Where(p => p.IsAvailable == true)
            .OrderBy(r => Guid.NewGuid())
            .Take(2)
            .Select(p => new RecommendationResponse(p.Id.ToString(), "fallback", 0.5))
            .ToListAsync();

        return randomProducts;
    }
}