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
using RestaurantApi.Tests.Helpers;
using Testcontainers.PostgreSql;
using Xunit;

namespace RestaurantApi.Tests;

/// <summary>
/// Masa auxiliară: a per-waiter holding area. Moving a line off a live round
/// shrinks the source bill (auto-cancelling an emptied round with no stock
/// restore); moving it onto a table lands as an archived completed round on that
/// session's bill without ever reaching the kitchen board; and every operation is
/// strictly scoped to the caller's own aux table.
/// </summary>
public class AuxTableTests : IAsyncLifetime
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

    private Task<(Guid TableId, Guid QrToken)> AddTableAsync(string number) =>
        WaiterModeTestFixtures.AddTableAsync(_factory, _world.RestaurantId, number);

    private Task<Guid> AddProductAsync(string name, decimal price) =>
        WaiterModeTestFixtures.AddProductAsync(_factory, _world.RestaurantId, name, price);

    private Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> action) =>
        WaiterModeTestFixtures.WithDbAsync(_factory, _world.RestaurantId, action);

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage res)
    {
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private async Task<Guid> ClaimOkAsync(HttpClient client, Guid tableId)
    {
        var res = await client.PostAsync($"{_world.WaiterUrl}/tables/{tableId}/claim", null);
        res.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await ReadJsonAsync(res)).GetProperty("id").GetGuid();
    }

    /// <summary>Waiter round with one line of the given quantity, sent to the kitchen.</summary>
    private async Task<(Guid OrderId, Guid ItemId)> PlaceRoundAsync(
        HttpClient client, Guid tableId, Guid productId, int quantity)
    {
        var created = await client.PostAsync($"{_world.WaiterUrl}/tables/{tableId}/orders", null);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var orderId = (await ReadJsonAsync(created)).GetProperty("id").GetGuid();

        var patched = await client.PatchAsJsonAsync(
            $"{_world.WaiterUrl}/orders/{orderId}/items",
            new { items = new[] { new { productId, quantity } } });
        patched.StatusCode.Should().Be(HttpStatusCode.OK);

        (await client.PostAsync($"{_world.WaiterUrl}/orders/{orderId}/submit", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var itemId = await WithDbAsync(db => db.OrderItems
            .Where(i => i.OrderId == orderId)
            .Select(i => i.Id)
            .SingleAsync());
        return (orderId, itemId);
    }

    private async Task<JsonElement> AuxAsync(HttpClient client)
    {
        var res = await client.GetAsync($"{_world.WaiterUrl}/aux-table");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        return await ReadJsonAsync(res);
    }

    private async Task<decimal> CumulativeTotalAsync(HttpClient client, Guid sessionId)
    {
        var res = await client.GetAsync($"{_world.WaiterUrl}/sessions/{sessionId}");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await ReadJsonAsync(res)).GetProperty("cumulativeTotal").GetDecimal();
    }

    // ── Order → aux ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Partial_move_parks_the_quantity_and_shrinks_the_source_bill()
    {
        var (tableId, _) = await AddTableAsync("X1");
        var productId = await AddProductAsync("Vin", 30m);
        var sessionId = await ClaimOkAsync(_world.WaiterA, tableId);
        var (orderId, itemId) = await PlaceRoundAsync(_world.WaiterA, tableId, productId, 3);

        (await CumulativeTotalAsync(_world.WaiterA, sessionId)).Should().Be(90m);

        var move = await _world.WaiterA.PostAsJsonAsync(
            $"{_world.WaiterUrl}/orders/{orderId}/items/{itemId}/move-to-aux",
            new { quantity = 1 });
        move.StatusCode.Should().Be(HttpStatusCode.OK);

        // Source order keeps 2×30; the parked line holds 1×30 with provenance.
        (await CumulativeTotalAsync(_world.WaiterA, sessionId)).Should().Be(60m);

        var aux = await AuxAsync(_world.WaiterA);
        aux.GetProperty("total").GetDecimal().Should().Be(30m);
        var entry = aux.GetProperty("items").EnumerateArray().Single();
        entry.GetProperty("productName").GetString().Should().Be("Vin");
        entry.GetProperty("quantity").GetInt32().Should().Be(1);
        entry.GetProperty("sourceTableNumber").GetString().Should().Be("X1");

        // The order stays live for the kitchen (it still has food to make).
        var status = await WithDbAsync(db => db.Orders
            .Where(o => o.Id == orderId).Select(o => o.Status).SingleAsync());
        status.Should().Be("submitted");
    }

    [Fact]
    public async Task Moving_the_last_line_cancels_the_emptied_round_without_a_stock_restore()
    {
        var (tableId, _) = await AddTableAsync("X2");
        var (productId, ingredientId) = await WaiterModeTestFixtures.AddRecipeProductAsync(
            _factory, _world.RestaurantId, "Friptură", 45m, stockOnHand: 10m);
        var sessionId = await ClaimOkAsync(_world.WaiterA, tableId);
        var (orderId, itemId) = await PlaceRoundAsync(_world.WaiterA, tableId, productId, 2);

        // Dispatch consumed 2 portions: 10 → 8.
        (await WithDbAsync(db => db.Ingredients
            .Where(i => i.Id == ingredientId).Select(i => i.QuantityOnHand).SingleAsync()))
            .Should().Be(8m);

        var move = await _world.WaiterA.PostAsJsonAsync(
            $"{_world.WaiterUrl}/orders/{orderId}/items/{itemId}/move-to-aux",
            new { });
        move.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadJsonAsync(move)).GetProperty("orderStatus").GetString().Should().Be("cancelled");

        var order = await WithDbAsync(db => db.Orders
            .IgnoreQueryFilters().SingleAsync(o => o.Id == orderId));
        order.Status.Should().Be("cancelled");
        order.DeletedAt.Should().NotBeNull();

        // The food moved, it didn't come back: stock stays at 8.
        (await WithDbAsync(db => db.Ingredients
            .Where(i => i.Id == ingredientId).Select(i => i.QuantityOnHand).SingleAsync()))
            .Should().Be(8m);

        // …and the bill dropped to zero while the food waits on aux.
        (await CumulativeTotalAsync(_world.WaiterA, sessionId)).Should().Be(0m);
        (await AuxAsync(_world.WaiterA)).GetProperty("total").GetDecimal().Should().Be(90m);
    }

    [Fact]
    public async Task A_waiter_cannot_move_lines_off_a_colleagues_round()
    {
        var (tableId, _) = await AddTableAsync("X3");
        var productId = await AddProductAsync("Bere", 10m);
        await ClaimOkAsync(_world.WaiterA, tableId);
        var (orderId, itemId) = await PlaceRoundAsync(_world.WaiterA, tableId, productId, 1);

        var move = await _world.WaiterB.PostAsJsonAsync(
            $"{_world.WaiterUrl}/orders/{orderId}/items/{itemId}/move-to-aux",
            new { });
        move.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Aux → table ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Moving_to_a_table_lands_as_a_completed_round_on_the_bill_not_the_kitchen()
    {
        var (fromTable, _) = await AddTableAsync("X4");
        var (toTable, _) = await AddTableAsync("X5");
        var productId = await AddProductAsync("Tiramisu", 25m);
        await ClaimOkAsync(_world.WaiterA, fromTable);
        var toSessionId = await ClaimOkAsync(_world.WaiterA, toTable);
        var (orderId, itemId) = await PlaceRoundAsync(_world.WaiterA, fromTable, productId, 1);

        (await _world.WaiterA.PostAsJsonAsync(
            $"{_world.WaiterUrl}/orders/{orderId}/items/{itemId}/move-to-aux", new { }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var move = await _world.WaiterA.PostAsJsonAsync(
            $"{_world.WaiterUrl}/aux-table/items/{(await AuxItemIdAsync())}/move-to-table",
            new { tableId = toTable });
        move.StatusCode.Should().Be(HttpStatusCode.OK);

        // Aux emptied; destination bill grew by the line.
        (await AuxAsync(_world.WaiterA)).GetProperty("items").GetArrayLength().Should().Be(0);
        (await CumulativeTotalAsync(_world.WaiterA, toSessionId)).Should().Be(25m);

        // The landed round is archived-completed: on the bill, never on the kanban.
        var landed = await WithDbAsync(db => db.Orders
            .IgnoreQueryFilters()
            .SingleAsync(o => o.TableId == toTable && o.Status == "completed"));
        landed.DeletedAt.Should().NotBeNull();
        landed.StockDecremented.Should().BeTrue();
        landed.TableServiceSessionId.Should().Be(toSessionId);

        var board = await _world.Manager.GetAsync(
            $"/api/restaurants/{_world.RestaurantId}/admin/orders/active");
        (await ReadJsonAsync(board)).EnumerateArray()
            .Any(o => o.GetProperty("id").GetGuid() == landed.Id)
            .Should().BeFalse();
    }

    [Fact]
    public async Task Aux_tables_are_private_per_waiter()
    {
        var (tableId, _) = await AddTableAsync("X6");
        var productId = await AddProductAsync("Apă plată", 7m);
        await ClaimOkAsync(_world.WaiterA, tableId);
        var (orderId, itemId) = await PlaceRoundAsync(_world.WaiterA, tableId, productId, 1);

        (await _world.WaiterA.PostAsJsonAsync(
            $"{_world.WaiterUrl}/orders/{orderId}/items/{itemId}/move-to-aux", new { }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var auxItemId = await AuxItemIdAsync();

        // B's aux is empty, and B can neither move nor discard A's parked line.
        (await AuxAsync(_world.WaiterB)).GetProperty("items").GetArrayLength().Should().Be(0);
        (await _world.WaiterB.PostAsJsonAsync(
            $"{_world.WaiterUrl}/aux-table/items/{auxItemId}/move-to-table",
            new { tableId }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _world.WaiterB.DeleteAsync($"{_world.WaiterUrl}/aux-table/items/{auxItemId}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        // A discards their own line; the aux table empties, stock untouched by design.
        (await _world.WaiterA.DeleteAsync($"{_world.WaiterUrl}/aux-table/items/{auxItemId}"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await AuxAsync(_world.WaiterA)).GetProperty("items").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Aux_move_to_an_unclaimed_or_foreign_table_is_rejected()
    {
        var (fromTable, _) = await AddTableAsync("X7");
        var (freeTable, _) = await AddTableAsync("X8");
        var (bTable, _) = await AddTableAsync("X9");
        var productId = await AddProductAsync("Espresso", 9m);
        await ClaimOkAsync(_world.WaiterA, fromTable);
        await ClaimOkAsync(_world.WaiterB, bTable);
        var (orderId, itemId) = await PlaceRoundAsync(_world.WaiterA, fromTable, productId, 1);
        (await _world.WaiterA.PostAsJsonAsync(
            $"{_world.WaiterUrl}/orders/{orderId}/items/{itemId}/move-to-aux", new { }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var auxItemId = await AuxItemIdAsync();

        (await _world.WaiterA.PostAsJsonAsync(
            $"{_world.WaiterUrl}/aux-table/items/{auxItemId}/move-to-table",
            new { tableId = freeTable }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await _world.WaiterA.PostAsJsonAsync(
            $"{_world.WaiterUrl}/aux-table/items/{auxItemId}/move-to-table",
            new { tableId = bTable }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    private Task<Guid> AuxItemIdAsync() =>
        WithDbAsync(db => db.AuxTableItems
            .Where(a => a.WaiterUserId == _world.WaiterAUserId)
            .Select(a => a.Id)
            .SingleAsync());
}
