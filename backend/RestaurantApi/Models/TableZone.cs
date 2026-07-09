namespace RestaurantApi.Models;

/// <summary>
/// A floor-plan zone (table category) such as "Terasă" or "Salon". Tables
/// reference a zone via <see cref="RestaurantTable.ZoneId"/>. Soft-deleted via
/// <see cref="DeletedAt"/> so historical assignments stay intact.
/// </summary>
public class TableZone : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RestaurantId { get; set; }

    public string Name { get; set; } = string.Empty;   // "Terasă", "Salon"

    public int SortOrder { get; set; }                  // tab order

    // Soft-delete marker. A non-null value hides the zone from the admin list
    // while keeping the row intact for reporting. See ZonesController.Delete.
    public DateTime? DeletedAt { get; set; }
}
