using System.ComponentModel.DataAnnotations;

namespace RestaurantApi.Models;

public class Subcategory : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RestaurantId { get; set; }
    public Guid CategoryId { get; set; }
    public Category Category { get; set; } = null!;

    [Required]
    public string Name { get; set; } = string.Empty;

    public int? SortOrder { get; set; }
    public string? ImageUrl { get; set; }

    public ICollection<Product> Products { get; set; } = [];
}
