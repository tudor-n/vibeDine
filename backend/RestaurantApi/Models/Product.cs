using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RestaurantApi.Models;

public class Product : ITenantScoped {
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RestaurantId { get; set; }

    public Guid CategoryId { get; set; }
    public Category Category { get; set; } = null!;

    public Guid? SubcategoryId { get; set; }
    public Subcategory? Subcategory { get; set; }

    [Required]
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    [Column(TypeName = "decimal(10,2)")]
    public decimal Price { get; set; }

    public string? ImageUrl { get; set; } = null;

    public bool? IsAvailable { get; set; } = true;

    public int SortOrder { get; set; } = 0;
}