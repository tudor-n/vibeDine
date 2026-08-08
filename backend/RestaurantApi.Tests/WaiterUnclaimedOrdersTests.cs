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
/// The "Comenzi nepreluate" feed: GET /waiter/unclaimed-orders lists live rounds on
/// tables no session covers, the floor exposes the per-table count, and claiming the
/// table (which adopts the rounds) is what empties both.
/// </summary>
public class WaiterUnclaimedOrdersTests : IAsyncLifetime
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

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage res)
    {
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private async Task<Guid> SubmitCustomerOrderAsync(Guid restaurantId, Guid tableId, Guid productId, int quantity)
    {
        var create = await _anonymous.PostAsJsonAsync(
            $"/api/restaurants/{restaurantId}/orders",
            new { tableId, items = new[] { new { productId, quantity } } });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var orderId = (await ReadJsonAsync(create)).GetProperty("id").GetGuid();

        var submit = await _anonymous.PostAsync(
            $"/api/restaurants/{restaurantId}/orders/{orderId}/submit", null);
        submit.StatusCode.Should().Be(HttpStatusCode.OK);

        return orderId;
    }

    private async Task<JsonElement> GetUnclaimedAsync(HttpClient client)
    {
        var res = await client.GetAsync($"{_world.WaiterUrl}/unclaimed-orders");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        return await ReadJsonAsync(res);
    }

    // ── The feed ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Feed_lists_a_qr_round_on_an_unclaimed_table_with_items_and_table_number()
    {
        var (tableId, _) = await AddTableAsync("U1");
        var productId = await AddProductAsync("Limonadă", 15.5m);

        var orderId = await SubmitCustomerOrderAsync(_world.RestaurantId, tableId, productId, 2);

        // Waiter-role caller: also proves the allow-list entry works.
        var feed = await GetUnclaimedAsync(_world.WaiterA);
        feed.GetArrayLength().Should().Be(1);

        var entry = feed[0];
        entry.GetProperty("id").GetGuid().Should().Be(orderId);
        entry.GetProperty("tableId").GetGuid().Should().Be(tableId);
        entry.GetProperty("tableNumber").GetString().Should().Be("U1");
        entry.GetProperty("source").GetString().Should().Be("qr");
        // Line-derived total, not the stored header total.
        entry.GetProperty("totalAmount").GetDecimal().Should().Be(31m);

        var items = entry.GetProperty("items");
        items.GetArrayLength().Should().Be(1);
        items[0].GetProperty("productName").GetString().Should().Be("Limonadă");
        items[0].GetProperty("quantity").GetInt32().Should().Be(2);
        items[0].GetProperty("unitPriceAtPurchase").GetDecimal().Should().Be(15.5m);
    }

    [Fact]
    public async Task Claiming_the_table_adopts_the_round_and_empties_the_feed()
    {
        var (tableId, _) = await AddTableAsync("U2");
        var productId = await AddProductAsync("Cafea", 12m);
        var orderId = await SubmitCustomerOrderAsync(_world.RestaurantId, tableId, productId, 1);

        (await GetUnclaimedAsync(_world.WaiterA)).GetArrayLength().Should().Be(1);

        var claim = await _world.WaiterA.PostAsync($"{_world.WaiterUrl}/tables/{tableId}/claim", null);
        claim.StatusCode.Should().Be(HttpStatusCode.Created);
        var sessionId = (await ReadJsonAsync(claim)).GetProperty("id").GetGuid();

        (await GetUnclaimedAsync(_world.WaiterA)).GetArrayLength().Should().Be(0);

        // The round moved onto the new bill ("Mesele mele" side of the flow).
        var detail = await ReadJsonAsync(
            await _world.WaiterA.GetAsync($"{_world.WaiterUrl}/sessions/{sessionId}"));
        detail.GetProperty("orders").EnumerateArray()
            .Select(o => o.GetProperty("id").GetGuid())
            .Should().Contain(orderId);
    }

    [Fact]
    public async Task Qr_round_on_a_claimed_table_never_enters_the_feed()
    {
        var (tableId, _) = await AddTableAsync("U3");
        var productId = await AddProductAsync("Ceai", 10m);

        var claim = await _world.WaiterB.PostAsync($"{_world.WaiterUrl}/tables/{tableId}/claim", null);
        claim.StatusCode.Should().Be(HttpStatusCode.Created);

        await SubmitCustomerOrderAsync(_world.RestaurantId, tableId, productId, 1);

        (await GetUnclaimedAsync(_world.WaiterA)).GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Feed_is_tenant_scoped()
    {
        var otherWorld = await WaiterModeTestFixtures.SeedAsync(_factory);
        try
        {
            var (foreignTableId, _) = await WaiterModeTestFixtures.AddTableAsync(
                _factory, otherWorld.RestaurantId, "F1");
            var foreignProductId = await WaiterModeTestFixtures.AddProductAsync(
                _factory, otherWorld.RestaurantId, "Străin", 9m);

            await SubmitCustomerOrderAsync(otherWorld.RestaurantId, foreignTableId, foreignProductId, 1);

            // The other restaurant's orphan round must not leak into this feed.
            (await GetUnclaimedAsync(_world.WaiterA)).GetArrayLength().Should().Be(0);
        }
        finally
        {
            otherWorld.Dispose();
        }
    }

    // ── The floor counter ────────────────────────────────────────────────────

    [Fact]
    public async Task Floor_counts_unclaimed_rounds_per_table_until_the_claim_adopts_them()
    {
        var (tableId, _) = await AddTableAsync("U4");
        var productId = await AddProductAsync("Papanaşi", 22m);
        await SubmitCustomerOrderAsync(_world.RestaurantId, tableId, productId, 1);

        var before = await FindFloorTableAsync(tableId);
        before.GetProperty("activeOrderCount").GetInt32().Should().Be(1);
        before.GetProperty("unclaimedOrderCount").GetInt32().Should().Be(1);

        var claim = await _world.WaiterA.PostAsync($"{_world.WaiterUrl}/tables/{tableId}/claim", null);
        claim.StatusCode.Should().Be(HttpStatusCode.Created);

        var after = await FindFloorTableAsync(tableId);
        after.GetProperty("activeOrderCount").GetInt32().Should().Be(1);
        after.GetProperty("unclaimedOrderCount").GetInt32().Should().Be(0);
    }

    private async Task<JsonElement> FindFloorTableAsync(Guid tableId)
    {
        var res = await _world.WaiterA.GetAsync($"{_world.WaiterUrl}/floor");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var floor = await ReadJsonAsync(res);
        return floor.GetProperty("tables").EnumerateArray()
            .Single(t => t.GetProperty("id").GetGuid() == tableId);
    }
}
