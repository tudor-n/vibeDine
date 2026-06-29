namespace RestaurantApi.DTOs.Responses;

public class AdminIngredientResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Unit { get; set; } = "piece";
    public decimal QuantityOnHand { get; set; }
    public decimal LowStockThreshold { get; set; }
    public bool IsLow { get; set; }
    /// <summary>Non-null when this stock row backs a simple-tracked product.</summary>
    public Guid? LinkedProductId { get; set; }
    public string? LinkedProductName { get; set; }
}

public class RecipeLineResponse
{
    public Guid IngredientId { get; set; }
    public string IngredientName { get; set; } = string.Empty;
    public string Unit { get; set; } = "piece";
    public decimal QuantityPerPortion { get; set; }
}

public class ProductStockResponse
{
    public Guid ProductId { get; set; }
    public string Mode { get; set; } = "untracked"; // untracked | recipe | simple
    public List<RecipeLineResponse> Recipe { get; set; } = new();
    public Guid? SimpleIngredientId { get; set; }
    public decimal? SimpleQuantity { get; set; }
    public string? SimpleUnit { get; set; }
    public decimal? SimpleThreshold { get; set; }
}
