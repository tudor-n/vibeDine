using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantApi.Data;
using RestaurantApi.Models;
using RestaurantApi.Tests.Helpers;
using Testcontainers.PostgreSql;
using Xunit;

namespace RestaurantApi.Tests.Branding;

// Covers the waiter-read carve-out added to TenantResolutionMiddleware's WaiterAllowedPaths:
// branding is non-sensitive (already shown to anonymous diners) so an active Waiter must be
// able to GET it (needed so the admin shell can re-skin for them), but must still be rejected
// from PUT (colour writes) and the logo upload route.
public class BrandingWaiterAccessTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private MenuImportTestFactory _factory = null!;
    private HttpClient _clientA = null!;
    private HttpClient _clientB = null!;
    private Guid _restaurantIdA;
    private Guid _restaurantIdB;
    private Guid _waiterUserId;

    private static readonly byte[] MinimalPng =
        { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0, 0, 0, 0, 0 };

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

        _waiterUserId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var waiterUser = new AppUser
            {
                Id = _waiterUserId,
                UserName = $"waiter-{_waiterUserId:N}@test.com",
                Email = $"waiter-{_waiterUserId:N}@test.com",
                FullName = "Waiter",
            };
            await userManager.CreateAsync(waiterUser, "TestPass1!");

            var restaurant = await db.Restaurants.FirstAsync(r => r.Id == _restaurantIdA);
            db.Set<Membership>().Add(new Membership
            {
                Id = Guid.NewGuid(),
                UserId = _waiterUserId,
                OrganizationId = restaurant.OrganizationId,
                RestaurantId = _restaurantIdA,
                Role = MembershipRole.Waiter,
                Status = MembershipStatus.Active,
            });
            await db.SaveChangesAsync();
        }
    }

    public async Task DisposeAsync()
    {
        _clientA.Dispose();
        _clientB.Dispose();
        _factory.Dispose();
        await _pg.DisposeAsync();
    }

    private string BrandingUrl(Guid restaurantId) => $"/api/restaurants/{restaurantId}/admin/branding";
    private string LogoUrl(Guid restaurantId) => $"/api/restaurants/{restaurantId}/admin/branding/logo";

    [Fact]
    public async Task Waiter_can_read_branding()
    {
        using var waiterClient = _factory.CreateAuthenticatedClient(_waiterUserId, _restaurantIdA);

        var res = await waiterClient.GetAsync(BrandingUrl(_restaurantIdA));

        res.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Waiter_cannot_write_branding()
    {
        using var waiterClient = _factory.CreateAuthenticatedClient(_waiterUserId, _restaurantIdA);
        var payload = new
        {
            enabled = true,
            primary = "#E51F4E",
            secondary = "#E8A33B",
            background = "#F5EEE3",
        };

        var res = await waiterClient.PutAsJsonAsync(BrandingUrl(_restaurantIdA), payload);

        res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Waiter_cannot_upload_logo()
    {
        using var waiterClient = _factory.CreateAuthenticatedClient(_waiterUserId, _restaurantIdA);
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(MinimalPng);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "logo.png");

        var res = await waiterClient.PostAsync(LogoUrl(_restaurantIdA), form);

        res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
