using System.ComponentModel.DataAnnotations;

namespace RestaurantApi.Models;

/// <summary>A single location belonging to an <see cref="Organization"/>.</summary>
public class Restaurant
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid OrganizationId { get; set; }
    public Organization? Organization { get; set; }

    [Required]
    public string Name { get; set; } = string.Empty;

    [Required]
    public string Slug { get; set; } = string.Empty;

    public string? Address { get; set; }

    public string Timezone { get; set; } = "Europe/Bucharest";

    public string Currency { get; set; } = "RON";

    public string Locale { get; set; } = "ro-RO";

    public bool IsActive { get; set; } = true;

    // --- White-label branding (all optional; disabled by default) ---
    public bool BrandingEnabled { get; set; } = false;
    public string? BrandPrimary { get; set; }
    public string? BrandSecondary { get; set; }
    public string? BrandBackground { get; set; }
    public string? LogoUrl { get; set; }
    public string? PhotoUrl { get; set; }
    public string? BrandPresetKey { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
