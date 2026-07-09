namespace RestaurantApi.DTOs.Requests;

public record VibeyRecommendationRequest(
    List<VibeyCartItem> Items,
    string? Locale
);

public record VibeyCartItem(string Id, string Name, string? Category, int Qty);
