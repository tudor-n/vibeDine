using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantApi.Data;
using RestaurantApi.Models;
using RestaurantApi.Tests.Helpers;
using Testcontainers.PostgreSql;
using Xunit;

namespace RestaurantApi.Tests.Branding;

// Covers the logo/photo upload actions added to BrandingController in Task 4: magic-byte
// validated multipart uploads, tenant-scoped storage paths, and waiter path-restriction
// (uploads are not in TenantResolutionMiddleware's WaiterAllowedPaths whitelist).
public class BrandingUploadTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private MenuImportTestFactory _factory = null!;
    private HttpClient _clientA = null!;
    private HttpClient _clientB = null!;
    private Guid _restaurantIdA;
    private Guid _restaurantIdB;

    private static readonly byte[] MinimalPng =
        { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0, 0, 0, 0, 0 };

    public async Task InitializeAsync()
    {
        await _pg.StartAsync();
        _factory = new MenuImportTestFactory { PostgresConnectionString = _pg.GetConnectionString() };

        using (var migrateScope = _factory.Services.CreateScope())
        {
            var migrateDb = migrateScope.ServiceProvider.GetRequiredService<AppDbContext>();
            await migrateDb.Database.MigrateAsync();
        }

        var (restA, restB, clientA, clientB) = await MenuImportTestFixtures.SeedTwoTenantsAsync(_factory);
        _restaurantIdA = restA;
        _restaurantIdB = restB;
        _clientA = clientA;
        _clientB = clientB;
    }

    public async Task DisposeAsync()
    {
        _clientA.Dispose();
        _clientB.Dispose();
        _factory.Dispose();
        await _pg.DisposeAsync();
    }

    private string LogoUrl(Guid restaurantId) => $"/api/restaurants/{restaurantId}/admin/branding/logo";
    private string PhotoUrl(Guid restaurantId) => $"/api/restaurants/{restaurantId}/admin/branding/photo";

    private static MultipartFormDataContent PngForm(byte[] bytes, string mime = "image/png", string fileName = "logo.png")
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(mime);
        form.Add(file, "file", fileName);
        return form;
    }

    [Fact]
    public async Task Upload_logo_returns_url()
    {
        using var form = PngForm(MinimalPng);

        var res = await _clientA.PostAsync(LogoUrl(_restaurantIdA), form);

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await res.Content.ReadFromJsonAsync<LogoShape>();
        body!.logoUrl.Should().StartWith("/uploads/branding/");
        body.logoUrl.Should().Contain(_restaurantIdA.ToString("N"));

        var restaurant = await GetRestaurantAsync(_restaurantIdA);
        restaurant.LogoUrl.Should().Be(body.logoUrl);
    }

    [Fact]
    public async Task Upload_photo_returns_url()
    {
        using var form = PngForm(MinimalPng, fileName: "photo.png");

        var res = await _clientA.PostAsync(PhotoUrl(_restaurantIdA), form);

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await res.Content.ReadFromJsonAsync<PhotoShape>();
        body!.photoUrl.Should().StartWith("/uploads/branding/");

        var restaurant = await GetRestaurantAsync(_restaurantIdA);
        restaurant.PhotoUrl.Should().Be(body.photoUrl);
    }

    [Fact]
    public async Task Upload_rejects_non_image_by_content()
    {
        using var form = PngForm(System.Text.Encoding.ASCII.GetBytes("not really a png"), fileName: "evil.png"); // lies about MIME

        var res = await _clientA.PostAsync(LogoUrl(_restaurantIdA), form);

        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Reupload_deletes_prior_asset()
    {
        using (var first = PngForm(MinimalPng))
            (await _clientA.PostAsync(LogoUrl(_restaurantIdA), first)).EnsureSuccessStatusCode();

        using var envScope = _factory.Services.CreateScope();
        var env = envScope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>();
        var restaurantDir = System.IO.Path.Combine(
            env.ContentRootPath, "wwwroot", "uploads", "branding", _restaurantIdA.ToString("N"));

        using var second = PngForm(MinimalPng);
        var res = await _clientA.PostAsync(LogoUrl(_restaurantIdA), second);
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        // Only one logo.* file should remain for this restaurant (old one replaced, not accumulated).
        System.IO.Directory.EnumerateFiles(restaurantDir, "logo.*").Should().HaveCount(1);
    }

    [Fact]
    public async Task Waiter_cannot_upload_logo()
    {
        // Uploads aren't in TenantResolutionMiddleware's WaiterAllowedPaths whitelist, so an
        // active Waiter membership on restaurant A must be rejected before the controller runs.
        var waiterUserId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var waiterUser = new AppUser
            {
                Id = waiterUserId,
                UserName = $"waiter-{waiterUserId:N}@test.com",
                Email = $"waiter-{waiterUserId:N}@test.com",
                FullName = "Waiter",
            };
            await userManager.CreateAsync(waiterUser, "TestPass1!");

            var restaurant = await db.Restaurants.FirstAsync(r => r.Id == _restaurantIdA);
            db.Set<Membership>().Add(new Membership
            {
                Id = Guid.NewGuid(),
                UserId = waiterUserId,
                OrganizationId = restaurant.OrganizationId,
                RestaurantId = _restaurantIdA,
                Role = MembershipRole.Waiter,
                Status = MembershipStatus.Active,
            });
            await db.SaveChangesAsync();
        }

        using var waiterClient = _factory.CreateAuthenticatedClient(waiterUserId, _restaurantIdA);
        using var form = PngForm(MinimalPng);

        var res = await waiterClient.PostAsync(LogoUrl(_restaurantIdA), form);

        res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Cannot_upload_to_another_restaurant()
    {
        // _clientA's user only has membership on restaurant A; hitting restaurant B's
        // upload route must be blocked by TenantResolutionMiddleware before the
        // controller ever runs.
        using var form = PngForm(MinimalPng);

        var res = await _clientA.PostAsync(LogoUrl(_restaurantIdB), form);

        res.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
    }

    private async Task<Restaurant> GetRestaurantAsync(Guid restaurantId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Restaurants.AsNoTracking().FirstAsync(r => r.Id == restaurantId);
    }

    private record LogoShape(string logoUrl);
    private record PhotoShape(string photoUrl);
}
