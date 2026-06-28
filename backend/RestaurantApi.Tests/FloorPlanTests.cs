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
/// Covers the floor-plan slice: zone CRUD, deleting a zone unassigns its tables,
/// and the new ZoneId/PositionX/PositionY fields on tables (create, patch, list).
/// Asserts on the exact camelCased JSON the frontend depends on.
/// </summary>
public class FloorPlanTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
    private MenuImportTestFactory _factory = null!;
    private HttpClient _client = null!;
    private Guid _restaurantId;

    private string ZonesUrl => $"/api/restaurants/{_restaurantId}/admin/zones";
    private string TablesUrl => $"/api/restaurants/{_restaurantId}/admin/tables";

    public async Task InitializeAsync()
    {
        await _pg.StartAsync();
        _factory = new MenuImportTestFactory { PostgresConnectionString = _pg.GetConnectionString() };
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.MigrateAsync();
        }
        var (restA, _, clientA, _) = await MenuImportTestFixtures.SeedTwoTenantsAsync(_factory);
        _restaurantId = restA;
        _client = clientA;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        await _pg.DisposeAsync();
    }

    // Unique table number per call so concurrent/sequential tests never collide.
    private static string NewTableNumber() => "T-" + Guid.NewGuid().ToString("N")[..8];

    private async Task<JsonElement> CreateZoneAsync(string name)
    {
        var res = await _client.PostAsJsonAsync(ZonesUrl, new { name });
        res.StatusCode.Should().Be(HttpStatusCode.Created);
        return JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private async Task<JsonElement> CreateTableAsync(object body)
    {
        var res = await _client.PostAsJsonAsync(TablesUrl, body);
        res.StatusCode.Should().Be(HttpStatusCode.Created);
        return JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    [Fact]
    public async Task Zone_create_assigns_incrementing_sort_order_and_returns_camelCase_fields()
    {
        var first = await CreateZoneAsync("Terasă");
        var second = await CreateZoneAsync("Salon");

        first.GetProperty("id").GetGuid().Should().NotBeEmpty();
        first.GetProperty("name").GetString().Should().Be("Terasă");
        var firstSort = first.GetProperty("sortOrder").GetInt32();
        var secondSort = second.GetProperty("sortOrder").GetInt32();
        secondSort.Should().Be(firstSort + 1);
    }

    [Fact]
    public async Task Zone_create_rejects_empty_name_with_400()
    {
        var res = await _client.PostAsJsonAsync(ZonesUrl, new { name = "   " });
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Zone_list_returns_live_zones_ordered_by_sort_order()
    {
        await CreateZoneAsync("Z-" + Guid.NewGuid().ToString("N")[..6]);
        await CreateZoneAsync("Z-" + Guid.NewGuid().ToString("N")[..6]);

        var res = await _client.GetAsync(ZonesUrl);
        res.IsSuccessStatusCode.Should().BeTrue();
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var sortOrders = doc.RootElement.EnumerateArray()
            .Select(z => z.GetProperty("sortOrder").GetInt32())
            .ToList();
        sortOrders.Should().BeInAscendingOrder();
        // Every item exposes the camelCase contract.
        foreach (var z in doc.RootElement.EnumerateArray())
        {
            z.TryGetProperty("id", out _).Should().BeTrue();
            z.TryGetProperty("name", out _).Should().BeTrue();
            z.TryGetProperty("sortOrder", out _).Should().BeTrue();
        }
    }

    [Fact]
    public async Task Zone_patch_updates_name_and_sort_order()
    {
        var zone = await CreateZoneAsync("Old Name");
        var id = zone.GetProperty("id").GetGuid();

        var res = await _client.PatchAsJsonAsync($"{ZonesUrl}/{id}", new { name = "New Name", sortOrder = 42 });
        res.IsSuccessStatusCode.Should().BeTrue();
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("name").GetString().Should().Be("New Name");
        doc.RootElement.GetProperty("sortOrder").GetInt32().Should().Be(42);
    }

    [Fact]
    public async Task Zone_patch_returns_404_for_unknown_id()
    {
        var res = await _client.PatchAsJsonAsync($"{ZonesUrl}/{Guid.NewGuid()}", new { name = "x" });
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Zone_delete_soft_deletes_and_unassigns_its_tables()
    {
        var zone = await CreateZoneAsync("ToDelete");
        var zoneId = zone.GetProperty("id").GetGuid();

        var table = await CreateTableAsync(new { tableNumber = NewTableNumber(), zoneId, positionX = 5.0, positionY = 6.0 });
        var tableId = table.GetProperty("id").GetGuid();
        table.GetProperty("zoneId").GetGuid().Should().Be(zoneId);

        var del = await _client.DeleteAsync($"{ZonesUrl}/{zoneId}");
        del.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Zone no longer listed.
        var zonesRes = await _client.GetAsync(ZonesUrl);
        using var zonesDoc = JsonDocument.Parse(await zonesRes.Content.ReadAsStringAsync());
        zonesDoc.RootElement.EnumerateArray()
            .Any(z => z.GetProperty("id").GetGuid() == zoneId).Should().BeFalse();

        // Table survived but is now unassigned.
        var tablesRes = await _client.GetAsync(TablesUrl);
        using var tablesDoc = JsonDocument.Parse(await tablesRes.Content.ReadAsStringAsync());
        var found = tablesDoc.RootElement.EnumerateArray()
            .First(t => t.GetProperty("id").GetGuid() == tableId);
        found.GetProperty("zoneId").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Create_table_with_zone_and_position_persists_and_returns_fields()
    {
        var zone = await CreateZoneAsync("WithTable");
        var zoneId = zone.GetProperty("id").GetGuid();

        var table = await CreateTableAsync(new { tableNumber = NewTableNumber(), zoneId, positionX = 12.5, positionY = 30.25 });
        table.GetProperty("zoneId").GetGuid().Should().Be(zoneId);
        table.GetProperty("positionX").GetDouble().Should().Be(12.5);
        table.GetProperty("positionY").GetDouble().Should().Be(30.25);
        table.GetProperty("qrToken").GetGuid().Should().NotBeEmpty();
    }

    [Fact]
    public async Task Create_table_without_position_defaults_to_zero()
    {
        var table = await CreateTableAsync(new { tableNumber = NewTableNumber() });
        table.GetProperty("positionX").GetDouble().Should().Be(0);
        table.GetProperty("positionY").GetDouble().Should().Be(0);
        table.GetProperty("zoneId").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Patch_table_updates_position_and_zone()
    {
        var zoneA = await CreateZoneAsync("ZoneA");
        var zoneB = await CreateZoneAsync("ZoneB");
        var zoneAId = zoneA.GetProperty("id").GetGuid();
        var zoneBId = zoneB.GetProperty("id").GetGuid();

        var table = await CreateTableAsync(new { tableNumber = NewTableNumber(), zoneId = zoneAId, positionX = 1.0, positionY = 1.0 });
        var tableId = table.GetProperty("id").GetGuid();

        var res = await _client.PatchAsJsonAsync($"{TablesUrl}/{tableId}",
            new { zoneId = zoneBId, positionX = 99.0, positionY = 77.5 });
        res.IsSuccessStatusCode.Should().BeTrue();
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("zoneId").GetGuid().Should().Be(zoneBId);
        doc.RootElement.GetProperty("positionX").GetDouble().Should().Be(99.0);
        doc.RootElement.GetProperty("positionY").GetDouble().Should().Be(77.5);
    }

    [Fact]
    public async Task Patch_table_returns_404_for_unknown_id()
    {
        var res = await _client.PatchAsJsonAsync($"{TablesUrl}/{Guid.NewGuid()}", new { positionX = 1.0 });
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Table_list_returns_new_floor_plan_fields()
    {
        var zone = await CreateZoneAsync("ListZone");
        var zoneId = zone.GetProperty("id").GetGuid();
        var created = await CreateTableAsync(new { tableNumber = NewTableNumber(), zoneId, positionX = 7.0, positionY = 8.0 });
        var tableId = created.GetProperty("id").GetGuid();

        var res = await _client.GetAsync(TablesUrl);
        res.IsSuccessStatusCode.Should().BeTrue();
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var found = doc.RootElement.EnumerateArray()
            .First(t => t.GetProperty("id").GetGuid() == tableId);
        found.GetProperty("zoneId").GetGuid().Should().Be(zoneId);
        found.GetProperty("positionX").GetDouble().Should().Be(7.0);
        found.GetProperty("positionY").GetDouble().Should().Be(8.0);
    }
}
