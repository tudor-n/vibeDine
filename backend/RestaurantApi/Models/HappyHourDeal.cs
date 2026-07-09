namespace RestaurantApi.Models;

// A restaurant's single "happy hour" promotion: one product discounted by a
// fixed percentage during a daily wall-clock window (interpreted in the
// restaurant's Timezone). It auto-activates inside the window every day and is
// dormant outside it — there is one row per restaurant (saving replaces it).
// Replaces the old AI-parsed AIDirective: eligibility is now a deterministic
// time check (see FlashDealService.EvaluateEligibilityAsync), not an LLM call.
public class HappyHourDeal : ITenantScoped {
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RestaurantId { get; set; }

    public Guid ProductId { get; set; }
    public Product? Product { get; set; }

    public int DiscountPercent { get; set; }

    // Wall-clock window in the restaurant's local timezone (Restaurant.Timezone).
    // StartTime > EndTime means the window wraps past midnight (e.g. 22:00–02:00).
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }

    public bool IsEnabled { get; set; } = true;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
