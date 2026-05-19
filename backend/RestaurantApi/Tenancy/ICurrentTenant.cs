namespace RestaurantApi.Tenancy;

/// <summary>
/// Ambient, request-scoped tenant context. Set by <see cref="TenantResolutionMiddleware"/>
/// (from the route's restaurantId or a table's qr_token) and consumed by EF Core global
/// query filters so every tenant-scoped query is isolated automatically.
/// </summary>
public interface ICurrentTenant
{
    /// <summary>The active restaurant, or null when no tenant is in scope (auth/org endpoints).</summary>
    Guid? RestaurantId { get; }

    /// <summary>True while a deliberate cross-tenant operation (seeding, org-owner listing) is running.</summary>
    bool IsResolutionSuppressed { get; }

    void SetRestaurant(Guid restaurantId);

    /// <summary>Temporarily disable the tenant filter; dispose to restore.</summary>
    IDisposable SuppressResolution();
}

public sealed class CurrentTenant : ICurrentTenant
{
    public Guid? RestaurantId { get; private set; }
    public bool IsResolutionSuppressed { get; private set; }

    public void SetRestaurant(Guid restaurantId) => RestaurantId = restaurantId;

    public IDisposable SuppressResolution()
    {
        IsResolutionSuppressed = true;
        return new Suppression(this);
    }

    private sealed class Suppression(CurrentTenant owner) : IDisposable
    {
        public void Dispose() => owner.IsResolutionSuppressed = false;
    }
}
