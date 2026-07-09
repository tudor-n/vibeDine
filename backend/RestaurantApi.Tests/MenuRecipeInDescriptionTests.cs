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

// The customer menu surfaces a recipe-tracked dish's ingredients (name + per-portion quantity
// + unit) so the customer app can render them as a subtle line under the description.
public class MenuRecipeInDescriptionTests : IAsyncLifetime
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

        // A recipe-tracked dish with two ingredients...
        var margherita = new Product
        {
            Id = Guid.NewGuid(), CategoryId = cat.Id, Name = "Margherita",
            Description = "Clasica napoletană", Price = 32m, IsAvailable = true,
            StockTrackingMode = StockTrackingMode.Recipe,
        };
        // ...and an untracked one, which must carry no recipe.
        var calzone = new Product
        {
            Id = Guid.NewGuid(), CategoryId = cat.Id, Name = "Calzone",
            Price = 30m, IsAvailable = true, StockTrackingMode = StockTrackingMode.Untracked,
        };

        var mozzarella = new Ingredient { Id = Guid.NewGuid(), Name = "mozzarella", Unit = StockUnit.Gram, QuantityOnHand = 5000m, LowStockThreshold = 500m };
        var rosii = new Ingredient { Id = Guid.NewGuid(), Name = "roșii", Unit = StockUnit.Gram, QuantityOnHand = 5000m, LowStockThreshold = 500m };

        seedDb.AddRange(cat, margherita, calzone, mozzarella, rosii,
            new RecipeItem { Id = Guid.NewGuid(), ProductId = margherita.Id, IngredientId = mozzarella.Id, QuantityPerPortion = 80m },
            new RecipeItem { Id = Guid.NewGuid(), ProductId = margherita.Id, IngredientId = rosii.Id, QuantityPerPortion = 100m });
        await seedDb.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        _adminClient.Dispose();
        _factory.Dispose();
        await _pg.DisposeAsync();
    }

    [Fact]
    public async Task CustomerMenu_returns_recipe_ingredients_for_recipe_tracked_product()
    {
        using var anon = _factory.CreateClient();
        var res = await anon.GetAsync($"/api/restaurants/{_restaurantId}/menu");
        res.IsSuccessStatusCode.Should().BeTrue();

        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var products = doc.RootElement.GetProperty("categories")[0].GetProperty("products");

        var margherita = products.EnumerateArray().First(p => p.GetProperty("name").GetString() == "Margherita");
        margherita.TryGetProperty("recipe", out var recipe).Should().BeTrue();
        recipe.ValueKind.Should().Be(JsonValueKind.Array);
        recipe.GetArrayLength().Should().Be(2);

        var mozz = recipe.EnumerateArray().First(r => r.GetProperty("ingredientName").GetString() == "mozzarella");
        mozz.GetProperty("unit").GetString().Should().Be("gram");
        mozz.GetProperty("quantityPerPortion").GetDecimal().Should().Be(80m);
    }

    [Fact]
    public async Task CustomerMenu_omits_recipe_for_untracked_product()
    {
        using var anon = _factory.CreateClient();
        var res = await anon.GetAsync($"/api/restaurants/{_restaurantId}/menu");
        res.IsSuccessStatusCode.Should().BeTrue();

        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var products = doc.RootElement.GetProperty("categories")[0].GetProperty("products");

        var calzone = products.EnumerateArray().First(p => p.GetProperty("name").GetString() == "Calzone");
        var hasRecipe = calzone.TryGetProperty("recipe", out var cRecipe)
            && cRecipe.ValueKind == JsonValueKind.Array
            && cRecipe.GetArrayLength() > 0;
        hasRecipe.Should().BeFalse();
    }
}
