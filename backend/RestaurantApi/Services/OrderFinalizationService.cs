using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using RestaurantApi.Data;
using RestaurantApi.Hubs;
using RestaurantApi.Models;
using RestaurantApi.Tenancy;

namespace RestaurantApi.Services;

/// <summary>
/// Polls for orders left in the "ready" column past the grace period and soft-deletes
/// them. This is what powers the "drag to Finalizată → auto-archive after ~45s, but
/// the admin can drag it back during the window" UX. Polling (not in-memory timers)
/// so a restart can't strand an order in the kanban.
/// </summary>
public sealed class OrderFinalizationService : BackgroundService
{
    private static readonly TimeSpan GracePeriod = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private readonly IServiceProvider _services;
    private readonly ILogger<OrderFinalizationService> _logger;

    public OrderFinalizationService(IServiceProvider services, ILogger<OrderFinalizationService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceForTestAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OrderFinalizationService poll iteration failed");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Exposed for unit-testing: runs exactly one poll iteration synchronously
    /// (mirrors MenuImportBackgroundService.RunOnceForTestAsync).
    /// </summary>
    public Task RunOnceForTestAsync(CancellationToken ct) => FinalizeDueOrdersAsync(ct);

    private async Task FinalizeDueOrdersAsync(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
        var events = scope.ServiceProvider.GetRequiredService<IOrderEventWriter>();
        var customerHub = scope.ServiceProvider.GetRequiredService<IHubContext<CustomerHub>>();
        var staffHub = scope.ServiceProvider.GetRequiredService<IHubContext<StaffHub>>();

        // Cross-tenant work — the background service runs without a request, so
        // the tenant filter would otherwise yield zero rows.
        using var _ = tenant.SuppressResolution();

        var cutoff = DateTime.UtcNow - GracePeriod;

        var due = await db.Orders
            .Where(o => o.Status == "ready"
                        && o.DeletedAt == null
                        && o.ReadyAt != null
                        && o.ReadyAt <= cutoff)
            .ToListAsync(ct);

        if (due.Count == 0) return;

        var now = DateTime.UtcNow;
        foreach (var order in due)
        {
            order.Status = "completed";
            order.DeletedAt = now;

            // Archiving is the order's terminal state and what Logistic books as revenue.
            // Enqueued into the same context so the batch's events and archive writes
            // commit in the single SaveChangesAsync below — a failed poll iteration
            // re-finds these orders next tick and emits exactly once then.
            events.Enqueue(
                db,
                OrderEventTypes.OrderCompleted,
                order.RestaurantId,
                order.Id,
                OrderEventPayloads.BuildOrderCompletedPayload(order));
        }
        await db.SaveChangesAsync(ct);

        foreach (var order in due)
        {
            await customerHub.Clients
                .Group(TenantGroups.Table(order.RestaurantId, order.TableId))
                .SendAsync("OrderStatusUpdated", new
                {
                    orderId = order.Id,
                    status = order.Status,
                }, ct);

            var finalizedPayload = new
            {
                orderId = order.Id,
                orderNumber = order.OrderNumber,
                tableId = order.TableId,
            };
            await staffHub.Clients
                .Group(TenantGroups.Role(order.RestaurantId, "admin"))
                .SendAsync("OrderFinalized", finalizedPayload, ct);
            await staffHub.Clients
                .Group(TenantGroups.Role(order.RestaurantId, "WaitStaff"))
                .SendAsync("OrderFinalized", finalizedPayload, ct);
        }

        _logger.LogInformation("OrderFinalizationService archived {Count} order(s)", due.Count);
    }
}
