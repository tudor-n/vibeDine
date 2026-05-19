using System.ComponentModel.DataAnnotations;

namespace RestaurantApi.Models;

/// <summary>A restaurant chain. Created by, and owned by, its master owner.</summary>
public class Organization
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public string Name { get; set; } = string.Empty;

    [Required]
    public string Slug { get; set; } = string.Empty;

    public Guid CreatedByUserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Restaurant> Restaurants { get; set; } = [];
}
