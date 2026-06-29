using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantApi.Data;
using RestaurantApi.Models;
using RestaurantApi.Tenancy;
using RestaurantApi.Tests.Helpers;
using Testcontainers.PostgreSql;
using Xunit;

namespace RestaurantApi.Tests;

// Reproduces the report: saving a flash deal WITHOUT a minimum-order condition
// works, but saving WITH one fails. Drives the real PUT /admin/flash-deal endpoint
// against a real Postgres to isolate whether the backend is the culprit.
public class FlashDealRuleSaveTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private MenuImportTestFactory _factory = null!;
    private HttpClient _client = null!;
    private Guid _restaurantId;
    private Guid _productId;

    public async Task InitializeAsync()
    {
        await _pg.StartAsync();
        _factory = new MenuImportTestFactory { PostgresConnectionString = _pg.GetConnectionString() };

        using (var migrateScope = _factory.Services.CreateScope())
        {
            var migrateDb = migrateScope.ServiceProvider.GetRequiredService<AppDbContext>();
            await migrateDb.Database.MigrateAsync();
        }

        var (restA, _, clientA, _) = await MenuImportTestFixtures.SeedTwoTenantsAsync(_factory);
        _restaurantId = restA;
        _client = clientA;

        using var seedScope = _factory.Services.CreateScope();
        var tenant = seedScope.ServiceProvider.GetRequiredService<ICurrentTenant>();
        tenant.SetRestaurant(_restaurantId);
        var db = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cat = new Category { Id = Guid.NewGuid(), RestaurantId = _restaurantId, Name = "Aperitive", SortOrder = 0 };
        db.Categories.Add(cat);
        var product = new Product
        {
            Id = Guid.NewGuid(),
            RestaurantId = _restaurantId,
            CategoryId = cat.Id,
            Name = "Bruschete",
            Price = 24.00m,
            IsAvailable = true,
        };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        _productId = product.Id;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        await _pg.DisposeAsync();
    }

    private string FlashDealUrl => $"/api/restaurants/{_restaurantId}/admin/flash-deal";

    [Fact]
    public async Task Save_without_min_order_total_succeeds()
    {
        var resp = await _client.PutAsJsonAsync(FlashDealUrl, new
        {
            productId = _productId,
            discountPercent = 20,
            minOrderTotal = (decimal?)null,
            isEnabled = true,
        });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Save_with_min_order_total_succeeds_and_persists()
    {
        var resp = await _client.PutAsJsonAsync(FlashDealUrl, new
        {
            productId = _productId,
            discountPercent = 20,
            minOrderTotal = 100m,
            isEnabled = true,
        });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
        tenant.SetRestaurant(_restaurantId);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rule = await db.FlashDealRules.SingleAsync();
        rule.MinOrderTotal.Should().Be(100m);
    }
}
