namespace RestaurantApi.Models;

/// <summary>
/// One diner device's visit at a table, keyed by a client-persisted deviceId.
/// Survives order submission (unlike TableGuest, whose seat is freed on submit)
/// so a re-scan restores the visit: submitted rounds, running total, pending
/// flash deal. Swept lazily on join once LastSeenAt falls outside
/// CustomerSessionOptions.SessionLifetimeMinutes.
/// </summary>
public class DeviceSession : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RestaurantId { get; set; }
    public Guid TableId { get; set; }
    public RestaurantTable? Table { get; set; }
    public string DeviceId { get; set; } = string.Empty; // client UUID, ≤64 chars
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
}
