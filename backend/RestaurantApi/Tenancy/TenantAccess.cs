using Microsoft.EntityFrameworkCore;
using RestaurantApi.Data;
using RestaurantApi.Models;

namespace RestaurantApi.Tenancy;

/// <summary>Authorization checks for tenant access. Encodes the two-role model.</summary>
public interface ITenantAccess
{
    /// <summary>
    /// True if the user may access the restaurant's dashboard: either an OrgOwner of the
    /// owning organization, or a RestaurantAdmin invited to that specific restaurant.
    /// </summary>
    Task<bool> CanAccessRestaurantAsync(Guid userId, Guid restaurantId);

    /// <summary>True if the user is the OrgOwner of the organization.</summary>
    Task<bool> IsOrgOwnerAsync(Guid userId, Guid organizationId);
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
            ((m.Role == MembershipRole.OrgOwner && m.OrganizationId == orgId) ||
             (m.Role == MembershipRole.RestaurantAdmin && m.RestaurantId == restaurantId)));
    }

    public Task<bool> IsOrgOwnerAsync(Guid userId, Guid organizationId) =>
        _db.Memberships.AnyAsync(m =>
            m.UserId == userId &&
            m.OrganizationId == organizationId &&
            m.Role == MembershipRole.OrgOwner);
}
