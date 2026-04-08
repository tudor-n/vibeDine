using System.Text.Json;

namespace RestaurantApi.Services;

public class SimilarityService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SimilarityService> _logger;

    public SimilarityService(IHttpClientFactory httpClientFactory, ILogger<SimilarityService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<JsonElement> AutoGenerateRulesAsync(List<List<string>> orderHistory)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("AiService");
            var response = await client.PostAsJsonAsync(
                "/similarity/auto-generate",
                new { order_history = orderHistory });

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<JsonElement>();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI service unreachable during similarity rule generation.");
        }

        using var doc = JsonDocument.Parse("{\"rules\": []}");
        return doc.RootElement.Clone();
    }
}