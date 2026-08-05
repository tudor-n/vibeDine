using System.ComponentModel.DataAnnotations.Schema;

namespace RestaurantApi.Models;

public class OrderItem : ITenantScoped {
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RestaurantId { get; set; }

    public Guid OrderId { get; set; }
    public Order Order { get; set; } = null!;

    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public int Quantity { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal UnitPriceAtPurchase { get; set; }

    public bool? WasAiRecommended { get; set; } = false;

    public bool? IsFlashDealItem { get; set; } = false;

    // Free-text kitchen remark the waiter attaches to a line ("fără ceapă",
    // "bine făcut"). Capped at 200 chars by the API; null on QR-placed lines.
    public string? Notes { get; set; }
}