using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantApi.Data;
using RestaurantApi.DTOs.Responses;
using RestaurantApi.Services.Email;
using RestaurantApi.Tests.Helpers;
using Testcontainers.PostgreSql;
using Xunit;

namespace RestaurantApi.Tests;

public class AuthControllerTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
    private AuthTestFactory _factory = null!;

    public async Task InitializeAsync() => await _pg.StartAsync();

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        await _pg.DisposeAsync();
    }

    private async Task<HttpClient> ClientWithSenderAsync(IEmailSender sender)
    {
        _factory = new AuthTestFactory
        {
            PostgresConnectionString = _pg.GetConnectionString(),
            EmailSender = sender,
        };
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.MigrateAsync();
        }
        return _factory.CreateClient();
    }

    [Fact]
    public async Task Register_returns_confirmation_sent_when_email_succeeds()
    {
        var recorder = new RecordingEmailSender();
        var client = await ClientWithSenderAsync(recorder);

        var resp = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = "alice@example.com",
            password = "Sup3rSecret!",
            fullName = "Alice",
        });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<RegisterResponse>();
        body!.Status.Should().Be("confirmation_sent");
        recorder.Sent.Should().ContainSingle(e => e.To == "alice@example.com");
    }

    [Fact]
    public async Task Register_returns_confirmation_failed_when_email_send_throws()
    {
        var client = await ClientWithSenderAsync(new FailingEmailSender());

        var resp = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = "bob@example.com",
            password = "Sup3rSecret!",
            fullName = "Bob",
        });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<RegisterResponse>();
        body!.Status.Should().Be("confirmation_failed");
    }
}
