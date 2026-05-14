using System.ComponentModel.DataAnnotations;

namespace RestaurantApi.Models;

public class Category{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public string Name { get; set; } = string.Empty;

    public int? SortOrder { get; set; }

    public string? ImageUrl { get; set; } = null;

    public ICollection<Product> Products { get; set; } = [];
}