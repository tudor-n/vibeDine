using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantApi.Data;
using RestaurantApi.DTOs.Responses;
using RestaurantApi.Tests.Helpers;
using RestaurantApi.Tenancy;
using Testcontainers.PostgreSql;
using Xunit;

namespace RestaurantApi.Tests;

public class BulkCreateHierarchyTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
    private MenuImportTestFactory _factory = null!;
    private HttpClient _client = null!;
    private Guid _restaurantId;

    public async Task InitializeAsync()
    {
        await _pg.StartAsync();
        _factory = new MenuImportTestFactory { PostgresConnectionString = _pg.GetConnectionString() };
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
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

    [Fact]
    public async Task HappyPath_creates_categories_subcategories_and_products()
    {
        var req = new
        {
            categories = new[]
            {
                new
                {
                    tempId = "c1", name = "Pizza", sortOrder = 0,
                    subcategories = new[] { new { tempId = "s1", name = "Classic", sortOrder = 0 } }
                }
            },
            products = new[]
            {
                new { name = "Margherita", price = 28m, description = "", categoryTempId = "c1", subcategoryTempId = "s1", sortOrder = 0, isAvailable = true }
            }
        };

        var res = await _client.PostAsJsonAsync($"/api/restaurants/{_restaurantId}/admin/menu/bulk-create-hierarchy", req);
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await res.Content.ReadFromJsonAsync<BulkCreateHierarchyResponse>();
        body!.CreatedCategories.Should().HaveCount(1);
        body.CreatedSubcategories.Should().HaveCount(1);
        body.CreatedProducts.Should().HaveCount(1);

        using var scope = _factory.Services.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
        tenant.SetRestaurant(_restaurantId);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var products = await db.Products.ToListAsync();
        products.Should().HaveCount(1);
        products[0].SubcategoryId.Should().NotBeNull();
    }

    [Fact]
    public async Task SubcategoryTempId_referencing_other_category_is_rejected()
    {
        var req = new
        {
            categories = new object[]
            {
                new { tempId = "c1", name = "Pizza", sortOrder = 0,
                      subcategories = new[] { new { tempId = "s1", name = "Classic", sortOrder = 0 } } },
                new { tempId = "c2", name = "Salads", sortOrder = 1, subcategories = Array.Empty<object>() }
            },
            products = new[]
            {
                new { name = "Caesar", price = 15m, description = "", categoryTempId = "c2", subcategoryTempId = "s1", sortOrder = 0, isAvailable = true }
            }
        };
        var res = await _client.PostAsJsonAsync($"/api/restaurants/{_restaurantId}/admin/menu/bulk-create-hierarchy", req);
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DuplicateCategoryTempIds_rejected()
    {
        var req = new
        {
            categories = new[]
            {
                new { tempId = "c1", name = "Pizza", sortOrder = 0, subcategories = Array.Empty<object>() },
                new { tempId = "c1", name = "Pasta", sortOrder = 1, subcategories = Array.Empty<object>() }
            },
            products = Array.Empty<object>()
        };
        var res = await _client.PostAsJsonAsync($"/api/restaurants/{_restaurantId}/admin/menu/bulk-create-hierarchy", req);
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task EmptyRequest_returns_BadRequest()
    {
        var req = new { categories = Array.Empty<object>(), products = Array.Empty<object>() };
        var res = await _client.PostAsJsonAsync($"/api/restaurants/{_restaurantId}/admin/menu/bulk-create-hierarchy", req);
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Tenant_is_stamped_on_all_created_entities()
    {
        var req = new
        {
            categories = new[]
            {
                new { tempId = "c1", name = "Pizza", sortOrder = 0,
                      subcategories = new[] { new { tempId = "s1", name = "Classic", sortOrder = 0 } } }
            },
            products = new[]
            {
                new { name = "Margherita", price = 28m, description = "", categoryTempId = "c1", subcategoryTempId = "s1", sortOrder = 0, isAvailable = true }
            }
        };
        var res = await _client.PostAsJsonAsync($"/api/restaurants/{_restaurantId}/admin/menu/bulk-create-hierarchy", req);
        res.IsSuccessStatusCode.Should().BeTrue();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
        using var _ = tenant.SuppressResolution();
        (await db.Categories.IgnoreQueryFilters().AllAsync(c => c.RestaurantId == _restaurantId)).Should().BeTrue();
        (await db.Subcategories.IgnoreQueryFilters().AllAsync(s => s.RestaurantId == _restaurantId)).Should().BeTrue();
        (await db.Products.IgnoreQueryFilters().AllAsync(p => p.RestaurantId == _restaurantId)).Should().BeTrue();
    }

    [Fact]
    public async Task DuplicateSubcategoryTempIds_rejected()
    {
        var req = new
        {
            categories = new[]
            {
                new
                {
                    tempId = "c1", name = "Pizza", sortOrder = 0,
                    subcategories = new[]
                    {
                        new { tempId = "s1", name = "Classic", sortOrder = 0 },
                        new { tempId = "s1", name = "Specialty", sortOrder = 1 },
                    }
                }
            },
            products = Array.Empty<object>()
        };
        var res = await _client.PostAsJsonAsync($"/api/restaurants/{_restaurantId}/admin/menu/bulk-create-hierarchy", req);
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task BlankCategoryTempId_rejected()
    {
        var req = new
        {
            categories = new[]
            {
                new { tempId = "   ", name = "Pizza", sortOrder = 0, subcategories = Array.Empty<object>() }
            },
            products = Array.Empty<object>()
        };
        var res = await _client.PostAsJsonAsync($"/api/restaurants/{_restaurantId}/admin/menu/bulk-create-hierarchy", req);
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task InvalidProductName_rejected()
    {
        var req = new
        {
            categories = new[]
            {
                new { tempId = "c1", name = "Pizza", sortOrder = 0, subcategories = Array.Empty<object>() }
            },
            products = new[]
            {
                new { name = "", price = 28m, description = "", categoryTempId = "c1", subcategoryTempId = (string?)null, sortOrder = 0, isAvailable = true }
            }
        };
        var res = await _client.PostAsJsonAsync($"/api/restaurants/{_restaurantId}/admin/menu/bulk-create-hierarchy", req);
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UnknownCategoryTempId_rejected()
    {
        var req = new
        {
            categories = new[]
            {
                new { tempId = "c1", name = "Pizza", sortOrder = 0, subcategories = Array.Empty<object>() }
            },
            products = new[]
            {
                new { name = "Margherita", price = 28m, description = "", categoryTempId = "unknown", subcategoryTempId = (string?)null, sortOrder = 0, isAvailable = true }
            }
        };
        var res = await _client.PostAsJsonAsync($"/api/restaurants/{_restaurantId}/admin/menu/bulk-create-hierarchy", req);
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ProductWithoutSubcategory_creates_product_directly_under_category()
    {
        var req = new
        {
            categories = new[]
            {
                new { tempId = "c1", name = "Pizza", sortOrder = 0, subcategories = Array.Empty<object>() }
            },
            products = new[]
            {
                new { name = "Calzone", price = 30m, description = "", categoryTempId = "c1", subcategoryTempId = (string?)null, sortOrder = 0, isAvailable = true }
            }
        };
        var res = await _client.PostAsJsonAsync($"/api/restaurants/{_restaurantId}/admin/menu/bulk-create-hierarchy", req);
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
        tenant.SetRestaurant(_restaurantId);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var products = await db.Products.ToListAsync();
        products.Should().HaveCount(1);
        products[0].SubcategoryId.Should().BeNull();
    }

    [Fact]
    public async Task SortOrder_is_assigned_per_bucket_in_input_order()
    {
        var req = new
        {
            categories = new[]
            {
                new
                {
                    tempId = "c1", name = "Pizza", sortOrder = 0,
                    subcategories = new[] { new { tempId = "s1", name = "Classic", sortOrder = 0 } }
                }
            },
            products = new[]
            {
                new { name = "Alpha",  price = 10m, description = "", categoryTempId = "c1", subcategoryTempId = "s1", sortOrder = 99, isAvailable = true },
                new { name = "Beta",   price = 11m, description = "", categoryTempId = "c1", subcategoryTempId = "s1", sortOrder = 99, isAvailable = true },
                new { name = "Gamma",  price = 12m, description = "", categoryTempId = "c1", subcategoryTempId = (string?)null, sortOrder = 99, isAvailable = true }
            }
        };
        var res = await _client.PostAsJsonAsync($"/api/restaurants/{_restaurantId}/admin/menu/bulk-create-hierarchy", req);
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
        tenant.SetRestaurant(_restaurantId);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var products = await db.Products.OrderBy(p => p.Name).ToListAsync();
        var alpha = products.Single(p => p.Name == "Alpha");
        var beta = products.Single(p => p.Name == "Beta");
        var gamma = products.Single(p => p.Name == "Gamma");
        alpha.SortOrder.Should().Be(0);
        beta.SortOrder.Should().Be(1);  // Same bucket as Alpha
        gamma.SortOrder.Should().Be(0); // Different bucket (no subcategory)
    }

    [Fact]
    public async Task Product_image_url_is_persisted_and_invalid_is_rejected()
    {
        var ok = new
        {
            categories = new[] { new { tempId = "c1", name = "Pizza", sortOrder = 0, subcategories = Array.Empty<object>() } },
            products = new[]
            {
                new { name = "Margherita", price = 28m, description = "", categoryTempId = "c1",
                      subcategoryTempId = (string?)null, sortOrder = 0, isAvailable = true,
                      imageUrl = "/uploads/menu-items/abc/def.jpg" }
            }
        };
        var res = await _client.PostAsJsonAsync($"/api/restaurants/{_restaurantId}/admin/menu/bulk-create-hierarchy", ok);
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = _factory.Services.CreateScope())
        {
            var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
            tenant.SetRestaurant(_restaurantId);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Products.SingleAsync()).ImageUrl.Should().Be("/uploads/menu-items/abc/def.jpg");
        }

        var bad = new
        {
            categories = new[] { new { tempId = "c2", name = "Salads", sortOrder = 1, subcategories = Array.Empty<object>() } },
            products = new[]
            {
                new { name = "Caesar", price = 15m, description = "", categoryTempId = "c2",
                      subcategoryTempId = (string?)null, sortOrder = 0, isAvailable = true,
                      imageUrl = "//evil.com/x.jpg" }
            }
        };
        var badRes = await _client.PostAsJsonAsync($"/api/restaurants/{_restaurantId}/admin/menu/bulk-create-hierarchy", bad);
        badRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
