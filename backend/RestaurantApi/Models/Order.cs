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

    // The device-bound visit this order belongs to; null for legacy orders and
    // after the session is swept (orders outlive sessions as plain history).
    public Guid? DeviceSessionId { get; set; }

    // When this draft is merged into another guest's draft, this points at the
    // surviving order and Status becomes "merged" so it drops out of active queries.
    public Guid? MergedIntoOrderId { get; set; }

    // The waiter's table-service session this order belongs to. Set when a waiter
    // creates the order, when a QR order is submitted at a claimed table, and when a
    // claim adopts the table's orphan active orders. Null for orders placed at an
    // unclaimed table (and after the session row is deleted — SetNull).
    public Guid? TableServiceSessionId { get; set; }

    // Where the order came from: "qr" (a diner's phone) or "waiter" (entered on the
    // waiter's device). This is the ORIGIN, not the fulfillment channel — the delivery
    // storefront spec owns OrderType (dine_in|delivery|takeaway) separately, and waiter
    // orders are dine_in orders whose origin happens to be the waiter.
    public string Source { get; set; } = "qr";

    // The staff member who entered a waiter-sourced order. Null on QR orders.
    public Guid? PlacedByUserId { get; set; }
    public AppUser? PlacedByUser { get; set; }

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

    // True once the order's ingredients have been subtracted from stock (at dispatch to the
    // kitchen). Drives idempotent decrement and a one-time restore if the order is cancelled.
    public bool StockDecremented { get; set; } = false;

    // The staff member (waiter/manager) who claimed this order from the floor
    // plan ("Preia comanda"). Null while unclaimed — such orders show on the
    // Comenzi board greyed-out and immutable until someone takes them.
    public Guid? ClaimedByWaiterId { get; set; }
    public AppUser? ClaimedByWaiter { get; set; }

    public DateTime? ClaimedAt { get; set; }

    public ICollection<OrderItem> Items { get; set; } = [];

    public ICollection<FlashDealOffer> FlashDeals { get; set; } = [];
}