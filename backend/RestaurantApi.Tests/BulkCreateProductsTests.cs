using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantApi.Data;
using RestaurantApi.DTOs.Requests;
using RestaurantApi.Models;
using RestaurantApi.Tests.Helpers;
using Testcontainers.PostgreSql;
using Xunit;

namespace RestaurantApi.Tests;

public class BulkCreateProductsTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private TestWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;
    private Guid _existingCategoryId;

    public async Task InitializeAsync()
    {
        await _pg.StartAsync();
        _factory = new TestWebApplicationFactory
        {
            PostgresConnectionString = _pg.GetConnectionString(),
            PythonAiBaseUrl = "http://localhost:0",
        };
        _client = _factory.CreateClient();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        var cat = new Category { Id = Guid.NewGuid(), Name = "Aperitive", SortOrder = 0 };
        db.Categories.Add(cat);
        await db.SaveChangesAsync();
        _existingCategoryId = cat.Id;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        await _pg.DisposeAsync();
    }

    [Fact]
    public async Task Happy_path_creates_categories_and_products_atomically()
    {
        var tempId = Guid.NewGuid();
        var request = new BulkCreateProductsRequest(
            Items: new List<BulkCreateProductItem>
            {
                new("Ciorbă", 18.00m, _existingCategoryId),
                new("Tort", 22.00m, tempId),
            },
            NewCategories: new List<BulkCreateCategoryItem>
            {
                new(tempId, "Deserturi"),
            });

        var resp = await _client.PostAsJsonAsync("/api/admin/menu/products/bulk", request);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Categories.CountAsync()).Should().Be(2);
        (await db.Products.CountAsync()).Should().Be(2);
        var dessertCat = await db.Categories.SingleAsync(c => c.Name == "Deserturi");
        (await db.Products.CountAsync(p => p.CategoryId == dessertCat.Id)).Should().Be(1);
    }

    [Fact]
    public async Task Validation_failure_on_one_item_rolls_back_everything()
    {
        var tempId = Guid.NewGuid();
        var request = new BulkCreateProductsRequest(
            Items: new List<BulkCreateProductItem>
            {
                new("Ciorbă", 18.00m, _existingCategoryId),
                new("", 1.00m, _existingCategoryId),  // invalid: empty name
            },
            NewCategories: new List<BulkCreateCategoryItem> { new(tempId, "Should not be created") });

        var resp = await _client.PostAsJsonAsync("/api/admin/menu/products/bulk", request);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Categories.CountAsync()).Should().Be(1);  // only seed
        (await db.Products.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Unknown_category_id_returns_409()
    {
        var bogusId = Guid.NewGuid();
        var request = new BulkCreateProductsRequest(
            Items: new List<BulkCreateProductItem> { new("Ciorbă", 18.00m, bogusId) },
            NewCategories: new List<BulkCreateCategoryItem>());

        var resp = await _client.PostAsJsonAsync("/api/admin/menu/products/bulk", request);
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await resp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        body.Should().ContainKey("missingCategoryIds");
    }

    [Fact]
    public async Task New_category_with_existing_name_is_merged_into_existing()
    {
        var tempId = Guid.NewGuid();
        var request = new BulkCreateProductsRequest(
            Items: new List<BulkCreateProductItem> { new("Salată", 12.00m, tempId) },
            NewCategories: new List<BulkCreateCategoryItem> { new(tempId, "  APERITIVE  ") }); // normalises to "aperitive"

        var resp = await _client.PostAsJsonAsync("/api/admin/menu/products/bulk", request);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Categories.CountAsync()).Should().Be(1);  // existing "Aperitive" reused
        var product = await db.Products.SingleAsync();
        product.CategoryId.Should().Be(_existingCategoryId);
    }
}
