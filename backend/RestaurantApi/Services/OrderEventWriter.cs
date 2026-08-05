using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantApi.Data;
using RestaurantApi.Models;

namespace RestaurantApi.Services;

/// <summary>
/// Writes rows into the <c>order_events</c> outbox that the separate LogisticApi consumes.
///
/// The whole point is atomicity: <see cref="Enqueue"/> only ATTACHES the row to the caller's
/// <see cref="AppDbContext"/> — the caller's own <c>SaveChangesAsync</c> (and whatever
/// transaction it runs in) is what persists it. So the event and the domain change it
/// describes commit together or not at all: a rolled-back dispatch leaves no
/// "order.dispatched" behind, and a committed one is never silently unannounced.
/// The writer therefore never saves, and never opens a transaction of its own.
/// </summary>
public interface IOrderEventWriter
{
    /// <summary>
    /// Adds an outbox row to <paramref name="db"/>'s change tracker. Call it before the
    /// SaveChangesAsync that persists the domain change.
    /// </summary>
    void Enqueue(AppDbContext db, string eventType, Guid restaurantId, Guid entityId, object payload);
}

public class OrderEventWriter : IOrderEventWriter
{
    // camelCase to match the wire contract the Logistic consumer parses (and the same
    // policy MVC and SignalR are configured with). Held as a static single instance
    // because JsonSerializerOptions caches its metadata per instance.
    private static readonly JsonSerializerOptions PayloadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public void Enqueue(AppDbContext db, string eventType, Guid restaurantId, Guid entityId, object payload)
    {
        db.OrderEvents.Add(new OrderEvent
        {
            RestaurantId = restaurantId,
            EventType = eventType,
            EntityId = entityId,
            Payload = JsonSerializer.Serialize(payload, PayloadOptions),
            CreatedAt = DateTime.UtcNow,
        });
    }
}

/// <summary>
/// Builds the frozen outbox payload shapes. Kept here (rather than inline at each emission
/// site) so <c>order.dispatched</c> and <c>order.cancelled</c> — which the contract defines
/// as the same shape — can never drift apart.
/// </summary>
public static class OrderEventPayloads
{
    /// <summary>
    /// The full order snapshot shared by <c>order.dispatched</c> and <c>order.cancelled</c>.
    /// Product names are resolved by join at emission time so the consumer gets a
    /// human-readable receipt line even after the product is later deleted.
    /// </summary>
    public static async Task<object> BuildOrderPayloadAsync(
        AppDbContext db, Order order, CancellationToken ct = default)
    {
        var tableNumber = await db.Tables
            .Where(t => t.Id == order.TableId)
            .Select(t => t.TableNumber)
            .FirstOrDefaultAsync(ct);

        var items = await db.OrderItems
            .Where(i => i.OrderId == order.Id)
            .Select(i => new { i.ProductId, i.Quantity, i.UnitPriceAtPurchase, i.Notes })
            .ToListAsync(ct);

        var productIds = items.Select(i => i.ProductId).Distinct().ToList();
        var namesById = await db.Products
            .Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Name })
            .ToDictionaryAsync(p => p.Id, p => p.Name, ct);

        return new
        {
            orderId = order.Id,
            orderNumber = order.OrderNumber,
            tableId = order.TableId,
            tableNumber,
            tableServiceSessionId = order.TableServiceSessionId,
            source = order.Source,
            placedByUserId = order.PlacedByUserId,
            totalAmount = order.TotalAmount,
            items = items.Select(i => new
            {
                productId = i.ProductId,
                // Matches the "Produs șters" fallback the admin order log already uses.
                productName = namesById.GetValueOrDefault(i.ProductId) ?? "Produs șters",
                quantity = i.Quantity,
                unitPrice = i.UnitPriceAtPurchase,
                notes = i.Notes,
            }).ToList(),
            occurredAt = DateTime.UtcNow,
        };
    }

    /// <summary>The slimmer <c>order.completed</c> shape — the archive write carries no line detail.</summary>
    public static object BuildOrderCompletedPayload(Order order) => new
    {
        orderId = order.Id,
        orderNumber = order.OrderNumber,
        tableId = order.TableId,
        tableServiceSessionId = order.TableServiceSessionId,
        totalAmount = order.TotalAmount,
        occurredAt = DateTime.UtcNow,
    };
}
