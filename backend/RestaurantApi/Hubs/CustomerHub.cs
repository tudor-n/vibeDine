using Microsoft.AspNetCore.SignalR;

namespace RestaurantApi.Hubs;

public class CustomerHub : Hub
{
    public async Task JoinTableGroup(string restaurantId, string tableId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, TenantGroups.Table(restaurantId, tableId));
    }

    public async Task LeaveTableGroup(string restaurantId, string tableId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, TenantGroups.Table(restaurantId, tableId));
    }
}
