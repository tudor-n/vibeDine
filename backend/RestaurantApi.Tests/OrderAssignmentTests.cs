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
/// Order ownership follows the table session: waiter-typed rounds, QR rounds on a
/// claimed table, and rounds adopted at claim are assigned to the table's waiter;
/// transferring the session moves the rounds; and the active-orders board serves a
/// Waiter-role caller only the rounds assigned to them.
/// </summary>
public class OrderAssignmentTests : IAsyncLifetime
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

    private async Task<Guid> ClaimOkAsync(HttpClient client, Guid tableId)
    {
        var res = await client.PostAsync($"{_world.WaiterUrl}/tables/{tableId}/claim", null);
        res.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await ReadJsonAsync(res)).GetProperty("id").GetGuid();
    }

    private async Task<Guid> PlaceWaiterOrderAsync(HttpClient client, Guid tableId, Guid productId)
    {
        var created = await client.PostAsync($"{_world.WaiterUrl}/tables/{tableId}/orders", null);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var orderId = (await ReadJsonAsync(created)).GetProperty("id").GetGuid();

        var patched = await client.PatchAsJsonAsync(
            $"{_world.WaiterUrl}/orders/{orderId}/items",
            new { items = new[] { new { productId, quantity = 1 } } });
        patched.StatusCode.Should().Be(HttpStatusCode.OK);

        var submitted = await client.PostAsync($"{_world.WaiterUrl}/orders/{orderId}/submit", null);
        submitted.StatusCode.Should().Be(HttpStatusCode.OK);
        return orderId;
    }

    private async Task<Guid> SubmitCustomerOrderAsync(Guid tableId, Guid productId)
    {
        var create = await _anonymous.PostAsJsonAsync(
            $"/api/restaurants/{_world.RestaurantId}/orders",
            new { tableId, items = new[] { new { productId, quantity = 1 } } });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var orderId = (await ReadJsonAsync(create)).GetProperty("id").GetGuid();

        var submit = await _anonymous.PostAsync(
            $"/api/restaurants/{_world.RestaurantId}/orders/{orderId}/submit", null);
        submit.StatusCode.Should().Be(HttpStatusCode.OK);
        return orderId;
    }

    private async Task<JsonElement> BoardAsync(HttpClient client)
    {
        var res = await client.GetAsync($"/api/restaurants/{_world.RestaurantId}/admin/orders/active");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        return await ReadJsonAsync(res);
    }

    private static Guid[] BoardOrderIds(JsonElement board) =>
        board.EnumerateArray().Select(o => o.GetProperty("id").GetGuid()).ToArray();

    // ── Assignment at creation ───────────────────────────────────────────────

    [Fact]
    public async Task Waiter_round_is_assigned_to_the_tables_waiter()
    {
        var (tableId, _) = await AddTableAsync("A1");
        var productId = await AddProductAsync("Mici", 18m);
        await ClaimOkAsync(_world.WaiterA, tableId);

        var orderId = await PlaceWaiterOrderAsync(_world.WaiterA, tableId, productId);

        var board = await BoardAsync(_world.Manager);
        var entry = board.EnumerateArray().Single(o => o.GetProperty("id").GetGuid() == orderId);
        entry.GetProperty("claimedByWaiterId").GetGuid().Should().Be(_world.WaiterAUserId);
        entry.GetProperty("claimedByName").GetString().Should().Be(WaiterModeTestFixtures.WaiterAName);
    }

    [Fact]
    public async Task Qr_round_on_a_claimed_table_is_assigned_to_the_sessions_waiter()
    {
        var (tableId, _) = await AddTableAsync("A2");
        var productId = await AddProductAsync("Salată", 14m);
        await ClaimOkAsync(_world.WaiterA, tableId);

        var orderId = await SubmitCustomerOrderAsync(tableId, productId);

        var board = await BoardAsync(_world.Manager);
        board.EnumerateArray().Single(o => o.GetProperty("id").GetGuid() == orderId)
            .GetProperty("claimedByWaiterId").GetGuid().Should().Be(_world.WaiterAUserId);
    }

    [Fact]
    public async Task Claiming_a_table_assigns_its_adopted_rounds()
    {
        var (tableId, _) = await AddTableAsync("A3");
        var productId = await AddProductAsync("Supă", 16m);

        var orderId = await SubmitCustomerOrderAsync(tableId, productId);
        await ClaimOkAsync(_world.WaiterB, tableId);

        var board = await BoardAsync(_world.Manager);
        board.EnumerateArray().Single(o => o.GetProperty("id").GetGuid() == orderId)
            .GetProperty("claimedByWaiterId").GetGuid().Should().Be(_world.WaiterBUserId);
    }

    // ── The waiter-filtered board ────────────────────────────────────────────

    [Fact]
    public async Task Waiters_see_only_their_own_rounds_managers_see_everything()
    {
        var (tableA, _) = await AddTableAsync("A4");
        var (tableB, _) = await AddTableAsync("A5");
        var productId = await AddProductAsync("Paste", 25m);

        await ClaimOkAsync(_world.WaiterA, tableA);
        await ClaimOkAsync(_world.WaiterB, tableB);
        var orderA = await PlaceWaiterOrderAsync(_world.WaiterA, tableA, productId);
        var orderB = await PlaceWaiterOrderAsync(_world.WaiterB, tableB, productId);

        BoardOrderIds(await BoardAsync(_world.WaiterA)).Should().Equal(orderA);
        BoardOrderIds(await BoardAsync(_world.WaiterB)).Should().Equal(orderB);
        BoardOrderIds(await BoardAsync(_world.Manager)).Should().BeEquivalentTo(new[] { orderA, orderB });
    }

    // ── Transfer moves the rounds ────────────────────────────────────────────

    [Fact]
    public async Task Transferring_the_session_reassigns_its_live_rounds()
    {
        var (tableId, _) = await AddTableAsync("A6");
        var productId = await AddProductAsync("Tort", 20m);

        var sessionId = await ClaimOkAsync(_world.WaiterA, tableId);
        var orderId = await PlaceWaiterOrderAsync(_world.WaiterA, tableId, productId);

        var transfer = await _world.WaiterA.PostAsJsonAsync(
            $"{_world.WaiterUrl}/sessions/{sessionId}/transfer",
            new { toUserId = _world.WaiterBUserId });
        transfer.StatusCode.Should().Be(HttpStatusCode.OK);

        // The round follows the table to WaiterB's board and leaves WaiterA's.
        BoardOrderIds(await BoardAsync(_world.WaiterB)).Should().Equal(orderId);
        BoardOrderIds(await BoardAsync(_world.WaiterA)).Should().BeEmpty();

        var board = await BoardAsync(_world.Manager);
        board.EnumerateArray().Single(o => o.GetProperty("id").GetGuid() == orderId)
            .GetProperty("claimedByWaiterId").GetGuid().Should().Be(_world.WaiterBUserId);
    }
}
