using System.ComponentModel.DataAnnotations.Schema;

namespace RestaurantApi.Models;

public class FlashDealOffer : ITenantScoped {
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RestaurantId { get; set; }

    public Guid? OrderId { get; set; }
    public Order? Order { get; set; }

    public Guid? ProductId { get; set; }
    public Product? Product { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal? DiscountPrice { get; set; }

    public DateTime? ExpiresAt { get; set; }

    public string? Status { get; set; } = "pending";
}