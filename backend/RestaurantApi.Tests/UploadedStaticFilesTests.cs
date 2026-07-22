using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace RestaurantApi.Tests;

[Collection("Static file startup config")]
public class UploadedStaticFilesTests : IDisposable
{
    private readonly Dictionary<string, string?> _originalEnvironment = new();
    private readonly string _contentRoot;

    public UploadedStaticFilesTests()
    {
        _contentRoot = Path.Combine(Path.GetTempPath(), "uploaded-static-files-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_contentRoot);
        SetEnvironment("ASPNETCORE_ENVIRONMENT", "Testing");
        SetEnvironment("ConnectionStrings__Default", "Host=localhost;Database=unused;Username=unused;Password=unused");
        SetEnvironment("AiService__BaseUrl", "http://localhost:0");
        SetEnvironment("Jwt__Key", "test-only-key-do-not-use-in-production!!");
        SetEnvironment("Jwt__Issuer", "test-issuer");
        SetEnvironment("Jwt__Audience", "test-audience");
    }

    public void Dispose()
    {
        foreach (var (key, value) in _originalEnvironment)
            Environment.SetEnvironmentVariable(key, value);

        try { Directory.Delete(_contentRoot, recursive: true); } catch { /* best effort */ }
    }

    private void SetEnvironment(string key, string value)
    {
        _originalEnvironment[key] = Environment.GetEnvironmentVariable(key);
        Environment.SetEnvironmentVariable(key, value);
    }

    [Fact]
    public async Task UploadsMenuItemFilesAreServedFromContentRootWwwroot()
    {
        var restaurantId = Guid.NewGuid();
        var relativeUrl = $"/uploads/menu-items/{restaurantId:N}/crop.jpg";
        using var factory = new StaticFileWebApplicationFactory(_contentRoot);
        using var client = factory.CreateClient();

        var filePath = Path.Combine(
            _contentRoot,
            "wwwroot",
            "uploads",
            "menu-items",
            restaurantId.ToString("N"),
            "crop.jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        await File.WriteAllBytesAsync(filePath, new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 });

        var response = await client.GetAsync(relativeUrl);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("image/jpeg");
        (await response.Content.ReadAsByteArrayAsync()).Should().Equal(0xFF, 0xD8, 0xFF, 0xD9);
    }

    // The Railway shape: Uploads__Root points at a persistent volume outside the
    // container's ephemeral filesystem, and /uploads/* must serve from there.
    [Fact]
    public async Task UploadsRootOverride_ServesFilesFromConfiguredDirectory()
    {
        var overrideRoot = Path.Combine(_contentRoot, "volume", "uploads");
        var restaurantId = Guid.NewGuid();
        using var factory = new StaticFileWebApplicationFactory(_contentRoot, overrideRoot);
        using var client = factory.CreateClient();

        var filePath = Path.Combine(overrideRoot, "branding", restaurantId.ToString("N"), "logo.png");
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        await File.WriteAllBytesAsync(filePath, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        var response = await client.GetAsync($"/uploads/branding/{restaurantId:N}/logo.png");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("image/png");
        (await response.Content.ReadAsByteArrayAsync()).Should().Equal(0x89, 0x50, 0x4E, 0x47);
    }

    private sealed class StaticFileWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly string _contentRoot;
        private readonly string? _uploadsRoot;

        public StaticFileWebApplicationFactory(string contentRoot, string? uploadsRoot = null)
        {
            _contentRoot = contentRoot;
            _uploadsRoot = uploadsRoot;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseContentRoot(_contentRoot);
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Default"] = "Host=localhost;Database=unused;Username=unused;Password=unused",
                    ["AiService:BaseUrl"] = "http://localhost:0",
                    ["Jwt:Key"] = "test-only-key-do-not-use-in-production!!",
                    ["Jwt:Issuer"] = "test-issuer",
                    ["Jwt:Audience"] = "test-audience",
                    ["Uploads:Root"] = _uploadsRoot,
                });
            });
            builder.ConfigureServices(services =>
            {
                foreach (var hostedService in services.Where(d => d.ServiceType == typeof(IHostedService)).ToList())
                    services.Remove(hostedService);
            });
        }
    }
}

[CollectionDefinition("Static file startup config", DisableParallelization = true)]
public sealed class StaticFileStartupConfigCollection;
