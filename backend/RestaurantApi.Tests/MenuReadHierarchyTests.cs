using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantApi.Data;
using RestaurantApi.Models;
using RestaurantApi.Tests.Helpers;
using RestaurantApi.Tenancy;
using Testcontainers.PostgreSql;
using Xunit;

namespace RestaurantApi.Tests;

public class MenuReadHierarchyTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
    private MenuImportTestFactory _factory = null!;
    private HttpClient _adminClient = null!;
    private Guid _restaurantId;

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
        _adminClient = clientA;

        using var seedScope = _factory.Services.CreateScope();
        var tenant = seedScope.ServiceProvider.GetRequiredService<ICurrentTenant>();
        tenant.SetRestaurant(_restaurantId);
        var seedDb = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var cat = new Category { Id = Guid.NewGuid(), Name = "Pizza", SortOrder = 0 };
        var s1 = new Subcategory { Id = Guid.NewGuid(), CategoryId = cat.Id, Name = "Classic", SortOrder = 0 };
        var s2 = new Subcategory { Id = Guid.NewGuid(), CategoryId = cat.Id, Name = "Specialty", SortOrder = 1 };
        seedDb.AddRange(cat, s1, s2,
            new Product { Id = Guid.NewGuid(), CategoryId = cat.Id, Name = "Calzone", Price = 30m, IsAvailable = true },
            new Product { Id = Guid.NewGuid(), CategoryId = cat.Id, SubcategoryId = s1.Id, Name = "Margherita", Price = 28m, IsAvailable = true },
            new Product { Id = Guid.NewGuid(), CategoryId = cat.Id, SubcategoryId = s2.Id, Name = "Truffle", Price = 45m, IsAvailable = true });
        await seedDb.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        _adminClient.Dispose();
        _factory.Dispose();
        await _pg.DisposeAsync();
    }

    [Fact]
    public async Task CustomerMenu_returns_subcategories_per_category_and_subcategoryId_per_product()
    {
        using var anon = _factory.CreateClient();
        var res = await anon.GetAsync($"/api/restaurants/{_restaurantId}/menu");
        res.IsSuccessStatusCode.Should().BeTrue();
        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var category = doc.RootElement.GetProperty("categories")[0];
        category.GetProperty("subcategories").GetArrayLength().Should().Be(2);
        var products = category.GetProperty("products");
        var withSub = products.EnumerateArray()
            .Count(p => p.TryGetProperty("subcategoryId", out var v) && v.ValueKind == JsonValueKind.String);
        withSub.Should().Be(2);
    }

    [Fact]
    public async Task AdminMenu_returns_subcategories_per_category_and_subcategoryId_per_product()
    {
        var res = await _adminClient.GetAsync($"/api/restaurants/{_restaurantId}/admin/menu");
        res.IsSuccessStatusCode.Should().BeTrue();
        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var category = doc.RootElement.GetProperty("categories")[0];
        category.GetProperty("subcategories").GetArrayLength().Should().Be(2);
        var products = category.GetProperty("products");
        var withSub = products.EnumerateArray()
            .Count(p => p.TryGetProperty("subcategoryId", out var v) && v.ValueKind == JsonValueKind.String);
        withSub.Should().Be(2);
    }
}
