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

/// <summary>
/// Maps <see cref="StockUnit"/> to/from a stable string code used on the wire (the API serializes
/// enums as ints by default, and a string code is clearer and reorder-proof for the frontend).
/// </summary>
public static class StockUnits
{
    public static string ToCode(StockUnit unit) => unit switch
    {
        StockUnit.Gram => "gram",
        StockUnit.Kilogram => "kilogram",
        StockUnit.Milliliter => "milliliter",
        StockUnit.Liter => "liter",
        _ => "piece",
    };

    public static bool TryFromCode(string? code, out StockUnit unit)
    {
        switch ((code ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "piece": unit = StockUnit.Piece; return true;
            case "gram": unit = StockUnit.Gram; return true;
            case "kilogram": unit = StockUnit.Kilogram; return true;
            case "milliliter": unit = StockUnit.Milliliter; return true;
            case "liter": unit = StockUnit.Liter; return true;
            default: unit = StockUnit.Piece; return false;
        }
    }
}
