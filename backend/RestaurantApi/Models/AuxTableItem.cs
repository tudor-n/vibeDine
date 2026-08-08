using System.ComponentModel.DataAnnotations.Schema;

namespace RestaurantApi.Models;

/// <summary>
/// One line parked on a waiter's "masa auxiliară" — a private, per-waiter holding
/// area for order lines pulled off a live round (a problem with the dish, guests
/// changing tables, …). Not a physical table: it never appears on the floor plan,
/// has no QR token and no service session. The price is a snapshot taken when the
/// line left its source order, so the money moves with the food unchanged; stock
/// is untouched in both directions (the food already physically exists).
/// </summary>
public class AuxTableItem : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RestaurantId { get; set; }

    /// <summary>The owner. Every staffer has exactly one aux table: theirs.</summary>
    public Guid WaiterUserId { get; set; }

    public Guid ProductId { get; set; }
    public Product? Product { get; set; }

    /// <summary>Snapshot — the row must still read correctly if the product is retired.</summary>
    public string ProductName { get; set; } = string.Empty;

    public int Quantity { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal UnitPriceAtPurchase { get; set; }

    /// <summary>The kitchen remark carried over from the source line, if any.</summary>
    public string? Notes { get; set; }

    // Provenance, for display only ("de la masa 12, comanda #41").
    public Guid? SourceOrderId { get; set; }
    public int? SourceOrderNumber { get; set; }
    public string? SourceTableNumber { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
