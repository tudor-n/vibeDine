using System.ComponentModel.DataAnnotations.Schema;

namespace RestaurantApi.Models;

public class SimilarityRule : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RestaurantId { get; set; }

    public Guid ProductAId { get; set; }
    public Product? ProductA { get; set; }

    public Guid ProductBId { get; set; }
    public Product? ProductB { get; set; }

    [Column(TypeName = "decimal(5,4)")]
    public decimal Score { get; set; }

    public string? RuleType { get; set; } = "manual";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}