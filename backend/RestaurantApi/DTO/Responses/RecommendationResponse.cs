namespace RestaurantApi.DTOs.Responses;

public record RecommendationResponse(
    string ProductId,
    string Reason,
    double Confidence
);