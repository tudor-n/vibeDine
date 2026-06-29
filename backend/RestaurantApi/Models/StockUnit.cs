namespace RestaurantApi.Models;

/// <summary>
/// Unit of measure for a stock <see cref="Ingredient"/>. A small fixed preset — there is
/// deliberately NO conversion math between units; each ingredient is counted purely in its own
/// unit. Persisted as an int. Romanian display labels (buc, g, kg, ml, l) live in the frontend.
/// </summary>
public enum StockUnit
{
    Piece = 0,
    Gram = 1,
    Kilogram = 2,
    Milliliter = 3,
    Liter = 4,
}
