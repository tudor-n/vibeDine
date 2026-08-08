namespace RestaurantApi.Models;

public class RestaurantTable : ITenantScoped {
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RestaurantId { get; set; }

    public string? TableNumber { get; set; }

    public Guid? QrToken { get; set; }

    // Number of seats (persons) the table sits. Drives the floor-plan icon —
    // exactly this many chairs are drawn around the table. Clamped to 1..12 at
    // the API; defaults to 4.
    public int Capacity { get; set; } = 4;

    // Floor-plan shape of the table: "round" | "square" | "rectangle" | "oval".
    // Drives how the icon is drawn and where chairs sit. Validated at the API;
    // defaults to "square".
    public string Shape { get; set; } = "square";

    // Floor-plan zone (table category) this table belongs to. Optional FK to
    // TableZone; null means unassigned. Cleared (SetNull) when its zone is deleted.
    public Guid? ZoneId { get; set; }

    // Floor-plan position within the zone/canvas. Defaults to (0, 0).
    public double PositionX { get; set; }
    public double PositionY { get; set; }

    // Auxiliary (parking) table owned by one staff member, used to park problem
    // orders. Never appears on the floor plan or the QR table list; QrToken
    // stays null. Managed via AuxTablesController, invisible to TablesController.
    public bool IsAux { get; set; }

    // The waiter who owns this aux table (one live aux table per staff member
    // per restaurant). SetNull on user hard-delete so parked orders survive the
    // account's removal.
    public Guid? AssignedWaiterId { get; set; }
    public AppUser? AssignedWaiter { get; set; }

    // Soft-delete marker. A non-null value hides the table from the admin list
    // and stops its QR token from resolving, while keeping the row (and its
    // historical orders) intact for reporting. See TablesController.Delete.
    public DateTime? DeletedAt { get; set; }

    public ICollection<Order> Orders { get; set; } = [];
}