using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using RestaurantApi.Data;
using RestaurantApi.Models;
using RestaurantApi.Tenancy;

namespace RestaurantApi.Tests.Helpers;

/// <summary>
/// One restaurant with a manager and two waiters, each with a pre-authenticated client.
/// Waiter Mode's rules are almost all about *who* is acting (owner vs colleague vs
/// manager), so every suite needs this same cast.
/// </summary>
public sealed class WaiterWorld
{
    public required Guid OrganizationId { get; init; }
    public required Guid RestaurantId { get; init; }

    public required Guid ManagerUserId { get; init; }
    public required Guid WaiterAUserId { get; init; }
    public required Guid WaiterBUserId { get; init; }

    /// <summary>RestaurantAdmin — passes CanManageRestaurantAsync, bypasses the waiter allow-list.</summary>
    public required HttpClient Manager { get; init; }
    /// <summary>Active Waiter membership — subject to the allow-list.</summary>
    public required HttpClient WaiterA { get; init; }
    public required HttpClient WaiterB { get; init; }

    public string WaiterUrl => $"/api/restaurants/{RestaurantId}/waiter";

    public void Dispose()
    {
        Manager.Dispose();
        WaiterA.Dispose();
        WaiterB.Dispose();
    }
}

public static class WaiterModeTestFixtures
{
    public const string ManagerName = "Manager Test";
    public const string WaiterAName = "Ana Ospătar";
    public const string WaiterBName = "Bogdan Ospătar";

    public static async Task<WaiterWorld> SeedAsync(MenuImportTestFactory factory)
    {
        var orgId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var waiterAId = Guid.NewGuid();
        var waiterBId = Guid.NewGuid();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

            await CreateUserAsync(users, managerId, ManagerName);
            await CreateUserAsync(users, waiterAId, WaiterAName);
            await CreateUserAsync(users, waiterBId, WaiterBName);

            db.Set<Organization>().Add(new Organization
            {
                Id = orgId,
                Name = "Waiter Mode Org",
                Slug = "wmo-" + orgId.ToString("N")[..8],
                CreatedByUserId = managerId,
            });
            db.Restaurants.Add(new Restaurant
            {
                Id = restaurantId,
                OrganizationId = orgId,
                Name = "Waiter Mode Restaurant",
                Slug = "wmr-" + restaurantId.ToString("N")[..8],
                IsActive = true,
            });
            await db.SaveChangesAsync();

            db.Memberships.AddRange(
                new Membership { Id = Guid.NewGuid(), UserId = managerId, OrganizationId = orgId, RestaurantId = restaurantId, Role = MembershipRole.RestaurantAdmin, Status = MembershipStatus.Active },
                new Membership { Id = Guid.NewGuid(), UserId = waiterAId, OrganizationId = orgId, RestaurantId = restaurantId, Role = MembershipRole.Waiter, Status = MembershipStatus.Active },
                new Membership { Id = Guid.NewGuid(), UserId = waiterBId, OrganizationId = orgId, RestaurantId = restaurantId, Role = MembershipRole.Waiter, Status = MembershipStatus.Active });
            await db.SaveChangesAsync();
        }

        return new WaiterWorld
        {
            OrganizationId = orgId,
            RestaurantId = restaurantId,
            ManagerUserId = managerId,
            WaiterAUserId = waiterAId,
            WaiterBUserId = waiterBId,
            Manager = factory.CreateAuthenticatedClient(managerId, restaurantId),
            WaiterA = factory.CreateAuthenticatedClient(waiterAId, restaurantId),
            WaiterB = factory.CreateAuthenticatedClient(waiterBId, restaurantId),
        };
    }

    private static async Task CreateUserAsync(UserManager<AppUser> users, Guid id, string fullName)
    {
        var email = $"{id:N}@waiter.test";
        var result = await users.CreateAsync(
            new AppUser { Id = id, UserName = email, Email = email, FullName = fullName },
            "TestPass1!");
        if (!result.Succeeded)
            throw new InvalidOperationException("Seed user creation failed: " + string.Join("; ", result.Errors.Select(e => e.Description)));
    }

    /// <summary>A live table. The QR token is returned so customer-flow tests can scan it.</summary>
    public static async Task<(Guid TableId, Guid QrToken)> AddTableAsync(
        MenuImportTestFactory factory, Guid restaurantId, string tableNumber)
    {
        var tableId = Guid.NewGuid();
        var qrToken = Guid.NewGuid();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Tables.Add(new RestaurantTable
        {
            Id = tableId,
            RestaurantId = restaurantId,
            TableNumber = tableNumber,
            QrToken = qrToken,
        });
        await db.SaveChangesAsync();
        return (tableId, qrToken);
    }

    /// <summary>An untracked product — ordering it never touches stock.</summary>
    public static async Task<Guid> AddProductAsync(
        MenuImportTestFactory factory, Guid restaurantId, string name, decimal price)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var category = new Category { Id = Guid.NewGuid(), RestaurantId = restaurantId, Name = "Categorie " + name, SortOrder = 0 };
        db.Set<Category>().Add(category);

        var product = new Product
        {
            Id = Guid.NewGuid(),
            RestaurantId = restaurantId,
            CategoryId = category.Id,
            Name = name,
            Price = price,
            IsAvailable = true,
            StockTrackingMode = StockTrackingMode.Untracked,
        };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return product.Id;
    }

    /// <summary>
    /// A recipe-tracked product consuming one unit of a dedicated ingredient per portion,
    /// so a single decrement is observable as an exact quantity drop.
    /// </summary>
    public static async Task<(Guid ProductId, Guid IngredientId)> AddRecipeProductAsync(
        MenuImportTestFactory factory, Guid restaurantId, string name, decimal price, decimal stockOnHand)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var category = new Category { Id = Guid.NewGuid(), RestaurantId = restaurantId, Name = "Categorie " + name, SortOrder = 0 };
        db.Set<Category>().Add(category);

        var product = new Product
        {
            Id = Guid.NewGuid(),
            RestaurantId = restaurantId,
            CategoryId = category.Id,
            Name = name,
            Price = price,
            IsAvailable = true,
            StockTrackingMode = StockTrackingMode.Recipe,
        };
        db.Products.Add(product);

        var ingredient = new Ingredient
        {
            Id = Guid.NewGuid(),
            RestaurantId = restaurantId,
            Name = "Ingredient " + name,
            Unit = StockUnit.Piece,
            QuantityOnHand = stockOnHand,
        };
        db.Ingredients.Add(ingredient);
        await db.SaveChangesAsync();

        db.RecipeItems.Add(new RecipeItem
        {
            Id = Guid.NewGuid(),
            RestaurantId = restaurantId,
            ProductId = product.Id,
            IngredientId = ingredient.Id,
            QuantityPerPortion = 1m,
        });
        await db.SaveChangesAsync();

        return (product.Id, ingredient.Id);
    }

    /// <summary>Runs <paramref name="action"/> against a DbContext scoped to the given restaurant.</summary>
    public static async Task WithDbAsync(MenuImportTestFactory factory, Guid restaurantId, Func<AppDbContext, Task> action)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICurrentTenant>().SetRestaurant(restaurantId);
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public static async Task<T> WithDbAsync<T>(MenuImportTestFactory factory, Guid restaurantId, Func<AppDbContext, Task<T>> action)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICurrentTenant>().SetRestaurant(restaurantId);
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }
}
