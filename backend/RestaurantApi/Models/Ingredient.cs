using System.ComponentModel.DataAnnotations;

namespace RestaurantApi.Models;

/// <summary>
/// A unit of stock held by a restaurant. Backs BOTH recipe ingredients (flour, mozzarella) and
/// simple-tracked products (a bottled drink): a simple product owns exactly one Ingredient row,
/// linked via <see cref="LinkedProductId"/>. Keeping both kinds in one table lets the
/// decrement/restore engine, the low-stock query and the restock action share a single code path.
/// </summary>
public class Ingredient : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RestaurantId { get; set; }

    [Required]
    public string Name { get; set; } = string.Empty;

    public StockUnit Unit { get; set; } = StockUnit.Piece;

    /// <summary>
    /// Current quantity on hand, expressed in <see cref="Unit"/>. May go transiently negative under
    /// concurrent orders — we surface the affected items as unavailable rather than hard-blocking.
    /// </summary>
    public decimal QuantityOnHand { get; set; }

    /// <summary>When QuantityOnHand drops to or below this, the ingredient counts as "running low".</summary>
    public decimal LowStockThreshold { get; set; }

    /// <summary>Non-null only for the auto-row backing a simple-tracked product (1:1).</summary>
    public Guid? LinkedProductId { get; set; }
    public Product? LinkedProduct { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
