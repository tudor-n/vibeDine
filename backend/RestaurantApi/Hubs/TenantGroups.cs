namespace RestaurantApi.Hubs;

/// <summary>
/// Restaurant-namespaced SignalR group names so realtime messages (flash deals,
/// staff alerts) never leak across tenants.
/// </summary>
public static class TenantGroups
{
    public static string Table(Guid restaurantId, Guid tableId) => $"r:{restaurantId}:t:{tableId}";

    public static string Table(string restaurantId, string tableId) => $"r:{restaurantId}:t:{tableId}";

    public static string Role(Guid restaurantId, string role) => $"r:{restaurantId}:{role}";

    public static string Role(string restaurantId, string role) => $"r:{restaurantId}:{role}";

    // Restaurant-wide menu group: every phone browsing the menu joins it so live
    // stock availability changes (an item going out of / back into stock) reach all guests.
    public static string Menu(Guid restaurantId) => $"r:{restaurantId}:menu";

    public static string Menu(string restaurantId) => $"r:{restaurantId}:menu";
}
