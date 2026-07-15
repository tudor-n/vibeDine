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
    private Guid _restaurantIdB;
    private Guid _inCartProductId;
    private Guid _candidateProductId;
    private Guid _unavailableProductId;
    private Guid _otherTenantProductId;

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

        var (restA, restB, _, _) = await MenuImportTestFixtures.SeedTwoTenantsAsync(_factory);
        _restaurantId = restA;
        _restaurantIdB = restB;

        using (var seedScope = _factory.Services.CreateScope())
        {
            var tenant = seedScope.ServiceProvider.GetRequiredService<ICurrentTenant>();
            tenant.SetRestaurant(_restaurantId);
            var seedDb = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();

            var category = new Category { Id = Guid.NewGuid(), Name = "Feluri principale", SortOrder = 0 };
            _inCartProductId = Guid.NewGuid();
            _candidateProductId = Guid.NewGuid();
            _unavailableProductId = Guid.NewGuid();
            seedDb.AddRange(
                category,
                new Product { Id = _inCartProductId, CategoryId = category.Id, Name = "Grătar mixt", Price = 45m, IsAvailable = true },
                new Product { Id = _candidateProductId, CategoryId = category.Id, Name = "Cartofi prăjiți", Price = 15.50m, IsAvailable = true },
                // Unavailable candidate must never be offered.
                new Product { Id = _unavailableProductId, CategoryId = category.Id, Name = "Supă epuizată", Price = 12m, IsAvailable = false });
            await seedDb.SaveChangesAsync();
        }

        // A product that belongs to restaurant B only — used to prove a
        // hallucinated/cross-tenant AI recommendation never leaks into
        // restaurant A's response (global tenant query filter drops it).
        using (var otherScope = _factory.Services.CreateScope())
        {
            var tenant = otherScope.ServiceProvider.GetRequiredService<ICurrentTenant>();
            tenant.SetRestaurant(_restaurantIdB);
            var otherDb = otherScope.ServiceProvider.GetRequiredService<AppDbContext>();

            var categoryB = new Category { Id = Guid.NewGuid(), Name = "Meniu B", SortOrder = 0 };
            _otherTenantProductId = Guid.NewGuid();
            otherDb.AddRange(
                categoryB,
                new Product { Id = _otherTenantProductId, CategoryId = categoryB.Id, Name = "Produs restaurant B", Price = 20m, IsAvailable = true });
            await otherDb.SaveChangesAsync();
        }

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
    public async Task Returns_graceful_empty_when_cart_exceeds_cap()
    {
        var items = Enumerable.Range(0, 51)
            .Select(_ => new VibeyCartItem(Guid.NewGuid().ToString(), "Item", "Feluri principale", 1))
            .ToList();

        var resp = await _client.PostAsJsonAsync(RecsUrl, new VibeyRecommendationRequest(items, "ro"));
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<VibeyRecommendationResponse>();
        body.Should().NotBeNull();
        body!.Summary.Should().BeEmpty();
        body.Recommendations.Should().BeEmpty();

        // Cap short-circuits before ever reaching the AI service — abusive payloads cost nothing.
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

    [Fact]
    public async Task Cross_tenant_product_is_dropped_even_if_ai_recommends_it()
    {
        // The fake AI (WireMock) "hallucinates" restaurant B's product into
        // restaurant A's response. Candidate selection is scoped to the current
        // tenant (global query filter), so restaurant B's product is never even
        // in the candidate set — it must be silently dropped, never returned.
        _python.Given(Request.Create().WithPath("/recommend").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody($$"""
              {
                "summary": "Iată o completare.",
                "recommendations": [
                  { "product_id": "{{_otherTenantProductId}}", "reason": "Ar trebui să meargă.", "confidence": 0.9 }
                ]
              }
            """));

        var resp = await _client.PostAsJsonAsync(RecsUrl, CartRequest());
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<VibeyRecommendationResponse>();
        body.Should().NotBeNull();
        body!.Recommendations.Should().NotContain(r => r.Id == _otherTenantProductId.ToString());
        body.Recommendations.Should().BeEmpty();
    }

    [Fact]
    public async Task Hallucinated_and_non_candidate_ids_are_dropped_while_a_valid_candidate_survives()
    {
        var hallucinatedId = Guid.NewGuid();

        // The fake AI response mixes one legitimate candidate with three ids
        // that must never survive: a fully made-up guid, an unavailable
        // product, and the item already in the cart.
        _python.Given(Request.Create().WithPath("/recommend").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody($$"""
              {
                "summary": "Iată completări.",
                "recommendations": [
                  { "product_id": "{{_candidateProductId}}", "reason": "Merge bine.", "confidence": 0.9 },
                  { "product_id": "{{hallucinatedId}}", "reason": "Halucinație.", "confidence": 0.5 },
                  { "product_id": "{{_unavailableProductId}}", "reason": "Nu mai există.", "confidence": 0.4 },
                  { "product_id": "{{_inCartProductId}}", "reason": "Deja în coș.", "confidence": 0.3 }
                ]
              }
            """));

        var resp = await _client.PostAsJsonAsync(RecsUrl, CartRequest());
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<VibeyRecommendationResponse>();
        body.Should().NotBeNull();
        body!.Recommendations.Should().ContainSingle();
        body.Recommendations[0].Id.Should().Be(_candidateProductId.ToString());
    }
}
