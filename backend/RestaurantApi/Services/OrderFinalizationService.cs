using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using RestaurantApi.Data;
using RestaurantApi.Hubs;
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
                await FinalizeDueOrdersAsync(stoppingToken);
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

    private async Task FinalizeDueOrdersAsync(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
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

            // Parked orders: the customer joined the ORIGINAL table's group, so mirror the
            // status broadcast there or their live timeline goes silent while it's parked.
            if (order.MovedFromTableId is Guid homeTableId)
                await customerHub.Clients
                    .Group(TenantGroups.Table(order.RestaurantId, homeTableId))
                    .SendAsync("OrderStatusUpdated", new
                    {
                        orderId = order.Id,
                        status = order.Status,
                    }, ct);

            await staffHub.Clients
                .Group(TenantGroups.Role(order.RestaurantId, "admin"))
                .SendAsync("OrderFinalized", new
                {
                    orderId = order.Id,
                    orderNumber = order.OrderNumber,
                    tableId = order.TableId,
                }, ct);
        }

        _logger.LogInformation("OrderFinalizationService archived {Count} order(s)", due.Count);
    }
}
