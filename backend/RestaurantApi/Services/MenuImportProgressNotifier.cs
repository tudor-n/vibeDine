using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using RestaurantApi.Hubs;

namespace RestaurantApi.Services;

/// <summary>
/// Singleton. Emits SignalR progress events for menu import jobs.
/// Progress events are throttled to ≤ 1 per 250 ms per jobId.
/// Completed and Failed events are never throttled.
/// </summary>
public sealed class MenuImportProgressNotifier
{
    private static readonly TimeSpan ThrottleWindow = TimeSpan.FromMilliseconds(250);

    // Stores the Environment.TickCount64 (ms) of the last emitted progress for each jobId.
    private readonly ConcurrentDictionary<Guid, long> _lastEmitTick = new();

    private readonly IHubContext<StaffHub> _hub;

    public MenuImportProgressNotifier(IHubContext<StaffHub> hub)
    {
        _hub = hub;
    }

    /// <summary>
    /// Emits a MenuImportProgress event, throttled to ≤ 1 per 250 ms per jobId.
    /// </summary>
    public async Task EmitProgressAsync(Guid restaurantId, Guid jobId, object payload, CancellationToken ct)
    {
        var now = Environment.TickCount64;
        var threshold = now - (long)ThrottleWindow.TotalMilliseconds;

        // Try to claim the slot: only emit if last emit was > 250 ms ago
        var last = _lastEmitTick.GetOrAdd(jobId, -long.MaxValue);
        if (last > threshold)
            return; // throttled

        // Race-safe update: only update if value is still what we read
        if (!_lastEmitTick.TryUpdate(jobId, now, last))
            return; // another thread won the race

        await _hub.Clients
            .Group(TenantGroups.Role(restaurantId, "admin"))
            .SendAsync("MenuImportProgress", payload, ct);
    }

    /// <summary>
    /// Emits a MenuImportCompleted event. Never throttled.
    /// </summary>
    public async Task EmitCompletedAsync(Guid restaurantId, Guid jobId, object payload, CancellationToken ct)
    {
        await _hub.Clients
            .Group(TenantGroups.Role(restaurantId, "admin"))
            .SendAsync("MenuImportCompleted", payload, ct);
    }

    /// <summary>
    /// Emits a MenuImportFailed event. Never throttled.
    /// </summary>
    public async Task EmitFailedAsync(Guid restaurantId, Guid jobId, object payload, CancellationToken ct)
    {
        await _hub.Clients
            .Group(TenantGroups.Role(restaurantId, "admin"))
            .SendAsync("MenuImportFailed", payload, ct);
    }
}
