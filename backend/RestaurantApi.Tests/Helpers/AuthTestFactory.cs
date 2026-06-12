using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using RestaurantApi.Services;
using RestaurantApi.Services.Email;

namespace RestaurantApi.Tests.Helpers;

/// <summary>
/// Test host that replaces the registered IEmailSender with a supplied double and
/// drops the background workers (which the base factory leaves running). Set
/// <see cref="EmailSender"/> before the first CreateClient() call.
/// </summary>
public class AuthTestFactory : TestWebApplicationFactory
{
    public IEmailSender EmailSender { get; set; } = new RecordingEmailSender();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            // Swap the SMTP sender for the supplied double.
            var emailDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IEmailSender));
            if (emailDescriptor != null) services.Remove(emailDescriptor);
            services.AddSingleton(EmailSender);

            // The base factory (unlike MenuImportTestFactory) leaves hosted services
            // registered; drop them so they don't poll/race the test database.
            RemoveHostedService<MenuImportBackgroundService>(services);
            RemoveHostedService<OrderFinalizationService>(services);
        });
    }

    private static void RemoveHostedService<T>(IServiceCollection services) where T : class
    {
        foreach (var d in services.Where(d => d.ImplementationType == typeof(T)).ToList())
            services.Remove(d);
    }
}
