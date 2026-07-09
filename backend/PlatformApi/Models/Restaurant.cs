namespace PlatformApi.Models;

/// <summary>A single location belonging to an <see cref="Organization"/>.</summary>
public class Restaurant
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid OrganizationId { get; set; }
    public Organization? Organization { get; set; }

    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string Timezone { get; set; } = "Europe/Bucharest";
    public string Currency { get; set; } = "RON";
    public string Locale { get; set; } = "ro-RO";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Membership> Memberships { get; set; } = [];
}
