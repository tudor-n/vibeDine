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

    // Join the restaurant-wide menu group to receive live stock availability updates
    // (items going out of / back into stock) while browsing the menu.
    public async Task JoinRestaurantMenuGroup(string restaurantId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, TenantGroups.Menu(restaurantId));
    }
}
