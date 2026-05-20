using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using RestaurantApi.Data;

namespace RestaurantApi.Tests.Helpers;

/// <summary>
/// WebApplicationFactory variant that injects a real Postgres connection string
/// and a valid MenuImport HMAC secret. Also provides a helper to create an HTTP
/// client pre-authenticated with a JWT for a given (userId, restaurantId) pair.
/// </summary>
public class MenuImportTestFactory : WebApplicationFactory<Program>
{
    // Same key registered in TestWebApplicationFactory — must be ≥ 32 bytes.
    private const string TestJwtKey = "test-only-key-do-not-use-in-production!!";
    private const string TestIssuer = "test-issuer";
    private const string TestAudience = "test-audience";

    public string PostgresConnectionString { get; set; } = string.Empty;
    public string HmacSecret { get; set; } = "dev-hmac-secret-32chars-minimum-pad-1234";
    public string StorageRoot { get; set; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "menu-import-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// When non-null, this IPdfExtractor is registered as a singleton in DI,
    /// replacing the real MenuImportPythonClient. Set before the first request.
    /// Also controls whether the background service runs: if set, the background
    /// service is NOT removed so tests can call RunOnceForTestAsync via DI.
    /// </summary>
    public RestaurantApi.Services.IPdfExtractor? FakeExtractor { get; set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = PostgresConnectionString,
                ["AiService:BaseUrl"] = "http://localhost:0",
                ["Jwt:Key"] = TestJwtKey,
                ["Jwt:Issuer"] = TestIssuer,
                ["Jwt:Audience"] = TestAudience,
                ["MenuImport:HmacSecret"] = HmacSecret,
                ["MenuImport:StorageRoot"] = StorageRoot,
                ["MenuImport:CallbackBaseUrl"] = "http://localhost:9999",
            });
        });
        builder.ConfigureServices(services =>
        {
            // Replace DB context to point at the test Postgres container.
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (descriptor != null) services.Remove(descriptor);
            services.AddDbContext<AppDbContext>(opts => opts.UseNpgsql(PostgresConnectionString));

            if (FakeExtractor is not null)
            {
                // Override IPdfExtractor with the provided fake.
                var extDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(RestaurantApi.Services.IPdfExtractor));
                if (extDescriptor != null) services.Remove(extDescriptor);
                services.AddSingleton(FakeExtractor);
            }
            else
            {
                // Remove background services so they don't race against test migrations.
                // Tests that need the background service logic test it directly (unit style).
                RemoveHostedService<RestaurantApi.Services.MenuImportBackgroundService>(services);
            }

            RemoveHostedService<RestaurantApi.Services.OrderFinalizationService>(services);
        });
    }

    /// <summary>
    /// Returns an HttpClient that carries a valid JWT for the given user + restaurant.
    /// The JWT is self-signed with the test key, so the ASP.NET auth middleware accepts it.
    /// </summary>
    public HttpClient CreateAuthenticatedClient(Guid userId, Guid restaurantId)
    {
        var token = BuildJwt(userId, restaurantId);
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static void RemoveHostedService<T>(IServiceCollection services) where T : class
    {
        var descriptors = services
            .Where(d => d.ImplementationType == typeof(T) ||
                        d.ImplementationFactory != null)
            .ToList();
        // More reliable: remove by implementation type
        var toRemove = services
            .Where(d => d.ImplementationType == typeof(T))
            .ToList();
        foreach (var d in toRemove)
            services.Remove(d);
    }

    private static string BuildJwt(Guid userId, Guid restaurantId)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwtKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, "test@example.com"),
            new Claim("name", "Test User"),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        var jwt = new JwtSecurityToken(
            issuer: TestIssuer,
            audience: TestAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }
}
