using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantApi.Data;
using RestaurantApi.DTOs.Requests;
using RestaurantApi.DTOs.Responses;
using RestaurantApi.Models;
using RestaurantApi.Tenancy;
using RestaurantApi.Tests.Helpers;
using Testcontainers.PostgreSql;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace RestaurantApi.Tests;

public class VibeyRecommendationsTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
    private WireMockServer _python = null!;
    private MenuImportTestFactory _factory = null!;
    private HttpClient _client = null!;
    private Guid _restaurantId;
    private Guid _inCartProductId;
    private Guid _candidateProductId;

    public async Task InitializeAsync()
    {
        await _pg.StartAsync();
        _python = WireMockServer.Start();
        _factory = new MenuImportTestFactory
        {
            PostgresConnectionString = _pg.GetConnectionString(),
            PythonAiBaseUrl = _python.Url!,
        };
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.MigrateAsync();
        }

        var (restA, _, _, _) = await MenuImportTestFixtures.SeedTwoTenantsAsync(_factory);
        _restaurantId = restA;

        using var seedScope = _factory.Services.CreateScope();
        var tenant = seedScope.ServiceProvider.GetRequiredService<ICurrentTenant>();
        tenant.SetRestaurant(_restaurantId);
        var seedDb = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var category = new Category { Id = Guid.NewGuid(), Name = "Feluri principale", SortOrder = 0 };
        _inCartProductId = Guid.NewGuid();
        _candidateProductId = Guid.NewGuid();
        seedDb.AddRange(
            category,
            new Product { Id = _inCartProductId, CategoryId = category.Id, Name = "Grătar mixt", Price = 45m, IsAvailable = true },
            new Product { Id = _candidateProductId, CategoryId = category.Id, Name = "Cartofi prăjiți", Price = 15.50m, IsAvailable = true },
            // Unavailable candidate must never be offered.
            new Product { Id = Guid.NewGuid(), CategoryId = category.Id, Name = "Supă epuizată", Price = 12m, IsAvailable = false });
        await seedDb.SaveChangesAsync();

        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        _python.Stop();
        await _pg.DisposeAsync();
    }

    private string RecsUrl => $"/api/restaurants/{_restaurantId}/recommendations";

    private VibeyRecommendationRequest CartRequest(string locale = "ro") => new(
        Items: new List<VibeyCartItem> { new(_inCartProductId.ToString(), "Grătar mixt", "Feluri principale", 1) },
        Locale: locale);

    [Fact]
    public async Task Happy_path_returns_enriched_recommendations_and_summary()
    {
        _python.Given(Request.Create().WithPath("/recommend").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody($$"""
              {
                "summary": "Am găsit câteva completări care merg de minune cu ce ai ales.",
                "recommendations": [
                  { "product_id": "{{_candidateProductId}}", "reason": "Merge perfect cu grătarul.", "confidence": 0.9 }
                ]
              }
            """));

        var resp = await _client.PostAsJsonAsync(RecsUrl, CartRequest());
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<VibeyRecommendationResponse>();
        body.Should().NotBeNull();
        body!.Summary.Should().Be("Am găsit câteva completări care merg de minune cu ce ai ales.");
        body.Recommendations.Should().ContainSingle();

        var rec = body.Recommendations[0];
        rec.Id.Should().Be(_candidateProductId.ToString());
        rec.Name.Should().Be("Cartofi prăjiți");
        rec.Category.Should().Be("Feluri principale");
        rec.PriceRon.Should().Be(15.50m);
        rec.Reason.Should().Be("Merge perfect cu grătarul.");
    }

    [Fact]
    public async Task Returns_graceful_empty_when_ai_service_500s()
    {
        _python.Given(Request.Create().WithPath("/recommend").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(500).WithBody("boom"));

        var resp = await _client.PostAsJsonAsync(RecsUrl, CartRequest());
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<VibeyRecommendationResponse>();
        body.Should().NotBeNull();
        body!.Summary.Should().BeEmpty();
        body.Recommendations.Should().BeEmpty();
    }

    [Fact]
    public async Task Returns_graceful_empty_when_items_missing()
    {
        var resp = await _client.PostAsJsonAsync(RecsUrl, new VibeyRecommendationRequest(new List<VibeyCartItem>(), "ro"));
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<VibeyRecommendationResponse>();
        body.Should().NotBeNull();
        body!.Summary.Should().BeEmpty();
        body.Recommendations.Should().BeEmpty();

        // Guard short-circuits before ever reaching the AI service.
        _python.LogEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task Identical_cart_is_served_from_cache_without_a_second_ai_call()
    {
        _python.Given(Request.Create().WithPath("/recommend").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody($$"""
              {
                "summary": "Iată câteva idei.",
                "recommendations": [
                  { "product_id": "{{_candidateProductId}}", "reason": "Merge bine.", "confidence": 0.7 }
                ]
              }
            """));

        var first = await _client.PostAsJsonAsync(RecsUrl, CartRequest());
        var second = await _client.PostAsJsonAsync(RecsUrl, CartRequest());

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);

        var firstBody = await first.Content.ReadFromJsonAsync<VibeyRecommendationResponse>();
        var secondBody = await second.Content.ReadFromJsonAsync<VibeyRecommendationResponse>();
        secondBody.Should().BeEquivalentTo(firstBody);

        _python.LogEntries.Count(e => e.RequestMessage.Path == "/recommend").Should().Be(1);
    }
}
