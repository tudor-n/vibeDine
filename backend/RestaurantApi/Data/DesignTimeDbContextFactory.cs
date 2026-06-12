using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using RestaurantApi.Tenancy;

namespace RestaurantApi.Data;

/// <summary>
/// Lets <c>dotnet ef</c> build the model without booting the full web host (which
/// would run the dev-time <c>Migrate()</c> + seeding and require a live database).
/// <c>migrations add</c> only needs a constructible context, not a real connection.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = config.GetConnectionString("Default")
            ?? "Host=localhost;Port=5433;Database=restaurant;Username=dev;Password=devpass";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        // A bare CurrentTenant is fine at design time — no request is in flight.
        return new AppDbContext(options, new CurrentTenant());
    }
}
