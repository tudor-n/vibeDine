namespace RestaurantApi.Models;

/// <summary>
/// One row per phone seated at a table. Each guest owns their own draft
/// <see cref="Order"/>, so several people at the same table can build separate
/// carts and either merge them into one bill or pay separately at checkout.
/// Guests form organically — every phone that scans the QR is auto-assigned the
/// next "Guest N" slot. No upfront party-size prompt.
/// </summary>
public class TableGuest : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RestaurantId { get; set; }

    public Guid TableId { get; set; }
    public RestaurantTable? Table { get; set; }

    public string? DisplayName { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Touched on every (re)join. Reserved for a future stale-guest sweeper;
    // nothing reads it yet.
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
}
