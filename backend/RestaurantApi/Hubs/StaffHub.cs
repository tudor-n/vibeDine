using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;
using RestaurantApi.Tenancy;

namespace RestaurantApi.Hubs;

[Authorize]
public class StaffHub : Hub
{
    private static readonly Dictionary<string, string> AllowedRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["admin"] = "admin",
        ["WaitStaff"] = "WaitStaff",
    };

    private readonly ITenantAccess _tenantAccess;

    public StaffHub(ITenantAccess tenantAccess)
    {
        _tenantAccess = tenantAccess;
    }

    public async Task JoinRoleGroup(string restaurantId, string role)
    {
        var group = await AuthorizeGroupAccessAsync(restaurantId, role);
        await Groups.AddToGroupAsync(Context.ConnectionId, group);
    }

    public async Task LeaveRoleGroup(string restaurantId, string role)
    {
        var group = await AuthorizeGroupAccessAsync(restaurantId, role);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, group);
    }

    private async Task<string> AuthorizeGroupAccessAsync(string restaurantId, string role)
    {
        if (!Guid.TryParse(restaurantId, out var parsedRestaurantId))
            throw new HubException("Invalid restaurant id.");

        if (!AllowedRoles.TryGetValue(role, out var canonicalRole))
            throw new HubException("Unknown staff group.");

        var userIdRaw = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier)
                        ?? Context.User?.FindFirstValue("sub");
        if (!Guid.TryParse(userIdRaw, out var userId))
            throw new HubException("Authenticated user id is missing.");

        if (!await _tenantAccess.CanAccessRestaurantAsync(userId, parsedRestaurantId))
            throw new HubException("You do not have access to this restaurant.");

        return TenantGroups.Role(parsedRestaurantId, canonicalRole);
    }
}
