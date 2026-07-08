namespace PlatformApi.Models;

/// <summary>Mirrors the main app's membership roles (stored as int).</summary>
public enum MembershipRole
{
    OrgOwner = 0,
    RestaurantAdmin = 1,
    Waiter = 2,
}

public static class MembershipStatus
{
    public const string Active = "active";
    public const string PendingApproval = "pending_approval";
    public const string Rejected = "rejected";
}

/// <summary>Grants a user a role within an organization and (optionally) a single location.</summary>
public class Membership
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }
    public AppUser? User { get; set; }

    public Guid OrganizationId { get; set; }
    public Organization? Organization { get; set; }

    public Guid? RestaurantId { get; set; }
    public Restaurant? Restaurant { get; set; }

    public MembershipRole Role { get; set; }
    public string Status { get; set; } = MembershipStatus.Active;
    public Guid? InvitedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
