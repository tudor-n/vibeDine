namespace RestaurantApi.Models;

public enum MembershipRole
{
    /// <summary>Owns the organization and, implicitly, every restaurant in it.</summary>
    OrgOwner = 0,

    /// <summary>Invited co-owner with dashboard access to a single restaurant.</summary>
    RestaurantAdmin = 1
}

/// <summary>
/// Grants a user a role within a tenant. An <see cref="MembershipRole.OrgOwner"/> row has a null
/// <see cref="RestaurantId"/> and covers the whole org; a <see cref="MembershipRole.RestaurantAdmin"/>
/// row is scoped to one restaurant.
/// </summary>
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

    public Guid? InvitedByUserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
