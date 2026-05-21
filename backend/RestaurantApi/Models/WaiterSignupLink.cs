namespace RestaurantApi.Models;

/// <summary>
/// A reusable, public signup link for a single restaurant. The manager generates one per restaurant
/// and shares it with prospective waiters. Anyone with the URL can sign up; the resulting Membership
/// starts in <see cref="MembershipStatus.PendingApproval"/> until the manager approves it.
/// Only one row per restaurant should have <see cref="IsActive"/> true at a time; regenerating
/// flips the existing row off and inserts a fresh one with a new <see cref="Token"/>.
/// </summary>
public class WaiterSignupLink
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid OrganizationId { get; set; }
    public Organization? Organization { get; set; }

    public Guid RestaurantId { get; set; }
    public Restaurant? Restaurant { get; set; }

    public Guid Token { get; set; } = Guid.NewGuid();

    public bool IsActive { get; set; } = true;

    public Guid CreatedByUserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
