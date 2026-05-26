using System;
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

public class SubcategoryEntityTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
    private MenuImportTestFactory _factory = null!;
    private Guid _restaurantId;

    public async Task InitializeAsync()
    {
        await _pg.StartAsync();
        _factory = new MenuImportTestFactory { PostgresConnectionString = _pg.GetConnectionString() };
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        var (restA, _, _, _) = await MenuImportTestFixtures.SeedTwoTenantsAsync(_factory);
        _restaurantId = restA;
    }

    public async Task DisposeAsync()
    {
        _factory.Dispose();
        await _pg.DisposeAsync();
    }

    [Fact]
    public async Task RoundTrip_subcategory_persists_with_parent_category()
    {
        using var scope = _factory.Services.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
        tenant.SetRestaurant(_restaurantId);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var cat = new Category { Id = Guid.NewGuid(), Name = "Pizza" };
        var sub = new Subcategory { Id = Guid.NewGuid(), CategoryId = cat.Id, Name = "Classic", SortOrder = 0 };
        db.Categories.Add(cat);
        db.Subcategories.Add(sub);
        await db.SaveChangesAsync();

        var loaded = await db.Subcategories.SingleAsync(s => s.Id == sub.Id);
        loaded.Name.Should().Be("Classic");
        loaded.CategoryId.Should().Be(cat.Id);
        loaded.RestaurantId.Should().Be(_restaurantId);
    }

    [Fact]
    public async Task Product_subcategory_id_set_null_on_subcategory_delete()
    {
        using var scope = _factory.Services.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
        tenant.SetRestaurant(_restaurantId);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var cat = new Category { Id = Guid.NewGuid(), Name = "Pizza" };
        var sub = new Subcategory { Id = Guid.NewGuid(), CategoryId = cat.Id, Name = "Classic" };
        var prod = new Product { Id = Guid.NewGuid(), CategoryId = cat.Id, SubcategoryId = sub.Id, Name = "Margherita", Price = 28m };
        db.AddRange(cat, sub, prod);
        await db.SaveChangesAsync();

        db.Subcategories.Remove(sub);
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();

        var reloaded = await db.Products.SingleAsync(p => p.Id == prod.Id);
        reloaded.SubcategoryId.Should().BeNull();
        reloaded.CategoryId.Should().Be(cat.Id);
    }

    [Fact]
    public async Task Unique_index_blocks_duplicate_subcategory_name_within_category()
    {
        using var scope = _factory.Services.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
        tenant.SetRestaurant(_restaurantId);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var cat = new Category { Id = Guid.NewGuid(), Name = "Pizza" };
        db.Categories.Add(cat);
        db.Subcategories.Add(new Subcategory { Id = Guid.NewGuid(), CategoryId = cat.Id, Name = "Classic" });
        db.Subcategories.Add(new Subcategory { Id = Guid.NewGuid(), CategoryId = cat.Id, Name = "Classic" });

        var act = async () => await db.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }
}
