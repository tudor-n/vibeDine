using System.ComponentModel.DataAnnotations;

namespace RestaurantApi.Models;

public class Category : ITenantScoped {
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RestaurantId { get; set; }

    [Required]
    public string Name { get; set; } = string.Empty;

    public int? SortOrder { get; set; }

    public string? ImageUrl { get; set; } = null;

    public ICollection<Product> Products { get; set; } = [];
}