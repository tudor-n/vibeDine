using Microsoft.EntityFrameworkCore;
using RestaurantApi.Data;
using RestaurantApi.Models;

namespace RestaurantApi.Tenancy;

/// <summary>Authorization checks for tenant access. Encodes the three-role model.</summary>
public interface ITenantAccess
{
    /// <summary>
    /// True if the user may access the restaurant's dashboard: either an OrgOwner of the
    /// owning organization, a RestaurantAdmin invited to that specific restaurant, or an
    /// active Waiter assigned to it. Pending/rejected waiter memberships do not grant access.
    /// </summary>
    Task<bool> CanAccessRestaurantAsync(Guid userId, Guid restaurantId);

    /// <summary>True if the user is the OrgOwner of the organization.</summary>
    Task<bool> IsOrgOwnerAsync(Guid userId, Guid organizationId);

    /// <summary>
    /// True if the user can manage waiters of the restaurant — i.e. generate the signup link,
    /// approve / reject pending waiters, and remove active ones. OrgOwner of the org or
    /// RestaurantAdmin of the specific restaurant.
    /// </summary>
    Task<bool> CanManageRestaurantAsync(Guid userId, Guid restaurantId);

    /// <summary>True if the user holds an active Waiter membership for the restaurant.</summary>
    Task<bool> IsActiveWaiterAsync(Guid userId, Guid restaurantId);
}

public class TenantAccess : ITenantAccess
{
    private readonly AppDbContext _db;

    public TenantAccess(AppDbContext db) => _db = db;

    public async Task<bool> CanAccessRestaurantAsync(Guid userId, Guid restaurantId)
    {
        var orgId = await _db.Restaurants
            .Where(r => r.Id == restaurantId)
            .Select(r => (Guid?)r.OrganizationId)
            .FirstOrDefaultAsync();

        if (orgId is null) return false;

        return await _db.Memberships.AnyAsync(m =>
            m.UserId == userId &&
            m.Status == MembershipStatus.Active &&
            ((m.Role == MembershipRole.OrgOwner && m.OrganizationId == orgId) ||
             (m.Role == MembershipRole.RestaurantAdmin && m.RestaurantId == restaurantId) ||
             (m.Role == MembershipRole.Waiter && m.RestaurantId == restaurantId)));
    }

    public Task<bool> IsOrgOwnerAsync(Guid userId, Guid organizationId) =>
        _db.Memberships.AnyAsync(m =>
            m.UserId == userId &&
            m.OrganizationId == organizationId &&
            m.Status == MembershipStatus.Active &&
            m.Role == MembershipRole.OrgOwner);

    public async Task<bool> CanManageRestaurantAsync(Guid userId, Guid restaurantId)
    {
        var orgId = await _db.Restaurants
            .Where(r => r.Id == restaurantId)
            .Select(r => (Guid?)r.OrganizationId)
            .FirstOrDefaultAsync();

        if (orgId is null) return false;

        return await _db.Memberships.AnyAsync(m =>
            m.UserId == userId &&
            m.Status == MembershipStatus.Active &&
            ((m.Role == MembershipRole.OrgOwner && m.OrganizationId == orgId) ||
             (m.Role == MembershipRole.RestaurantAdmin && m.RestaurantId == restaurantId)));
    }

    public Task<bool> IsActiveWaiterAsync(Guid userId, Guid restaurantId) =>
        _db.Memberships.AnyAsync(m =>
            m.UserId == userId &&
            m.RestaurantId == restaurantId &&
            m.Role == MembershipRole.Waiter &&
            m.Status == MembershipStatus.Active);
}
