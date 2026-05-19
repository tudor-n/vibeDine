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
}
