using System.ComponentModel.DataAnnotations.Schema;

namespace RestaurantApi.Models;

// The manager's deterministic flash-deal configuration: one product offered at a
// discount as a post-order claimable popup, optionally gated by a minimum order
// total. One row per restaurant (saving replaces it). Drives
// FlashDealService.EvaluateEligibilityAsync; the per-order offer it produces is a
// FlashDealOffer. Unlike HappyHourDeal this is NOT time-windowed — it fires after
// an order is submitted.
public class FlashDealRule : ITenantScoped {
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RestaurantId { get; set; }

    public Guid ProductId { get; set; }
    public Product? Product { get; set; }

    public int DiscountPercent { get; set; }

    // Only offer when the order total is at least this much. Null = no gate
    // (offer after every order where the product isn't already in the cart).
    [Column(TypeName = "decimal(10,2)")]
    public decimal? MinOrderTotal { get; set; }

    public bool IsEnabled { get; set; } = true;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
