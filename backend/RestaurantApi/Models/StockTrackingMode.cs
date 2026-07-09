namespace RestaurantApi.Models;

/// <summary>
/// How a <see cref="Product"/> consumes stock. A product is in exactly one mode at a time.
/// Persisted as an int.
/// </summary>
public enum StockTrackingMode
{
    /// <summary>No stock logic — always available, behaves exactly as before this feature existed.</summary>
    Untracked = 0,

    /// <summary>Composite dish: consumes its <see cref="RecipeItem"/> ingredients when ordered.</summary>
    Recipe = 1,

    /// <summary>The product is its own stock unit, backed 1:1 by an auto-linked <see cref="Ingredient"/>.</summary>
    Simple = 2,
}
