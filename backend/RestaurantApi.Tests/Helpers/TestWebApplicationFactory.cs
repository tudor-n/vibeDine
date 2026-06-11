using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RestaurantApi.Data;

namespace RestaurantApi.Tests.Helpers;

public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    public string PostgresConnectionString { get; set; } = string.Empty;
    public string PythonAiBaseUrl { get; set; } = "http://localhost:0";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = PostgresConnectionString,
                ["AiService:BaseUrl"] = PythonAiBaseUrl,
                // Provide a dummy 32-byte JWT key so the startup guard passes in test environments
                ["Jwt:Key"] = "test-only-key-do-not-use-in-production!!",
                ["Jwt:Issuer"] = "test-issuer",
                ["Jwt:Audience"] = "test-audience",
            });
        });
        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (descriptor != null) services.Remove(descriptor);
            services.AddDbContext<AppDbContext>(opts => opts.UseNpgsql(PostgresConnectionString));
        });
    }
}
