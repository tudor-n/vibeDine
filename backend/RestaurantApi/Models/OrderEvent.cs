namespace RestaurantApi.Models;

/// <summary>
/// Transactional outbox row. RestaurantApi INSERTs one of these in the *same*
/// SaveChangesAsync as the domain change it describes (an order dispatched, an order
/// cancelled, a session closed…), so an event can never exist for a write that rolled
/// back, nor a write go unannounced.
///
/// The downstream consumer is the separate "vibeDine Logistic" service, which reads
/// across restaurants (valuation, reporting, fiscal receipts on <c>session.closed</c>)
/// and exclusively owns <see cref="ProcessedAt"/>, <see cref="Attempts"/>,
/// <see cref="LeaseId"/> and <see cref="LeaseExpiresAt"/>. RestaurantApi never updates
/// those columns and never deletes a row.
///
/// Deliberately NOT <see cref="ITenantScoped"/>: it carries <see cref="RestaurantId"/>
/// as a plain column (+ index) but gets no global query filter, because the consumer
/// polls it without a tenant in scope.
/// </summary>
public class OrderEvent
{
    /// <summary>Identity bigint — the consumer polls in insertion order.</summary>
    public long Id { get; set; }

    public Guid RestaurantId { get; set; }

    /// <summary>"order.dispatched" | "order.cancelled" | "order.completed" | "session.opened" | "session.transferred" | "session.closed".</summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>The order id or session id the event is about.</summary>
    public Guid EntityId { get; set; }

    /// <summary>camelCase JSON, stored as jsonb. Schemas are frozen in the waiter-mode contract.</summary>
    public string Payload { get; set; } = "{}";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // ── Consumer-owned columns (LogisticApi writes these; RestaurantApi never does) ──
    public DateTime? ProcessedAt { get; set; }

    public int Attempts { get; set; }

    public Guid? LeaseId { get; set; }

    public DateTime? LeaseExpiresAt { get; set; }
}

/// <summary>The frozen <c>event_type</c> literals. Compared as exact strings, like order statuses.</summary>
public static class OrderEventTypes
{
    public const string OrderDispatched = "order.dispatched";
    public const string OrderCancelled = "order.cancelled";
    public const string OrderCompleted = "order.completed";
    public const string SessionOpened = "session.opened";
    public const string SessionTransferred = "session.transferred";
    public const string SessionClosed = "session.closed";
}
