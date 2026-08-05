using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantApi.Data;
using RestaurantApi.Models;
using RestaurantApi.Tests.Helpers;
using Testcontainers.PostgreSql;
using Xunit;

namespace RestaurantApi.Tests;

/// <summary>
/// The table-service-session lifecycle: exclusive claims (including the concurrency
/// guarantee the partial unique index exists for), order adoption, the cumulative bill,
/// close/transfer/force-release and their guards.
/// </summary>
public class TableSessionTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
    private MenuImportTestFactory _factory = null!;
    private WaiterWorld _world = null!;
    private HttpClient _anonymous = null!;

    public async Task InitializeAsync()
    {
        await _pg.StartAsync();
        _factory = new MenuImportTestFactory { PostgresConnectionString = _pg.GetConnectionString() };

        using (var scope = _factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();

        _world = await WaiterModeTestFixtures.SeedAsync(_factory);
        _anonymous = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _anonymous.Dispose();
        _world.Dispose();
        _factory.Dispose();
        await _pg.DisposeAsync();
    }

    // ── Local helpers ────────────────────────────────────────────────────────

    private Task<(Guid TableId, Guid QrToken)> AddTableAsync(string number) =>
        WaiterModeTestFixtures.AddTableAsync(_factory, _world.RestaurantId, number);

    private Task<Guid> AddProductAsync(string name, decimal price) =>
        WaiterModeTestFixtures.AddProductAsync(_factory, _world.RestaurantId, name, price);

    private Task WithDbAsync(Func<AppDbContext, Task> action) =>
        WaiterModeTestFixtures.WithDbAsync(_factory, _world.RestaurantId, action);

    private Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> action) =>
        WaiterModeTestFixtures.WithDbAsync(_factory, _world.RestaurantId, action);

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage res)
    {
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private Task<HttpResponseMessage> ClaimAsync(HttpClient client, Guid tableId) =>
        client.PostAsync($"{_world.WaiterUrl}/tables/{tableId}/claim", null);

    private async Task<Guid> ClaimOkAsync(HttpClient client, Guid tableId)
    {
        var res = await ClaimAsync(client, tableId);
        res.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await ReadJsonAsync(res)).GetProperty("id").GetGuid();
    }

    /// <summary>Creates a waiter draft, fills it, and sends it to the kitchen.</summary>
    private async Task<Guid> PlaceWaiterOrderAsync(HttpClient client, Guid tableId, Guid productId, int quantity, string? notes = null)
    {
        var created = await client.PostAsync($"{_world.WaiterUrl}/tables/{tableId}/orders", null);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var orderId = (await ReadJsonAsync(created)).GetProperty("id").GetGuid();

        var patched = await client.PatchAsJsonAsync(
            $"{_world.WaiterUrl}/orders/{orderId}/items",
            new { items = new[] { new { productId, quantity, notes } } });
        patched.StatusCode.Should().Be(HttpStatusCode.OK);

        var submitted = await client.PostAsync($"{_world.WaiterUrl}/orders/{orderId}/submit", null);
        submitted.StatusCode.Should().Be(HttpStatusCode.OK);

        return orderId;
    }

    // ── Claim ────────────────────────────────────────────────────────────────

    /// <summary>
    /// The guarantee the partial unique index exists for. Both waiters fire at once;
    /// exactly one wins, and the loser is told who holds the table.
    /// </summary>
    [Fact]
    public async Task Two_concurrent_claims_on_one_table_yield_exactly_one_session()
    {
        var (tableId, _) = await AddTableAsync("C1");

        var responses = await Task.WhenAll(
            ClaimAsync(_world.WaiterA, tableId),
            ClaimAsync(_world.WaiterB, tableId));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(1);

        var conflict = await ReadJsonAsync(responses.Single(r => r.StatusCode == HttpStatusCode.Conflict));
        var winner = await ReadJsonAsync(responses.Single(r => r.StatusCode == HttpStatusCode.Created));

        // The 409 body names the colleague actually holding the table.
        var ownerName = conflict.GetProperty("session").GetProperty("waiterName").GetString();
        ownerName.Should().BeOneOf(WaiterModeTestFixtures.WaiterAName, WaiterModeTestFixtures.WaiterBName);
        ownerName.Should().Be(winner.GetProperty("waiterName").GetString());
        conflict.GetProperty("message").GetString().Should().Contain(ownerName);

        var open = await WithDbAsync(db => db.TableServiceSessions
            .CountAsync(s => s.TableId == tableId && s.Status == "open"));
        open.Should().Be(1);

        // The loser's outbox row must have rolled back with its failed insert.
        var opened = await WithDbAsync(db => db.OrderEvents
            .CountAsync(e => e.EventType == "session.opened" && e.RestaurantId == _world.RestaurantId));
        opened.Should().Be(1);
    }

    [Fact]
    public async Task Claim_adopts_the_tables_orphan_active_orders()
    {
        var (tableId, _) = await AddTableAsync("C2");
        var productId = await AddProductAsync("Adoptat", 20m);

        // A QR round submitted before anyone took the table.
        var orphanId = Guid.NewGuid();
        await WithDbAsync(async db =>
        {
            db.Orders.Add(new Order
            {
                Id = orphanId,
                RestaurantId = _world.RestaurantId,
                TableId = tableId,
                Status = "submitted",
                Source = "qr",
                TotalAmount = 20m,
                Items = { new OrderItem { RestaurantId = _world.RestaurantId, ProductId = productId, Quantity = 1, UnitPriceAtPurchase = 20m } },
            });
            await db.SaveChangesAsync();
        });

        var sessionId = await ClaimOkAsync(_world.WaiterA, tableId);

        var adopted = await WithDbAsync(db => db.Orders.SingleAsync(o => o.Id == orphanId));
        adopted.TableServiceSessionId.Should().Be(sessionId);

        // …and it counts on the bill straight away.
        var detail = await ReadJsonAsync(await _world.WaiterA.GetAsync($"{_world.WaiterUrl}/sessions/{sessionId}"));
        detail.GetProperty("cumulativeTotal").GetDecimal().Should().Be(20m);
    }

    [Fact]
    public async Task Claim_on_a_foreign_restaurants_table_is_not_found()
    {
        var otherFactoryWorld = await WaiterModeTestFixtures.SeedAsync(_factory);
        try
        {
            var (foreignTableId, _) = await WaiterModeTestFixtures.AddTableAsync(
                _factory, otherFactoryWorld.RestaurantId, "X1");

            // WaiterA's client is scoped to _world.RestaurantId; the tenant filter must
            // hide the other restaurant's table rather than let it be claimed.
            var res = await ClaimAsync(_world.WaiterA, foreignTableId);
            res.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        finally
        {
            otherFactoryWorld.Dispose();
        }
    }

    // ── QR interop ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Qr_submit_attaches_to_an_open_session_and_stays_source_qr()
    {
        var (tableId, _) = await AddTableAsync("C3");
        var productId = await AddProductAsync("Bere", 12m);
        var sessionId = await ClaimOkAsync(_world.WaiterA, tableId);

        var orderId = await SubmitCustomerOrderAsync(tableId, productId, 2);

        var order = await WithDbAsync(db => db.Orders.SingleAsync(o => o.Id == orderId));
        order.TableServiceSessionId.Should().Be(sessionId);
        order.Source.Should().Be("qr");
        order.PlacedByUserId.Should().BeNull();
    }

    [Fact]
    public async Task Qr_submit_on_an_unclaimed_table_leaves_the_session_null()
    {
        var (tableId, _) = await AddTableAsync("C4");
        var productId = await AddProductAsync("Apă", 8m);

        var orderId = await SubmitCustomerOrderAsync(tableId, productId, 1);

        var order = await WithDbAsync(db => db.Orders.SingleAsync(o => o.Id == orderId));
        order.TableServiceSessionId.Should().BeNull();
        order.Source.Should().Be("qr");
    }

    private async Task<Guid> SubmitCustomerOrderAsync(Guid tableId, Guid productId, int quantity)
    {
        var create = await _anonymous.PostAsJsonAsync(
            $"/api/restaurants/{_world.RestaurantId}/orders",
            new { tableId, items = new[] { new { productId, quantity } } });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var orderId = (await ReadJsonAsync(create)).GetProperty("id").GetGuid();

        var submit = await _anonymous.PostAsync(
            $"/api/restaurants/{_world.RestaurantId}/orders/{orderId}/submit", null);
        submit.StatusCode.Should().Be(HttpStatusCode.OK);

        return orderId;
    }

    // ── Totals & close ───────────────────────────────────────────────────────

    [Fact]
    public async Task Cumulative_total_includes_soft_deleted_completed_rounds()
    {
        var (tableId, _) = await AddTableAsync("C5");
        var productId = await AddProductAsync("Fel principal", 30m);
        var sessionId = await ClaimOkAsync(_world.WaiterA, tableId);

        await PlaceWaiterOrderAsync(_world.WaiterA, tableId, productId, 1);   // 30

        // An earlier round that OrderFinalizationService has already archived: completed
        // AND soft-deleted. It must still be on the bill — the guests are still seated.
        await WithDbAsync(async db =>
        {
            var archived = new Order
            {
                Id = Guid.NewGuid(),
                RestaurantId = _world.RestaurantId,
                TableId = tableId,
                TableServiceSessionId = sessionId,
                Status = "completed",
                DeletedAt = DateTime.UtcNow,
                Source = "waiter",
                TotalAmount = 60m,
                Items = { new OrderItem { RestaurantId = _world.RestaurantId, ProductId = productId, Quantity = 2, UnitPriceAtPurchase = 30m } },
            };
            // A cancelled round on the same session must NOT count.
            var cancelled = new Order
            {
                Id = Guid.NewGuid(),
                RestaurantId = _world.RestaurantId,
                TableId = tableId,
                TableServiceSessionId = sessionId,
                Status = "cancelled",
                DeletedAt = DateTime.UtcNow,
                Source = "waiter",
                TotalAmount = 30m,
                Items = { new OrderItem { RestaurantId = _world.RestaurantId, ProductId = productId, Quantity = 1, UnitPriceAtPurchase = 30m } },
            };
            db.Orders.AddRange(archived, cancelled);
            await db.SaveChangesAsync();
        });

        var detail = await ReadJsonAsync(await _world.WaiterA.GetAsync($"{_world.WaiterUrl}/sessions/{sessionId}"));
        detail.GetProperty("cumulativeTotal").GetDecimal().Should().Be(90m);
    }

    [Fact]
    public async Task Close_is_blocked_while_an_order_is_still_pending_dispatch()
    {
        var (tableId, _) = await AddTableAsync("C6");
        var productId = await AddProductAsync("În curs", 15m);
        var sessionId = await ClaimOkAsync(_world.WaiterA, tableId);
        var orderId = await PlaceWaiterOrderAsync(_world.WaiterA, tableId, productId, 1);

        // Simulate the flash-deal hold: the round is committed but not yet with the kitchen.
        await WithDbAsync(db => db.Orders
            .Where(o => o.Id == orderId)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, "pending_dispatch")));

        var res = await CloseAsync(_world.WaiterA, sessionId, "cash");
        res.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var stillOpen = await WithDbAsync(db => db.TableServiceSessions.SingleAsync(s => s.Id == sessionId));
        stillOpen.Status.Should().Be("open");
    }

    [Fact]
    public async Task Close_snapshots_the_total_detaches_drafts_and_rejects_a_second_close()
    {
        var (tableId, _) = await AddTableAsync("C7");
        var productId = await AddProductAsync("Desert", 25m);
        var sessionId = await ClaimOkAsync(_world.WaiterA, tableId);
        await PlaceWaiterOrderAsync(_world.WaiterA, tableId, productId, 2);   // 50

        // A half-built cart the waiter never sent: it must be detached, not cancelled,
        // and must not hold the close hostage.
        var draftRes = await _world.WaiterA.PostAsync($"{_world.WaiterUrl}/tables/{tableId}/orders", null);
        var draftId = (await ReadJsonAsync(draftRes)).GetProperty("id").GetGuid();

        var close = await CloseAsync(_world.WaiterA, sessionId, "cash");
        close.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await ReadJsonAsync(close);
        body.GetProperty("status").GetString().Should().Be("closed");
        body.GetProperty("closedReason").GetString().Should().Be("paid");
        body.GetProperty("paymentMethod").GetString().Should().Be("cash");
        body.GetProperty("totalAmount").GetDecimal().Should().Be(50m);

        var session = await WithDbAsync(db => db.TableServiceSessions.SingleAsync(s => s.Id == sessionId));
        session.TotalAmount.Should().Be(50m);
        session.ClosedByUserId.Should().Be(_world.WaiterAUserId);
        session.ClosedByName.Should().Be(WaiterModeTestFixtures.WaiterAName);
        session.ClosedAt.Should().NotBeNull();

        var draft = await WithDbAsync(db => db.Orders.SingleAsync(o => o.Id == draftId));
        draft.Status.Should().Be("draft");
        draft.TableServiceSessionId.Should().BeNull();

        // Double-tap / second device: the guarded UPDATE matches 0 rows.
        (await CloseAsync(_world.WaiterA, sessionId, "cash")).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Close_mixed_requires_amounts_that_add_up_to_the_bill()
    {
        var (tableId, _) = await AddTableAsync("C8");
        var productId = await AddProductAsync("Mixt", 40m);
        var sessionId = await ClaimOkAsync(_world.WaiterA, tableId);
        await PlaceWaiterOrderAsync(_world.WaiterA, tableId, productId, 1);   // 40

        // Missing amounts.
        (await CloseAsync(_world.WaiterA, sessionId, "mixed"))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Amounts that don't reconcile.
        (await CloseAsync(_world.WaiterA, sessionId, "mixed", cash: 10m, card: 20m))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var ok = await CloseAsync(_world.WaiterA, sessionId, "mixed", cash: 15m, card: 25m);
        ok.StatusCode.Should().Be(HttpStatusCode.OK);

        var session = await WithDbAsync(db => db.TableServiceSessions.SingleAsync(s => s.Id == sessionId));
        session.CashAmount.Should().Be(15m);
        session.CardAmount.Should().Be(25m);
        session.PaymentMethod.Should().Be("mixed");
    }

    [Fact]
    public async Task Close_rejects_an_unknown_payment_method()
    {
        var (tableId, _) = await AddTableAsync("C9");
        var sessionId = await ClaimOkAsync(_world.WaiterA, tableId);

        (await CloseAsync(_world.WaiterA, sessionId, "bitcoin"))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_colleague_cannot_close_someone_elses_table_but_a_manager_can()
    {
        var (tableId, _) = await AddTableAsync("C10");
        var sessionId = await ClaimOkAsync(_world.WaiterA, tableId);

        (await CloseAsync(_world.WaiterB, sessionId, "cash"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await CloseAsync(_world.Manager, sessionId, "cash"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private Task<HttpResponseMessage> CloseAsync(
        HttpClient client, Guid sessionId, string method, decimal? cash = null, decimal? card = null) =>
        client.PostAsJsonAsync(
            $"{_world.WaiterUrl}/sessions/{sessionId}/close",
            new { paymentMethod = method, cashAmount = cash, cardAmount = card });

    // ── Transfer ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Transfer_hands_the_table_over_and_renames_the_owner()
    {
        var (tableId, _) = await AddTableAsync("C11");
        var sessionId = await ClaimOkAsync(_world.WaiterA, tableId);

        var res = await _world.WaiterA.PostAsJsonAsync(
            $"{_world.WaiterUrl}/sessions/{sessionId}/transfer",
            new { toUserId = _world.WaiterBUserId });

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ReadJsonAsync(res);
        body.GetProperty("waiterUserId").GetGuid().Should().Be(_world.WaiterBUserId);
        body.GetProperty("waiterName").GetString().Should().Be(WaiterModeTestFixtures.WaiterBName);

        // B now owns it; A does not.
        var mine = await ReadJsonAsync(await _world.WaiterB.GetAsync($"{_world.WaiterUrl}/my-sessions"));
        mine.EnumerateArray().Select(s => s.GetProperty("id").GetGuid()).Should().Contain(sessionId);
    }

    [Fact]
    public async Task Transfer_to_a_non_member_is_rejected()
    {
        var (tableId, _) = await AddTableAsync("C12");
        var sessionId = await ClaimOkAsync(_world.WaiterA, tableId);

        var res = await _world.WaiterA.PostAsJsonAsync(
            $"{_world.WaiterUrl}/sessions/{sessionId}/transfer",
            new { toUserId = Guid.NewGuid() });

        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Transfer_after_close_conflicts()
    {
        var (tableId, _) = await AddTableAsync("C13");
        var sessionId = await ClaimOkAsync(_world.WaiterA, tableId);
        (await CloseAsync(_world.WaiterA, sessionId, "card")).StatusCode.Should().Be(HttpStatusCode.OK);

        var res = await _world.WaiterA.PostAsJsonAsync(
            $"{_world.WaiterUrl}/sessions/{sessionId}/transfer",
            new { toUserId = _world.WaiterBUserId });

        res.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ── Force-release ────────────────────────────────────────────────────────

    [Fact]
    public async Task Force_release_is_manager_only_and_records_why()
    {
        var (tableId, _) = await AddTableAsync("C14");
        var sessionId = await ClaimOkAsync(_world.WaiterA, tableId);

        // Not in the waiter allow-list — the middleware stops it before the action runs.
        var waiterAttempt = await _world.WaiterA.PostAsync(
            $"{_world.WaiterUrl}/sessions/{sessionId}/force-release", null);
        waiterAttempt.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var managerRes = await _world.Manager.PostAsync(
            $"{_world.WaiterUrl}/sessions/{sessionId}/force-release", null);
        managerRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var session = await WithDbAsync(db => db.TableServiceSessions.SingleAsync(s => s.Id == sessionId));
        session.Status.Should().Be("closed");
        session.ClosedReason.Should().Be("force_released");
        session.PaymentMethod.Should().BeNull();
        session.ClosedByUserId.Should().Be(_world.ManagerUserId);

        // The table is free again — a fresh claim succeeds.
        (await ClaimAsync(_world.WaiterB, tableId)).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    // ── Floor ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Floor_reports_free_and_claimed_tables_with_their_bills()
    {
        var (claimedId, _) = await AddTableAsync("C15");
        var (freeId, _) = await AddTableAsync("C16");
        var productId = await AddProductAsync("Pe masă", 11m);

        await ClaimOkAsync(_world.WaiterA, claimedId);
        await PlaceWaiterOrderAsync(_world.WaiterA, claimedId, productId, 3);   // 33

        var floor = await ReadJsonAsync(await _world.WaiterA.GetAsync($"{_world.WaiterUrl}/floor"));
        var tables = floor.GetProperty("tables").EnumerateArray().ToList();

        var claimed = tables.Single(t => t.GetProperty("id").GetGuid() == claimedId);
        claimed.GetProperty("session").ValueKind.Should().NotBe(JsonValueKind.Null);
        claimed.GetProperty("session").GetProperty("cumulativeTotal").GetDecimal().Should().Be(33m);
        claimed.GetProperty("activeOrderCount").GetInt32().Should().Be(1);
        claimed.GetProperty("hasOpenHelpRequest").GetBoolean().Should().BeFalse();

        var free = tables.Single(t => t.GetProperty("id").GetGuid() == freeId);
        free.GetProperty("session").ValueKind.Should().Be(JsonValueKind.Null);
        free.GetProperty("activeOrderCount").GetInt32().Should().Be(0);
    }
}
