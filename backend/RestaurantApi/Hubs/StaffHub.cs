using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace RestaurantApi.Hubs;

[Authorize]
public class StaffHub : Hub
{
    public async Task JoinRoleGroup(string restaurantId, string role)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, TenantGroups.Role(restaurantId, role));
    }

    public async Task LeaveRoleGroup(string restaurantId, string role)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, TenantGroups.Role(restaurantId, role));
    }
}
