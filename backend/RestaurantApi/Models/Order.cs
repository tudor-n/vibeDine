using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RestaurantApi.Models;

public class Order : ITenantScoped {
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RestaurantId { get; set; }

    public Guid TableId { get; set; }
    public RestaurantTable Table { get; set; } = null!;

    // The phone (TableGuest) that owns this draft. Null on legacy table-wide
    // drafts (created before multi-person ordering) and on merged orders.
    public Guid? GuestId { get; set; }

    // When this draft is merged into another guest's draft, this points at the
    // surviving order and Status becomes "merged" so it drops out of active queries.
    public Guid? MergedIntoOrderId { get; set; }

    [Required]
    public string Status { get; set; } = "draft";

    public string? PaymentStatus { get; set; } = "unpaid";

    [Column(TypeName = "decimal(10,2)")]
    public decimal TotalAmount { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Human-friendly sequential number per restaurant. Assigned on submit
    // (drafts have no number yet). Shown on the kitchen kanban and customer
    // status screen as "Comanda #N".
    public int? OrderNumber { get; set; }

    // Set when the order is dropped in the "Finalizată" column. The
    // OrderFinalizationService uses this timestamp to soft-delete the order
    // after a grace period; cleared when the admin drags it back.
    public DateTime? ReadyAt { get; set; }

    // Soft-delete marker. Non-null orders are excluded from the active board
    // and surface only in the finalized log.
    public DateTime? DeletedAt { get; set; }

    // The staff member (waiter/manager) who claimed this order from the floor
    // plan ("Preia comanda"). Null while unclaimed — such orders show on the
    // Comenzi board greyed-out and immutable until someone takes them.
    public Guid? ClaimedByWaiterId { get; set; }
    public AppUser? ClaimedByWaiter { get; set; }

    public DateTime? ClaimedAt { get; set; }

    public ICollection<OrderItem> Items { get; set; } = [];

    public ICollection<FlashDealOffer> FlashDeals { get; set; } = [];
}