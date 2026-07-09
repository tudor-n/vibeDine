namespace PlatformApi.Models;

/// <summary>
/// A restaurant chain / customer account. Owned by the existing main schema; this
/// service reads and writes it but never migrates it.
/// </summary>
public class Organization
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Restaurant> Restaurants { get; set; } = [];
    public ICollection<Membership> Memberships { get; set; } = [];
}
