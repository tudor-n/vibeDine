using Microsoft.AspNetCore.SignalR;


namespace RestaurantApi.Hubs;

public class CustomerHub : Hub
{
    public async Task JoinTableGroup(String tableId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, tableId);
    }

    public async Task LeaveTableGroup(string tableId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, tableId);
    }
}