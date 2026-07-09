using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantApi.Data;
using RestaurantApi.Tests.Helpers;
using Testcontainers.PostgreSql;
using Xunit;

namespace RestaurantApi.Tests.Branding;

// Covers the tenant-scoped branding-colours slice: PUT/GET roundtrip, hex validation,
// and cross-tenant isolation (a member of restaurant A must not read/write restaurant B's
// branding). Uploads (logo/photo) land on the same controller in a later task.
public class BrandingApiTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private MenuImportTestFactory _factory = null!;
    private HttpClient _clientA = null!;
    private HttpClient _clientB = null!;
    private Guid _restaurantIdA;
    private Guid _restaurantIdB;

    public async Task InitializeAsync()
    {
        await _pg.StartAsync();
        _factory = new MenuImportTestFactory { PostgresConnectionString = _pg.GetConnectionString() };

        using (var migrateScope = _factory.Services.CreateScope())
        {
            var migrateDb = migrateScope.ServiceProvider.GetRequiredService<AppDbContext>();
            await migrateDb.Database.MigrateAsync();
        }

        var (restA, restB, clientA, clientB) = await MenuImportTestFixtures.SeedTwoTenantsAsync(_factory);
        _restaurantIdA = restA;
        _restaurantIdB = restB;
        _clientA = clientA;
        _clientB = clientB;
    }

    public async Task DisposeAsync()
    {
        _clientA.Dispose();
        _clientB.Dispose();
        _factory.Dispose();
        await _pg.DisposeAsync();
    }

    private string BrandingUrl(Guid restaurantId) => $"/api/restaurants/{restaurantId}/admin/branding";

    [Fact]
    public async Task Put_then_get_roundtrips_branding()
    {
        var payload = new
        {
            enabled = true,
            primary = "#E51F4E",
            secondary = "#E8A33B",
            background = "#F5EEE3",
            presetKey = "supporter",
        };

        var put = await _clientA.PutAsJsonAsync(BrandingUrl(_restaurantIdA), payload);
        put.StatusCode.Should().Be(HttpStatusCode.OK);

        var get = await _clientA.GetFromJsonAsync<BrandingDtoShape>(BrandingUrl(_restaurantIdA));
        get!.enabled.Should().BeTrue();
        get.primary.Should().Be("#E51F4E");
        get.secondary.Should().Be("#E8A33B");
        get.background.Should().Be("#F5EEE3");
        get.presetKey.Should().Be("supporter");
    }

    [Fact]
    public async Task Put_rejects_invalid_hex()
    {
        var bad = new { enabled = true, primary = "red", secondary = "#E8A33B", background = "#F5EEE3" };
        var put = await _clientA.PutAsJsonAsync(BrandingUrl(_restaurantIdA), bad);
        put.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Put_rejects_enabled_with_missing_colour()
    {
        var bad = new { enabled = true, primary = (string?)null, secondary = "#E8A33B", background = "#F5EEE3" };
        var put = await _clientA.PutAsJsonAsync(BrandingUrl(_restaurantIdA), bad);
        put.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Cannot_read_another_restaurants_branding()
    {
        // _clientA's user only has membership on restaurant A; hitting restaurant B's
        // branding route must be blocked by TenantResolutionMiddleware before the
        // controller ever runs.
        var res = await _clientA.GetAsync(BrandingUrl(_restaurantIdB));
        res.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
    }

    private record BrandingDtoShape(bool enabled, string? primary, string? secondary,
        string? background, string? logoUrl, string? photoUrl, string? presetKey);
}
