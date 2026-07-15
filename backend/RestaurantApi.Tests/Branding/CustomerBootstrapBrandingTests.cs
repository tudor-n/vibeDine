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

namespace RestaurantApi.Tests.Branding;

// Covers Task 5: the customer bootstrap responses (PartyController.Join — the live
// endpoint — and CustomerController.GetTableSession — the legacy one) must both surface
// a `branding` object built from the restaurant's white-label columns, with a stable
// disabled-fallback shape so the customer app can always parse it regardless of whether
// the restaurant has opted in.
public class CustomerBootstrapBrandingTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private MenuImportTestFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await _pg.StartAsync();
        _factory = new MenuImportTestFactory { PostgresConnectionString = _pg.GetConnectionString() };

        using (var migrateScope = _factory.Services.CreateScope())
        {
            var migrateDb = migrateScope.ServiceProvider.GetRequiredService<AppDbContext>();
            await migrateDb.Database.MigrateAsync();
        }

        // The customer bootstrap endpoints are anonymous and scoped by qrToken only, so
        // no membership/JWT is needed — just an org, a restaurant, and a table with a token.
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        await _pg.DisposeAsync();
    }

    /// <summary>
    /// Seeds an Organization + Restaurant (with the given branding) + a RestaurantTable
    /// carrying a fresh QrToken, and returns that token. Follows the direct-DB seeding
    /// pattern used by BrandingApiTests/BrandingUploadTests for tenant setup.
    /// </summary>
    private async Task<Guid> SeedTableAsync(Action<Restaurant>? configureBranding = null)
    {
        var restaurantId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var qrToken = Guid.NewGuid();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var org = new Organization
        {
            Id = orgId,
            Name = "Branding Org",
            Slug = "branding-org-" + orgId.ToString("N")[..6],
            CreatedByUserId = Guid.NewGuid(),
        };
        db.Set<Organization>().Add(org);

        var restaurant = new Restaurant
        {
            Id = restaurantId,
            OrganizationId = orgId,
            Name = "Branding Restaurant",
            Slug = "branding-rest-" + restaurantId.ToString("N")[..6],
            IsActive = true,
        };
        configureBranding?.Invoke(restaurant);
        db.Restaurants.Add(restaurant);

        db.Tables.Add(new RestaurantTable
        {
            Id = Guid.NewGuid(),
            RestaurantId = restaurantId,
            TableNumber = "T1",
            QrToken = qrToken,
        });

        await db.SaveChangesAsync();

        return qrToken;
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage res)
    {
        var stream = await res.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.Clone();
    }

    [Fact]
    public async Task Join_returns_enabled_branding_with_colours_and_logo()
    {
        var qrToken = await SeedTableAsync(r =>
        {
            r.BrandingEnabled = true;
            r.BrandPrimary = "#E51F4E";
            r.BrandSecondary = "#E8A33B";
            r.BrandBackground = "#F5EEE3";
            r.LogoUrl = "/uploads/branding/abc/logo.png";
            r.PhotoUrl = "/uploads/branding/abc/photo.png";
        });

        var res = await _client.PostAsJsonAsync($"/api/tables/{qrToken}/join", new { });
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await ReadJsonAsync(res);
        var branding = json.GetProperty("branding");
        branding.GetProperty("enabled").GetBoolean().Should().BeTrue();
        branding.GetProperty("primary").GetString().Should().Be("#E51F4E");
        branding.GetProperty("secondary").GetString().Should().Be("#E8A33B");
        branding.GetProperty("background").GetString().Should().Be("#F5EEE3");
        branding.GetProperty("logoUrl").GetString().Should().Be("/uploads/branding/abc/logo.png");
        branding.GetProperty("photoUrl").GetString().Should().Be("/uploads/branding/abc/photo.png");
    }

    [Fact]
    public async Task Join_returns_disabled_branding_fallback_when_not_configured()
    {
        var qrToken = await SeedTableAsync(); // BrandingEnabled defaults to false, no colours set

        var res = await _client.PostAsJsonAsync($"/api/tables/{qrToken}/join", new { });
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await ReadJsonAsync(res);
        var branding = json.GetProperty("branding");
        branding.GetProperty("enabled").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Branding_endpoint_returns_colours_for_a_branded_table()
    {
        var qrToken = await SeedTableAsync(r =>
        {
            r.BrandingEnabled = true;
            r.BrandPrimary = "#E51F4E";
            r.BrandSecondary = "#E8A33B";
            r.BrandBackground = "#F5EEE3";
        });

        var res = await _client.GetAsync($"/api/tables/{qrToken}/branding");
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await ReadJsonAsync(res);
        json.GetProperty("enabled").GetBoolean().Should().BeTrue();
        json.GetProperty("primary").GetString().Should().Be("#E51F4E");
        json.GetProperty("secondary").GetString().Should().Be("#E8A33B");
        json.GetProperty("background").GetString().Should().Be("#F5EEE3");
    }

    [Fact]
    public async Task Branding_endpoint_reports_disabled_for_an_unbranded_table()
    {
        var qrToken = await SeedTableAsync();

        var res = await _client.GetAsync($"/api/tables/{qrToken}/branding");
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await ReadJsonAsync(res);
        json.GetProperty("enabled").GetBoolean().Should().BeFalse();
    }

    // The endpoint is anonymous and guarded only by the unguessable qrToken, so it
    // must stay a colours-only keyhole: anything else here would be readable by
    // anyone who ever photographed a table's QR code.
    [Fact]
    public async Task Branding_endpoint_exposes_only_colours_and_no_tenant_detail()
    {
        var qrToken = await SeedTableAsync(r =>
        {
            r.BrandingEnabled = true;
            r.BrandPrimary = "#E51F4E";
            r.BrandSecondary = "#E8A33B";
            r.BrandBackground = "#F5EEE3";
        });

        var res = await _client.GetAsync($"/api/tables/{qrToken}/branding");
        var json = await ReadJsonAsync(res);

        json.EnumerateObject().Select(p => p.Name)
            .Should().BeEquivalentTo(new[] { "enabled", "primary", "secondary", "background" });
    }

    [Fact]
    public async Task Branding_endpoint_404s_for_an_unknown_token()
    {
        var res = await _client.GetAsync($"/api/tables/{Guid.NewGuid()}/branding");
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Session_legacy_endpoint_returns_enabled_branding()
    {
        var qrToken = await SeedTableAsync(r =>
        {
            r.BrandingEnabled = true;
            r.BrandPrimary = "#E51F4E";
            r.BrandSecondary = "#E8A33B";
            r.BrandBackground = "#F5EEE3";
            r.LogoUrl = "/uploads/branding/abc/logo.png";
            r.PhotoUrl = "/uploads/branding/abc/photo.png";
        });

        var res = await _client.GetAsync($"/api/tables/{qrToken}/session");
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await ReadJsonAsync(res);
        var branding = json.GetProperty("branding");
        branding.GetProperty("enabled").GetBoolean().Should().BeTrue();
        branding.GetProperty("primary").GetString().Should().Be("#E51F4E");
    }

    [Fact]
    public async Task Session_legacy_endpoint_returns_disabled_branding_fallback()
    {
        var qrToken = await SeedTableAsync();

        var res = await _client.GetAsync($"/api/tables/{qrToken}/session");
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await ReadJsonAsync(res);
        var branding = json.GetProperty("branding");
        branding.GetProperty("enabled").GetBoolean().Should().BeFalse();
    }
}
