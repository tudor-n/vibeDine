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
/// The waiter-facing HTTP surface: the allow-list a Waiter-role token is bounded by,
/// the order lifecycle (draft → items → dispatch → cancel) including its input caps,
/// and the guarantee that stock still moves exactly once on the waiter path.
/// </summary>
public class WaiterEndpointTests : IAsyncLifetime
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

    private Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> action) =>
        WaiterModeTestFixtures.WithDbAsync(_factory, _world.RestaurantId, action);

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage res)
    {
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private async Task<(Guid TableId, Guid SessionId)> ClaimAsync(string number)
    {
        var (tableId, _) = await WaiterModeTestFixtures.AddTableAsync(_factory, _world.RestaurantId, number);
        var res = await _world.WaiterA.PostAsync($"{_world.WaiterUrl}/tables/{tableId}/claim", null);
        res.StatusCode.Should().Be(HttpStatusCode.Created);
        return (tableId, (await ReadJsonAsync(res)).GetProperty("id").GetGuid());
    }

    private Task<HttpResponseMessage> CreateDraftAsync(HttpClient client, Guid tableId) =>
        client.PostAsync($"{_world.WaiterUrl}/tables/{tableId}/orders", null);

    private Task<HttpResponseMessage> PatchItemsAsync(HttpClient client, Guid orderId, object items) =>
        client.PatchAsJsonAsync($"{_world.WaiterUrl}/orders/{orderId}/items", new { items });

    // ── Allow-list ───────────────────────────────────────────────────────────

    /// <summary>
    /// A Waiter-role token must pass every allow-listed route. These are asserted as
    /// "not 403": the point is the middleware gate, not each endpoint's own outcome.
    /// </summary>
    [Fact]
    public async Task A_waiter_token_passes_every_allow_listed_route()
    {
        var (tableId, sessionId) = await ClaimAsync("A1");
        var draftId = (await ReadJsonAsync(await CreateDraftAsync(_world.WaiterA, tableId))).GetProperty("id").GetGuid();

        var calls = new (string Label, Func<Task<HttpResponseMessage>> Call)[]
        {
            ("GET /floor",              () => _world.WaiterA.GetAsync($"{_world.WaiterUrl}/floor")),
            ("GET /my-sessions",        () => _world.WaiterA.GetAsync($"{_world.WaiterUrl}/my-sessions")),
            ("GET /staff",              () => _world.WaiterA.GetAsync($"{_world.WaiterUrl}/staff")),
            ("GET /sessions/{id}",      () => _world.WaiterA.GetAsync($"{_world.WaiterUrl}/sessions/{sessionId}")),
            ("PATCH /orders/{id}/items",() => PatchItemsAsync(_world.WaiterA, draftId, Array.Empty<object>())),
            ("POST /orders/{id}/cancel",() => _world.WaiterA.PostAsync($"{_world.WaiterUrl}/orders/{draftId}/cancel", null)),
            ("POST /sessions/{id}/transfer", () => _world.WaiterA.PostAsJsonAsync(
                $"{_world.WaiterUrl}/sessions/{sessionId}/transfer", new { toUserId = _world.WaiterBUserId })),
        };

        foreach (var (label, call) in calls)
        {
            var res = await call();
            res.StatusCode.Should().NotBe(HttpStatusCode.Forbidden, $"{label} is allow-listed for waiters");
        }
    }

    [Fact]
    public async Task A_waiter_token_is_still_blocked_outside_the_allow_list()
    {
        var (_, sessionId) = await ClaimAsync("A2");

        // Force-release is deliberately absent from the allow-list.
        (await _world.WaiterA.PostAsync($"{_world.WaiterUrl}/sessions/{sessionId}/force-release", null))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // And the pre-existing manager-only surface is untouched by this feature.
        (await _world.WaiterA.GetAsync($"/api/restaurants/{_world.RestaurantId}/admin/metrics"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // A manager passes both.
        (await _world.Manager.GetAsync($"/api/restaurants/{_world.RestaurantId}/admin/metrics"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await _world.Manager.PostAsync($"{_world.WaiterUrl}/sessions/{sessionId}/force-release", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Staff_lists_active_floor_staff_of_this_restaurant()
    {
        var staff = await ReadJsonAsync(await _world.WaiterA.GetAsync($"{_world.WaiterUrl}/staff"));
        var ids = staff.EnumerateArray().Select(s => s.GetProperty("userId").GetGuid()).ToList();

        ids.Should().Contain(new[] { _world.WaiterAUserId, _world.WaiterBUserId, _world.ManagerUserId });
        staff.EnumerateArray().Should().OnlyContain(s => s.GetProperty("fullName").GetString() != "");
    }

    // ── Order lifecycle ──────────────────────────────────────────────────────

    [Fact]
    public async Task Creating_an_order_requires_an_open_session_owned_by_the_caller()
    {
        var (tableId, _) = await WaiterModeTestFixtures.AddTableAsync(_factory, _world.RestaurantId, "A3");

        // Unclaimed table.
        (await CreateDraftAsync(_world.WaiterA, tableId)).StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await _world.WaiterA.PostAsync($"{_world.WaiterUrl}/tables/{tableId}/claim", null))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        // A colleague may not order onto someone else's table…
        (await CreateDraftAsync(_world.WaiterB, tableId)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        // …but the owner and a manager may.
        (await CreateDraftAsync(_world.WaiterA, tableId)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await CreateDraftAsync(_world.Manager, tableId)).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_new_waiter_order_is_stamped_as_waiter_sourced()
    {
        var (tableId, sessionId) = await ClaimAsync("A4");

        var res = await CreateDraftAsync(_world.WaiterA, tableId);
        var body = await ReadJsonAsync(res);
        body.GetProperty("source").GetString().Should().Be("waiter");
        body.GetProperty("status").GetString().Should().Be("draft");
        body.GetProperty("placedByUserId").GetGuid().Should().Be(_world.WaiterAUserId);
        body.GetProperty("orderNumber").ValueKind.Should().Be(JsonValueKind.Null);

        var order = await WithDbAsync(db => db.Orders.SingleAsync(o => o.Id == body.GetProperty("id").GetGuid()));
        order.TableServiceSessionId.Should().Be(sessionId);
        order.GuestId.Should().BeNull();
        order.DeviceSessionId.Should().BeNull();
    }

    [Fact]
    public async Task Item_sync_is_full_replace_and_carries_kitchen_notes()
    {
        var (tableId, _) = await ClaimAsync("A5");
        var soup = await WaiterModeTestFixtures.AddProductAsync(_factory, _world.RestaurantId, "Supă", 18m);
        var steak = await WaiterModeTestFixtures.AddProductAsync(_factory, _world.RestaurantId, "Friptură", 55m);

        var draftId = (await ReadJsonAsync(await CreateDraftAsync(_world.WaiterA, tableId))).GetProperty("id").GetGuid();

        var first = await PatchItemsAsync(_world.WaiterA, draftId, new[]
        {
            new { productId = soup, quantity = 2, notes = (string?)"fără smântână" },
            new { productId = steak, quantity = 1, notes = (string?)null },
        });
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstBody = await ReadJsonAsync(first);
        firstBody.GetProperty("totalAmount").GetDecimal().Should().Be(91m);
        firstBody.GetProperty("items").GetArrayLength().Should().Be(2);

        // Omission removes: resend with only the steak.
        var second = await PatchItemsAsync(_world.WaiterA, draftId, new[]
        {
            new { productId = steak, quantity = 3, notes = (string?)"bine făcut" },
        });
        var secondBody = await ReadJsonAsync(second);
        secondBody.GetProperty("totalAmount").GetDecimal().Should().Be(165m);

        var items = secondBody.GetProperty("items").EnumerateArray().ToList();
        items.Should().HaveCount(1);
        items[0].GetProperty("productId").GetGuid().Should().Be(steak);
        items[0].GetProperty("notes").GetString().Should().Be("bine făcut");
    }

    [Fact]
    public async Task Item_sync_enforces_the_input_caps()
    {
        var (tableId, _) = await ClaimAsync("A6");
        var productId = await WaiterModeTestFixtures.AddProductAsync(_factory, _world.RestaurantId, "Capat", 10m);
        var draftId = (await ReadJsonAsync(await CreateDraftAsync(_world.WaiterA, tableId))).GetProperty("id").GetGuid();

        // A 200-char note is fine; 201 is not.
        (await PatchItemsAsync(_world.WaiterA, draftId, new[]
        {
            new { productId, quantity = 1, notes = new string('x', 200) },
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        (await PatchItemsAsync(_world.WaiterA, draftId, new[]
        {
            new { productId, quantity = 1, notes = new string('x', 201) },
        })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Quantity bounds.
        (await PatchItemsAsync(_world.WaiterA, draftId, new[] { new { productId, quantity = 0, notes = (string?)null } }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await PatchItemsAsync(_world.WaiterA, draftId, new[] { new { productId, quantity = 100, notes = (string?)null } }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // An unknown product is rejected outright rather than silently dropped.
        (await PatchItemsAsync(_world.WaiterA, draftId, new[] { new { productId = Guid.NewGuid(), quantity = 1, notes = (string?)null } }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Item_sync_rejects_a_hidden_product()
    {
        var (tableId, _) = await ClaimAsync("A7");
        var productId = await WaiterModeTestFixtures.AddProductAsync(_factory, _world.RestaurantId, "Ascuns", 10m);
        await WaiterModeTestFixtures.WithDbAsync(_factory, _world.RestaurantId, db => db.Products
            .Where(p => p.Id == productId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsAvailable, false)));

        var draftId = (await ReadJsonAsync(await CreateDraftAsync(_world.WaiterA, tableId))).GetProperty("id").GetGuid();

        (await PatchItemsAsync(_world.WaiterA, draftId, new[] { new { productId, quantity = 1, notes = (string?)null } }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// The waiter path must go through the same idempotent dispatch as the diner path —
    /// one decrement, never two, and no second submit of an already-sent round.
    /// </summary>
    [Fact]
    public async Task Submit_dispatches_immediately_and_decrements_stock_exactly_once()
    {
        var (tableId, _) = await ClaimAsync("A8");
        var (productId, ingredientId) = await WaiterModeTestFixtures.AddRecipeProductAsync(
            _factory, _world.RestaurantId, "Pizza", 45m, stockOnHand: 10m);

        var draftId = (await ReadJsonAsync(await CreateDraftAsync(_world.WaiterA, tableId))).GetProperty("id").GetGuid();
        (await PatchItemsAsync(_world.WaiterA, draftId, new[] { new { productId, quantity = 3, notes = (string?)null } }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var submit = await _world.WaiterA.PostAsync($"{_world.WaiterUrl}/orders/{draftId}/submit", null);
        submit.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await ReadJsonAsync(submit);
        // No flash-deal hold on this path: the round is with the kitchen already.
        body.GetProperty("status").GetString().Should().Be("submitted");
        body.GetProperty("orderNumber").GetInt32().Should().BeGreaterThan(0);
        body.GetProperty("totalAmount").GetDecimal().Should().Be(135m);

        var afterFirst = await WithDbAsync(db => db.Ingredients.SingleAsync(i => i.Id == ingredientId));
        afterFirst.QuantityOnHand.Should().Be(7m);

        // The draft is gone, so a replayed submit finds nothing and cannot double-decrement.
        (await _world.WaiterA.PostAsync($"{_world.WaiterUrl}/orders/{draftId}/submit", null))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        var afterReplay = await WithDbAsync(db => db.Ingredients.SingleAsync(i => i.Id == ingredientId));
        afterReplay.QuantityOnHand.Should().Be(7m);
    }

    [Fact]
    public async Task Submitting_an_empty_order_is_rejected()
    {
        var (tableId, _) = await ClaimAsync("A9");
        var draftId = (await ReadJsonAsync(await CreateDraftAsync(_world.WaiterA, tableId))).GetProperty("id").GetGuid();

        (await _world.WaiterA.PostAsync($"{_world.WaiterUrl}/orders/{draftId}/submit", null))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Items_cannot_be_edited_once_the_round_is_with_the_kitchen()
    {
        var (tableId, _) = await ClaimAsync("A10");
        var productId = await WaiterModeTestFixtures.AddProductAsync(_factory, _world.RestaurantId, "Trimis", 20m);

        var draftId = (await ReadJsonAsync(await CreateDraftAsync(_world.WaiterA, tableId))).GetProperty("id").GetGuid();
        await PatchItemsAsync(_world.WaiterA, draftId, new[] { new { productId, quantity = 1, notes = (string?)null } });
        (await _world.WaiterA.PostAsync($"{_world.WaiterUrl}/orders/{draftId}/submit", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await PatchItemsAsync(_world.WaiterA, draftId, new[] { new { productId, quantity = 5, notes = (string?)null } }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Cancel_restores_stock_once_and_is_refused_to_a_colleague()
    {
        var (tableId, _) = await ClaimAsync("A11");
        var (productId, ingredientId) = await WaiterModeTestFixtures.AddRecipeProductAsync(
            _factory, _world.RestaurantId, "Paste", 30m, stockOnHand: 5m);

        var draftId = (await ReadJsonAsync(await CreateDraftAsync(_world.WaiterA, tableId))).GetProperty("id").GetGuid();
        await PatchItemsAsync(_world.WaiterA, draftId, new[] { new { productId, quantity = 2, notes = (string?)null } });
        (await _world.WaiterA.PostAsync($"{_world.WaiterUrl}/orders/{draftId}/submit", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await WithDbAsync(db => db.Ingredients.SingleAsync(i => i.Id == ingredientId))).QuantityOnHand.Should().Be(3m);

        // Move it along the board first — cancellation is allowed up to "ready".
        await WaiterModeTestFixtures.WithDbAsync(_factory, _world.RestaurantId, db => db.Orders
            .Where(o => o.Id == draftId)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, "preparing")));

        // Another waiter's session is none of B's business.
        (await _world.WaiterB.PostAsync($"{_world.WaiterUrl}/orders/{draftId}/cancel", null))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await _world.WaiterA.PostAsync($"{_world.WaiterUrl}/orders/{draftId}/cancel", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var order = await WithDbAsync(db => db.Orders.SingleAsync(o => o.Id == draftId));
        order.Status.Should().Be("cancelled");
        order.DeletedAt.Should().NotBeNull();
        order.StockDecremented.Should().BeFalse();

        (await WithDbAsync(db => db.Ingredients.SingleAsync(i => i.Id == ingredientId))).QuantityOnHand.Should().Be(5m);

        // A second cancel cannot restore twice.
        (await _world.WaiterA.PostAsync($"{_world.WaiterUrl}/orders/{draftId}/cancel", null))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await WithDbAsync(db => db.Ingredients.SingleAsync(i => i.Id == ingredientId))).QuantityOnHand.Should().Be(5m);
    }

    [Fact]
    public async Task Session_detail_lists_both_waiter_and_qr_rounds()
    {
        var (tableId, sessionId) = await ClaimAsync("A12");
        var productId = await WaiterModeTestFixtures.AddProductAsync(_factory, _world.RestaurantId, "Comun", 10m);

        var draftId = (await ReadJsonAsync(await CreateDraftAsync(_world.WaiterA, tableId))).GetProperty("id").GetGuid();
        await PatchItemsAsync(_world.WaiterA, draftId, new[] { new { productId, quantity = 1, notes = (string?)null } });
        (await _world.WaiterA.PostAsync($"{_world.WaiterUrl}/orders/{draftId}/submit", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        using var anonymous = _factory.CreateClient();
        var create = await anonymous.PostAsJsonAsync(
            $"/api/restaurants/{_world.RestaurantId}/orders",
            new { tableId, items = new[] { new { productId, quantity = 2 } } });
        var qrOrderId = (await ReadJsonAsync(create)).GetProperty("id").GetGuid();
        (await anonymous.PostAsync($"/api/restaurants/{_world.RestaurantId}/orders/{qrOrderId}/submit", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var detail = await ReadJsonAsync(await _world.WaiterA.GetAsync($"{_world.WaiterUrl}/sessions/{sessionId}"));
        var orders = detail.GetProperty("orders").EnumerateArray().ToList();

        orders.Should().HaveCount(2);
        orders.Select(o => o.GetProperty("source").GetString()).Should().BeEquivalentTo(["waiter", "qr"]);
        detail.GetProperty("cumulativeTotal").GetDecimal().Should().Be(30m);
        detail.GetProperty("session").GetProperty("cumulativeTotal").GetDecimal().Should().Be(30m);
    }
}
