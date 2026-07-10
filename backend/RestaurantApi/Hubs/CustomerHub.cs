using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using RestaurantApi.Data;

namespace RestaurantApi.Hubs;

// Anonymous customer hub. The connection carries the scanned QR token on the query
// string (?token=<qrToken>, set by the customer SPA's useWebSocket). That token is
// the ONLY authorization the diner has, so every group join is bound to it: the
// group name is derived server-side from the table the token resolves to, and any
// client-supplied restaurantId/tableId that does not match is rejected. Without this
// an anonymous caller could name any table/restaurant group and eavesdrop on other
// tables' or tenants' realtime order/flash-deal/party events.
public class CustomerHub : Hub
{
    private readonly AppDbContext _db;

    public CustomerHub(AppDbContext db) => _db = db;

    // Resolve the connection's qrToken to its (restaurantId, tableId). No ambient
    // tenant is resolved for a hub connection, so the global query filter would hide
    // every table — look the token up with the filter ignored; the secret token is
    // itself the proof of possession.
    private async Task<(Guid RestaurantId, Guid TableId)?> ResolveTableAsync()
    {
        var raw = Context.GetHttpContext()?.Request.Query["token"].ToString();
        if (!Guid.TryParse(raw, out var qrToken))
            return null;

        var table = await _db.Tables
            .IgnoreQueryFilters()
            .Where(t => t.QrToken == qrToken && t.DeletedAt == null)
            .Select(t => new { t.RestaurantId, t.Id })
            .FirstOrDefaultAsync();

        return table is null ? null : (table.RestaurantId, table.Id);
    }

    public async Task JoinTableGroup(string restaurantId, string tableId)
    {
        var resolved = await ResolveTableAsync();
        if (resolved is null)
            throw new HubException("Invalid or missing table session.");

        var (rid, tid) = resolved.Value;

        // The caller may only join the group for the table its own token maps to.
        if (!GuidEquals(restaurantId, rid) || !GuidEquals(tableId, tid))
            throw new HubException("Not authorized for this table.");

        await Groups.AddToGroupAsync(Context.ConnectionId, TenantGroups.Table(rid, tid));
    }

    public async Task LeaveTableGroup(string restaurantId, string tableId)
    {
        // Leaving only ever narrows scope; drop the caller from its own table group
        // and ignore client-supplied ids so it can never affect another table.
        var resolved = await ResolveTableAsync();
        if (resolved is null) return;

        var (rid, tid) = resolved.Value;
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, TenantGroups.Table(rid, tid));
    }

    // Join the restaurant-wide menu group to receive live stock availability updates
    // (items going out of / back into stock) while browsing the menu. Bound to the
    // restaurant the caller's qrToken belongs to.
    public async Task JoinRestaurantMenuGroup(string restaurantId)
    {
        var resolved = await ResolveTableAsync();
        if (resolved is null)
            throw new HubException("Invalid or missing table session.");

        var (rid, _) = resolved.Value;

        if (!GuidEquals(restaurantId, rid))
            throw new HubException("Not authorized for this restaurant.");

        await Groups.AddToGroupAsync(Context.ConnectionId, TenantGroups.Menu(rid));
    }

    private static bool GuidEquals(string? raw, Guid expected) =>
        Guid.TryParse(raw, out var parsed) && parsed == expected;
}
