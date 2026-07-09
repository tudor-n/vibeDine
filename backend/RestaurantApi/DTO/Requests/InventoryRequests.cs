namespace RestaurantApi.DTOs.Requests;

public class CreateIngredientRequest
{
    public string Name { get; set; } = string.Empty;
    public string Unit { get; set; } = "piece";
    public decimal QuantityOnHand { get; set; }
    public decimal LowStockThreshold { get; set; }
}

public class UpdateIngredientRequest
{
    public string? Name { get; set; }
    public string? Unit { get; set; }
    public decimal? LowStockThreshold { get; set; }
}

/// <summary>Restock / adjust. SetTo (absolute, ≥ 0) wins if present, otherwise Delta (+/-) is applied.</summary>
public class AdjustStockRequest
{
    public decimal? Delta { get; set; }
    public decimal? SetTo { get; set; }
}

public class RecipeLineRequest
{
    public Guid IngredientId { get; set; }
    public decimal QuantityPerPortion { get; set; }
}

/// <summary>Sets a product's stock-tracking mode and its associated recipe / simple stock in one call.</summary>
public class SetProductStockRequest
{
    public string Mode { get; set; } = "untracked"; // untracked | recipe | simple
    public List<RecipeLineRequest>? Recipe { get; set; }
    public decimal? SimpleQuantity { get; set; }
    public string? SimpleUnit { get; set; }
    public decimal? SimpleThreshold { get; set; }
}
