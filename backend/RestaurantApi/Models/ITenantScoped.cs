namespace RestaurantApi.Models;

/// <summary>
/// Marks an entity that belongs to a single restaurant. The DbContext auto-stamps
/// <see cref="RestaurantId"/> on insert and applies a global query filter on read.
/// </summary>
public interface ITenantScoped
{
    Guid RestaurantId { get; set; }
}
