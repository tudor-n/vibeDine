using Microsoft.AspNetCore.SignalR;

namespace RestaurantApi.Hubs;

public class StaffHub : Hub
{
    public async Task JoinRoleGroup(string role)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, role);
    }

    public async Task LeaveRoleGroup(string role)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, role);
    }
}