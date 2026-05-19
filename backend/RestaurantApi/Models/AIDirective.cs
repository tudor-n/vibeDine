using System.ComponentModel.DataAnnotations.Schema;

namespace RestaurantApi.Models;

public class AIDirective : ITenantScoped {
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RestaurantId { get; set; }

    public string? RawPrompt { get; set; }

    [Column(TypeName = "jsonb")]
    public string? ParsedRules { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool? IsActive { get; set; } = true;
}