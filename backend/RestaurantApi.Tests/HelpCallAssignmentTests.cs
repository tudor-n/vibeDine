using System;
using System.Linq;
using System.Net;
using System.Net.Http;
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
/// Help-call ownership: a call from a table covered by an open service session is
/// assigned to that session's waiter; the list endpoint hides colleagues' assigned
/// calls from other waiters while managers keep the full queue.
/// </summary>
public class HelpCallAssignmentTests : IAsyncLifetime
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

    private Task<(Guid TableId, Guid QrToken)> AddTableAsync(string number) =>
        WaiterModeTestFixtures.AddTableAsync(_factory, _world.RestaurantId, number);

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage res)
    {
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private async Task CallWaiterAsync(Guid tableId)
    {
        var res = await _anonymous.PostAsync(
            $"/api/restaurants/{_world.RestaurantId}/tables/{tableId}/call-waiter", null);
        res.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<JsonElement> ListAsync(HttpClient client)
    {
        var res = await client.GetAsync($"/api/restaurants/{_world.RestaurantId}/help-requests");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        return await ReadJsonAsync(res);
    }

    [Fact]
    public async Task Call_on_an_unclaimed_table_is_unassigned_and_visible_to_every_waiter()
    {
        var (tableId, _) = await AddTableAsync("H1");
        await CallWaiterAsync(tableId);

        foreach (var client in new[] { _world.WaiterA, _world.WaiterB, _world.Manager })
        {
            var list = await ListAsync(client);
            var entry = list.EnumerateArray().Single(c => c.GetProperty("tableId").GetGuid() == tableId);
            entry.GetProperty("assignedWaiterUserId").ValueKind.Should().Be(JsonValueKind.Null);
            entry.GetProperty("assignedWaiterName").ValueKind.Should().Be(JsonValueKind.Null);
        }
    }

    [Fact]
    public async Task Call_on_a_claimed_table_is_assigned_and_hidden_from_other_waiters()
    {
        var (tableId, _) = await AddTableAsync("H2");
        var claim = await _world.WaiterA.PostAsync($"{_world.WaiterUrl}/tables/{tableId}/claim", null);
        claim.StatusCode.Should().Be(HttpStatusCode.Created);

        await CallWaiterAsync(tableId);

        // The owning waiter sees it, tagged with themselves.
        var mine = await ListAsync(_world.WaiterA);
        var entry = mine.EnumerateArray().Single(c => c.GetProperty("tableId").GetGuid() == tableId);
        entry.GetProperty("assignedWaiterUserId").GetGuid().Should().Be(_world.WaiterAUserId);
        entry.GetProperty("assignedWaiterName").GetString().Should().Be(WaiterModeTestFixtures.WaiterAName);

        // A colleague does not — the call is not everyone's alarm.
        var colleague = await ListAsync(_world.WaiterB);
        colleague.EnumerateArray().Any(c => c.GetProperty("tableId").GetGuid() == tableId)
            .Should().BeFalse();

        // The manager keeps the full queue.
        var manager = await ListAsync(_world.Manager);
        manager.EnumerateArray().Any(c => c.GetProperty("tableId").GetGuid() == tableId)
            .Should().BeTrue();
    }

    [Fact]
    public async Task Claiming_the_table_after_the_call_assigns_the_existing_call()
    {
        var (tableId, _) = await AddTableAsync("H3");
        await CallWaiterAsync(tableId);

        // Open call is unassigned first — both waiters see it (checked above); the
        // claim moves it to WaiterB alone because assignment is derived at read time.
        var claim = await _world.WaiterB.PostAsync($"{_world.WaiterUrl}/tables/{tableId}/claim", null);
        claim.StatusCode.Should().Be(HttpStatusCode.Created);

        var mine = await ListAsync(_world.WaiterB);
        mine.EnumerateArray().Single(c => c.GetProperty("tableId").GetGuid() == tableId)
            .GetProperty("assignedWaiterUserId").GetGuid().Should().Be(_world.WaiterBUserId);

        var other = await ListAsync(_world.WaiterA);
        other.EnumerateArray().Any(c => c.GetProperty("tableId").GetGuid() == tableId)
            .Should().BeFalse();
    }
}
