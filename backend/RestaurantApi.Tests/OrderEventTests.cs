using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantApi.Data;
using RestaurantApi.Models;
using RestaurantApi.Services;
using RestaurantApi.Tests.Helpers;
using Testcontainers.PostgreSql;
using Xunit;

namespace RestaurantApi.Tests;

/// <summary>
/// The order_events outbox: that every domain change emits exactly one row, that the
/// payloads carry the frozen camelCase keys the Logistic consumer parses, and — the whole
/// reason the writer never saves — that an event cannot survive a failed write.
/// </summary>
public class OrderEventTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
    private MenuImportTestFactory _factory = null!;
    private WaiterWorld _world = null!;

    public async Task InitializeAsync()
    {
        await _pg.StartAsync();
        _factory = new MenuImportTestFactory { PostgresConnectionString = _pg.GetConnectionString() };

        using (var scope = _factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();

        _world = await WaiterModeTestFixtures.SeedAsync(_factory);
    }

    public async Task DisposeAsync()
    {
        _world.Dispose();
        _factory.Dispose();
        await _pg.DisposeAsync();
    }

    // ── Local helpers ────────────────────────────────────────────────────────

    private Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> action) =>
        WaiterModeTestFixtures.WithDbAsync(_factory, _world.RestaurantId, action);

    private Task WithDbAsync(Func<AppDbContext, Task> action) =>
        WaiterModeTestFixtures.WithDbAsync(_factory, _world.RestaurantId, action);

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage res)
    {
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    /// <summary>Every event of a type for this restaurant, oldest first, payload parsed.</summary>
    private async Task<List<JsonElement>> EventsOfTypeAsync(string eventType)
    {
        var payloads = await WithDbAsync(db => db.OrderEvents
            .Where(e => e.RestaurantId == _world.RestaurantId && e.EventType == eventType)
            .OrderBy(e => e.Id)
            .Select(e => e.Payload)
            .ToListAsync());

        return payloads.Select(p => JsonDocument.Parse(p).RootElement.Clone()).ToList();
    }

    private static void ShouldHaveExactKeys(JsonElement payload, params string[] expected)
    {
        payload.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(expected);
    }

    private async Task<(Guid TableId, Guid SessionId, Guid ProductId)> ClaimedTableAsync(string number, decimal price = 20m)
    {
        var (tableId, _) = await WaiterModeTestFixtures.AddTableAsync(_factory, _world.RestaurantId, number);
        var productId = await WaiterModeTestFixtures.AddProductAsync(_factory, _world.RestaurantId, "Produs " + number, price);

        var claim = await _world.WaiterA.PostAsync($"{_world.WaiterUrl}/tables/{tableId}/claim", null);
        claim.StatusCode.Should().Be(HttpStatusCode.Created);
        var sessionId = (await ReadJsonAsync(claim)).GetProperty("id").GetGuid();

        return (tableId, sessionId, productId);
    }

    private async Task<Guid> PlaceAndSubmitAsync(Guid tableId, Guid productId, int quantity, string? notes = null)
    {
        var created = await _world.WaiterA.PostAsync($"{_world.WaiterUrl}/tables/{tableId}/orders", null);
        var orderId = (await ReadJsonAsync(created)).GetProperty("id").GetGuid();

        (await _world.WaiterA.PatchAsJsonAsync(
            $"{_world.WaiterUrl}/orders/{orderId}/items",
            new { items = new[] { new { productId, quantity, notes } } }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await _world.WaiterA.PostAsync($"{_world.WaiterUrl}/orders/{orderId}/submit", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        return orderId;
    }

    // ── The atomicity contract ───────────────────────────────────────────────

    /// <summary>
    /// The writer only attaches the row; the caller's SaveChanges persists it. So when
    /// that save fails, the event must go down with it — otherwise Logistic would
    /// fiscalize a write that never happened.
    /// </summary>
    [Fact]
    public async Task A_failed_save_leaves_no_event_behind()
    {
        var before = await WithDbAsync(db => db.OrderEvents.CountAsync());

        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<RestaurantApi.Tenancy.ICurrentTenant>()
            .SetRestaurant(_world.RestaurantId);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var writer = scope.ServiceProvider.GetRequiredService<IOrderEventWriter>();

        // An order pointing at a table that does not exist: the FK rejects the whole
        // SaveChanges, event row included.
        db.Orders.Add(new Order
        {
            RestaurantId = _world.RestaurantId,
            TableId = Guid.NewGuid(),
            Status = "draft",
        });
        writer.Enqueue(db, OrderEventTypes.OrderDispatched, _world.RestaurantId, Guid.NewGuid(), new { orderId = Guid.NewGuid() });

        var act = async () => await db.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();

        var after = await WithDbAsync(freshDb => freshDb.OrderEvents.CountAsync());
        after.Should().Be(before);
    }

    /// <summary>RestaurantApi is a writer only: the consumer owns processed_at and the lease columns.</summary>
    [Fact]
    public async Task Emitted_rows_never_carry_consumer_owned_columns()
    {
        var (tableId, _, productId) = await ClaimedTableAsync("E0");
        await PlaceAndSubmitAsync(tableId, productId, 1);

        var rows = await WithDbAsync(db => db.OrderEvents
            .Where(e => e.RestaurantId == _world.RestaurantId)
            .ToListAsync());

        rows.Should().NotBeEmpty();
        rows.Should().OnlyContain(e =>
            e.ProcessedAt == null && e.LeaseId == null && e.LeaseExpiresAt == null && e.Attempts == 0);
        rows.Should().OnlyContain(e => e.CreatedAt != default);
    }

    // ── Order-side events ────────────────────────────────────────────────────

    [Fact]
    public async Task Dispatch_emits_one_order_dispatched_with_the_frozen_payload()
    {
        var (tableId, sessionId, productId) = await ClaimedTableAsync("E1", price: 12.50m);
        var orderId = await PlaceAndSubmitAsync(tableId, productId, 2, notes: "fără gheață");

        var events = await EventsOfTypeAsync(OrderEventTypes.OrderDispatched);
        events.Should().HaveCount(1);

        var payload = events[0];
        ShouldHaveExactKeys(payload,
            "orderId", "orderNumber", "tableId", "tableNumber", "tableServiceSessionId",
            "source", "placedByUserId", "totalAmount", "items", "occurredAt");

        payload.GetProperty("orderId").GetGuid().Should().Be(orderId);
        payload.GetProperty("tableId").GetGuid().Should().Be(tableId);
        payload.GetProperty("tableNumber").GetString().Should().Be("E1");
        payload.GetProperty("tableServiceSessionId").GetGuid().Should().Be(sessionId);
        payload.GetProperty("source").GetString().Should().Be("waiter");
        payload.GetProperty("placedByUserId").GetGuid().Should().Be(_world.WaiterAUserId);
        payload.GetProperty("orderNumber").GetInt32().Should().BeGreaterThan(0);
        payload.GetProperty("totalAmount").GetDecimal().Should().Be(25m);
        payload.GetProperty("occurredAt").GetDateTime().Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(5));

        var item = payload.GetProperty("items").EnumerateArray().Single();
        ShouldHaveExactKeys(item, "productId", "productName", "quantity", "unitPrice", "notes");
        item.GetProperty("productId").GetGuid().Should().Be(productId);
        item.GetProperty("quantity").GetInt32().Should().Be(2);
        item.GetProperty("unitPrice").GetDecimal().Should().Be(12.50m);
        item.GetProperty("notes").GetString().Should().Be("fără gheață");
    }

    [Fact]
    public async Task Waiter_cancel_emits_order_cancelled_once()
    {
        var (tableId, _, productId) = await ClaimedTableAsync("E2");
        var orderId = await PlaceAndSubmitAsync(tableId, productId, 1);

        (await _world.WaiterA.PostAsync($"{_world.WaiterUrl}/orders/{orderId}/cancel", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var events = await EventsOfTypeAsync(OrderEventTypes.OrderCancelled);
        events.Should().HaveCount(1);
        events[0].GetProperty("orderId").GetGuid().Should().Be(orderId);
        ShouldHaveExactKeys(events[0],
            "orderId", "orderNumber", "tableId", "tableNumber", "tableServiceSessionId",
            "source", "placedByUserId", "totalAmount", "items", "occurredAt");
    }

    [Fact]
    public async Task Cancelling_a_draft_emits_nothing()
    {
        var (tableId, _, _) = await ClaimedTableAsync("E3");

        var created = await _world.WaiterA.PostAsync($"{_world.WaiterUrl}/tables/{tableId}/orders", null);
        var draftId = (await ReadJsonAsync(created)).GetProperty("id").GetGuid();

        (await _world.WaiterA.PostAsync($"{_world.WaiterUrl}/orders/{draftId}/cancel", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // A draft never reached the kitchen, so there is nothing for Logistic to reverse.
        var events = await EventsOfTypeAsync(OrderEventTypes.OrderCancelled);
        events.Should().NotContain(e => e.GetProperty("orderId").GetGuid() == draftId);

        var gone = await WithDbAsync(db => db.Orders.AnyAsync(o => o.Id == draftId));
        gone.Should().BeFalse();
    }

    [Fact]
    public async Task Admin_cancel_emits_order_cancelled()
    {
        var (tableId, _, productId) = await ClaimedTableAsync("E4");
        var orderId = await PlaceAndSubmitAsync(tableId, productId, 1);

        (await _world.Manager.PostAsync(
            $"/api/restaurants/{_world.RestaurantId}/admin/orders/{orderId}/cancel", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var events = await EventsOfTypeAsync(OrderEventTypes.OrderCancelled);
        events.Should().ContainSingle(e => e.GetProperty("orderId").GetGuid() == orderId);
    }

    [Fact]
    public async Task Archiving_a_ready_order_emits_order_completed()
    {
        var (tableId, sessionId, productId) = await ClaimedTableAsync("E5", price: 40m);
        var orderId = await PlaceAndSubmitAsync(tableId, productId, 1);

        // Park it in "ready" with an expired grace period so one poll iteration archives it.
        await WithDbAsync(db => db.Orders
            .Where(o => o.Id == orderId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, "ready")
                .SetProperty(o => o.ReadyAt, DateTime.UtcNow.AddMinutes(-5))));

        // The hosted service is stripped from the test host, so drive one poll directly.
        var finalizer = ActivatorUtilities.CreateInstance<OrderFinalizationService>(_factory.Services);
        await finalizer.RunOnceForTestAsync(CancellationToken.None);

        var events = await EventsOfTypeAsync(OrderEventTypes.OrderCompleted);
        var payload = events.Single(e => e.GetProperty("orderId").GetGuid() == orderId);

        ShouldHaveExactKeys(payload,
            "orderId", "orderNumber", "tableId", "tableServiceSessionId", "totalAmount", "occurredAt");
        payload.GetProperty("tableServiceSessionId").GetGuid().Should().Be(sessionId);
        payload.GetProperty("totalAmount").GetDecimal().Should().Be(40m);

        var archived = await WithDbAsync(db => db.Orders.SingleAsync(o => o.Id == orderId));
        archived.Status.Should().Be("completed");
        archived.DeletedAt.Should().NotBeNull();
    }

    // ── Session-side events ──────────────────────────────────────────────────

    [Fact]
    public async Task Claim_transfer_and_close_emit_their_frozen_payloads()
    {
        var (tableId, sessionId, productId) = await ClaimedTableAsync("E6", price: 15m);
        await PlaceAndSubmitAsync(tableId, productId, 2);   // 30

        var opened = (await EventsOfTypeAsync(OrderEventTypes.SessionOpened))
            .Single(e => e.GetProperty("sessionId").GetGuid() == sessionId);
        ShouldHaveExactKeys(opened,
            "sessionId", "tableId", "tableNumber", "waiterUserId", "waiterName", "openedAt");
        opened.GetProperty("waiterUserId").GetGuid().Should().Be(_world.WaiterAUserId);
        opened.GetProperty("waiterName").GetString().Should().Be(WaiterModeTestFixtures.WaiterAName);

        (await _world.WaiterA.PostAsJsonAsync(
            $"{_world.WaiterUrl}/sessions/{sessionId}/transfer",
            new { toUserId = _world.WaiterBUserId })).StatusCode.Should().Be(HttpStatusCode.OK);

        var transferred = (await EventsOfTypeAsync(OrderEventTypes.SessionTransferred))
            .Single(e => e.GetProperty("sessionId").GetGuid() == sessionId);
        ShouldHaveExactKeys(transferred,
            "sessionId", "tableId", "fromUserId", "toUserId", "toName", "occurredAt");
        transferred.GetProperty("fromUserId").GetGuid().Should().Be(_world.WaiterAUserId);
        transferred.GetProperty("toUserId").GetGuid().Should().Be(_world.WaiterBUserId);
        transferred.GetProperty("toName").GetString().Should().Be(WaiterModeTestFixtures.WaiterBName);

        (await _world.WaiterB.PostAsJsonAsync(
            $"{_world.WaiterUrl}/sessions/{sessionId}/close",
            new { paymentMethod = "card" })).StatusCode.Should().Be(HttpStatusCode.OK);

        var closed = (await EventsOfTypeAsync(OrderEventTypes.SessionClosed))
            .Single(e => e.GetProperty("sessionId").GetGuid() == sessionId);
        ShouldHaveExactKeys(closed,
            "sessionId", "tableId", "tableNumber", "waiterUserId", "waiterName", "paymentMethod",
            "cashAmount", "cardAmount", "totalAmount", "closedReason", "orderIds", "closedAt");
        closed.GetProperty("paymentMethod").GetString().Should().Be("card");
        closed.GetProperty("closedReason").GetString().Should().Be("paid");
        closed.GetProperty("totalAmount").GetDecimal().Should().Be(30m);
        closed.GetProperty("orderIds").GetArrayLength().Should().Be(1);
    }

    /// <summary>
    /// Force-release still closes the session — Logistic sees the event but must not cut a
    /// receipt for it, which is exactly what the null paymentMethod + reason encode.
    /// </summary>
    [Fact]
    public async Task Force_release_emits_session_closed_with_no_payment()
    {
        var (_, sessionId, _) = await ClaimedTableAsync("E7");

        (await _world.Manager.PostAsync($"{_world.WaiterUrl}/sessions/{sessionId}/force-release", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var closed = (await EventsOfTypeAsync(OrderEventTypes.SessionClosed))
            .Single(e => e.GetProperty("sessionId").GetGuid() == sessionId);

        closed.GetProperty("paymentMethod").ValueKind.Should().Be(JsonValueKind.Null);
        closed.GetProperty("cashAmount").ValueKind.Should().Be(JsonValueKind.Null);
        closed.GetProperty("cardAmount").ValueKind.Should().Be(JsonValueKind.Null);
        closed.GetProperty("closedReason").GetString().Should().Be("force_released");
    }
}
