using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantApi.Data;
using RestaurantApi.DTOs.Responses;
using RestaurantApi.Tests.Helpers;
using Testcontainers.PostgreSql;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace RestaurantApi.Tests;

public class MenuScanControllerTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
    private WireMockServer _python = null!;
    private MenuImportTestFactory _factory = null!;
    private HttpClient _client = null!;
    private Guid _restaurantId;

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
        var (restA, _, clientA, _) = await MenuImportTestFixtures.SeedTwoTenantsAsync(_factory);
        _restaurantId = restA;
        _client = clientA;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        _python.Stop();
        await _pg.DisposeAsync();
    }

    private string ScanUrl => $"/api/restaurants/{_restaurantId}/admin/menu/scan";

    private static MultipartFormDataContent OnePhoto()
    {
        var form = new MultipartFormDataContent();
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(content, "photos", "p0.png");
        return form;
    }

    [Fact]
    public async Task Happy_path_forwards_to_python_and_returns_items()
    {
        _python.Given(Request.Create().WithPath("/menu/extract").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("""
              { "items": [{ "name":"Ciorbă","price":18.0,"source_page":0,"source_index":0,"description":null,"category_hint":"Ciorbe","confidence":null }],
                "failed_pages": [], "model":"fake","schema_version":1 }
            """));

        var resp = await _client.PostAsync(ScanUrl, OnePhoto());
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<MenuScanResponse>();
        body!.Items.Should().HaveCount(1);
        body.Items[0].CategoryHint.Should().Be("Ciorbe");
    }

    [Fact]
    public async Task Returns_502_when_python_unreachable()
    {
        _python.Stop();
        var resp = await _client.PostAsync(ScanUrl, OnePhoto());
        resp.StatusCode.Should().Be(HttpStatusCode.BadGateway);
    }

    [Fact]
    public async Task Returns_413_when_payload_exceeds_cap()
    {
        var form = new MultipartFormDataContent();
        for (var i = 0; i < 11; i++)
        {
            var content = new ByteArrayContent(new byte[] { 1 });
            content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(content, "photos", $"p{i}.png");
        }
        var resp = await _client.PostAsync(ScanUrl, form);
        resp.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task Returns_400_when_no_photos()
    {
        var resp = await _client.PostAsync(ScanUrl, new MultipartFormDataContent());
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
