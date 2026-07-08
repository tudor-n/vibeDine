namespace RestaurantApi.Models;

public class RestaurantTable : ITenantScoped {
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RestaurantId { get; set; }

    public string? TableNumber { get; set; }

    public Guid? QrToken { get; set; }

    // Floor-plan zone (table category) this table belongs to. Optional FK to
    // TableZone; null means unassigned. Cleared (SetNull) when its zone is deleted.
    public Guid? ZoneId { get; set; }

    // Floor-plan position within the zone/canvas. Defaults to (0, 0).
    public double PositionX { get; set; }
    public double PositionY { get; set; }

    // Soft-delete marker. A non-null value hides the table from the admin list
    // and stops its QR token from resolving, while keeping the row (and its
    // historical orders) intact for reporting. See TablesController.Delete.
    public DateTime? DeletedAt { get; set; }

    public ICollection<Order> Orders { get; set; } = [];
}