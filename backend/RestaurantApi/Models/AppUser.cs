using Microsoft.AspNetCore.Identity;

namespace RestaurantApi.Models;

/// <summary>Application user. Identity is keyed by <see cref="Guid"/>.</summary>
public class AppUser : IdentityUser<Guid>
{
    public string FullName { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Membership> Memberships { get; set; } = [];
}
