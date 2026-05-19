using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RestaurantApi.Models;

public class Order : ITenantScoped {
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RestaurantId { get; set; }

    public Guid TableId { get; set; }
    public RestaurantTable Table { get; set; } = null!;

    [Required]
    public string Status { get; set; } = "draft";

    public string? PaymentStatus { get; set; } = "unpaid";

    [Column(TypeName = "decimal(10,2)")]
    public decimal TotalAmount { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<OrderItem> Items { get; set; } = [];

    public ICollection<FlashDealOffer> FlashDeals { get; set; } = [];
}