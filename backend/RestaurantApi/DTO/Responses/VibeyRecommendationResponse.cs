namespace RestaurantApi.DTOs.Responses;

public record VibeyRecommendationResponse(
    string Summary,
    List<VibeyRec> Recommendations
);

public record VibeyRec(string Id, string Name, string? Category, decimal PriceRon, string Reason);
