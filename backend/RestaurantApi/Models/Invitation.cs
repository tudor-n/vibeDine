using System.ComponentModel.DataAnnotations;

namespace RestaurantApi.Models;

/// <summary>
/// A pending invite for someone to join a restaurant as a co-owner/admin.
/// Accepted via <see cref="Token"/>; no email infrastructure required (copyable link).
/// </summary>
public class Invitation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public string Email { get; set; } = string.Empty;

    public Guid OrganizationId { get; set; }
    public Organization? Organization { get; set; }

    public Guid? RestaurantId { get; set; }
    public Restaurant? Restaurant { get; set; }

    public MembershipRole Role { get; set; } = MembershipRole.RestaurantAdmin;

    public Guid Token { get; set; } = Guid.NewGuid();

    /// <summary>pending | accepted | revoked | expired</summary>
    public string Status { get; set; } = "pending";

    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddDays(7);

    public Guid InvitedByUserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
