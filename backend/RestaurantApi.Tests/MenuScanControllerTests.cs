using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
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

    // ── Item photo extraction ───────────────────────────────────────────────

    // FF D8 FF E0 .. — passes ItemPhotoStorage's JPEG magic check.
    private static readonly string TinyJpegBase64 =
        Convert.ToBase64String(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46 });

    private string MenuItemsDir
    {
        get
        {
            var env = _factory.Services.GetRequiredService<IWebHostEnvironment>();
            return Path.Combine(env.ContentRootPath, "wwwroot", "uploads", "menu-items", _restaurantId.ToString("N"));
        }
    }

    private static MultipartFormDataContent OnePhotoWith(string? extractItemPhotos)
    {
        var form = OnePhoto();
        if (extractItemPhotos is not null)
            form.Add(new StringContent(extractItemPhotos), "extractItemPhotos");
        return form;
    }

    [Fact]
    public async Task With_flag_on_persists_photo_and_stamps_image_url()
    {
        _python.Given(Request.Create().WithPath("/menu/extract").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody($$"""
              { "items": [{ "name":"Pizza","price":28.0,"source_page":0,"source_index":0,"description":null,"category_hint":"Pizza","confidence":0.9 }],
                "failed_pages": [], "model":"fake","schema_version":2,
                "item_photos": [{ "source_page":0, "source_index":0, "image_base64":"{{TinyJpegBase64}}", "confidence":0.9 }] }
            """));

        try
        {
            var resp = await _client.PostAsync(ScanUrl, OnePhotoWith("true"));
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await resp.Content.ReadFromJsonAsync<MenuScanResponse>();
            body!.Items[0].ImageUrl.Should().NotBeNull();
            body.Items[0].ImageUrl!.Should().StartWith($"/uploads/menu-items/{_restaurantId:N}/");

            var env = _factory.Services.GetRequiredService<IWebHostEnvironment>();
            var relative = body.Items[0].ImageUrl!.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            File.Exists(Path.Combine(env.ContentRootPath, "wwwroot", relative)).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(MenuItemsDir)) Directory.Delete(MenuItemsDir, recursive: true);
        }
    }

    [Fact]
    public async Task With_flag_off_does_not_forward_part_and_leaves_image_url_null()
    {
        _python.Given(Request.Create().WithPath("/menu/extract").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("""
              { "items": [{ "name":"Pizza","price":28.0,"source_page":0,"source_index":0,"description":null,"category_hint":"Pizza","confidence":0.9 }],
                "failed_pages": [], "model":"fake","schema_version":2 }
            """));

        var resp = await _client.PostAsync(ScanUrl, OnePhoto());
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<MenuScanResponse>();
        body!.Items[0].ImageUrl.Should().BeNull();

        var upstreamBody = _python.LogEntries.Last().RequestMessage.Body ?? string.Empty;
        upstreamBody.Should().NotContain("extract_item_photos");
    }

    [Fact]
    public async Task With_broken_base64_returns_200_with_null_image_url()
    {
        _python.Given(Request.Create().WithPath("/menu/extract").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("""
              { "items": [{ "name":"Pizza","price":28.0,"source_page":0,"source_index":0,"description":null,"category_hint":"Pizza","confidence":0.9 }],
                "failed_pages": [], "model":"fake","schema_version":2,
                "item_photos": [{ "source_page":0, "source_index":0, "image_base64":"!!!not-base64!!!", "confidence":0.9 }] }
            """));

        try
        {
            var resp = await _client.PostAsync(ScanUrl, OnePhotoWith("true"));
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await resp.Content.ReadFromJsonAsync<MenuScanResponse>();
            body!.Items[0].ImageUrl.Should().BeNull();
        }
        finally
        {
            if (Directory.Exists(MenuItemsDir)) Directory.Delete(MenuItemsDir, recursive: true);
        }
    }
}
