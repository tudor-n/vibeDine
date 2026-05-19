using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RestaurantApi.DTOs.Responses;
using RestaurantApi.Services;
using RestaurantApi.Tests.Helpers;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace RestaurantApi.Tests;

public class MenuScanControllerTests : IAsyncLifetime
{
    private WireMockServer _python = null!;
    private TestWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        _python = WireMockServer.Start();
        _factory = new TestWebApplicationFactory
        {
            // BulkCreate tests will overwrite this — scan tests only need /menu/extract reachable.
            PostgresConnectionString = "Host=localhost;Database=ignored;Username=x;Password=x",
            PythonAiBaseUrl = _python.Url!,
        };
        _client = _factory.CreateClient();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        _python.Stop();
        return Task.CompletedTask;
    }

    private MultipartFormDataContent OnePhoto()
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

        var resp = await _client.PostAsync("/api/admin/menu/scan", OnePhoto());
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<MenuScanResponse>();
        body!.Items.Should().HaveCount(1);
        body.Items[0].CategoryHint.Should().Be("Ciorbe");
    }

    [Fact]
    public async Task Returns_502_when_python_unreachable()
    {
        _python.Stop();
        var resp = await _client.PostAsync("/api/admin/menu/scan", OnePhoto());
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
        var resp = await _client.PostAsync("/api/admin/menu/scan", form);
        resp.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task Returns_400_when_no_photos()
    {
        var resp = await _client.PostAsync("/api/admin/menu/scan", new MultipartFormDataContent());
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
