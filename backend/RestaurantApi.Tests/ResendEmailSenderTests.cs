using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RestaurantApi.Services.Email;
using RestaurantApi.Tests.Helpers;
using Xunit;

namespace RestaurantApi.Tests;

public class ResendEmailSenderTests
{
    private static ResendEmailSender CreateSender(FakeHttpMessageHandler handler)
    {
        var http = new HttpClient(handler);
        var opts = Options.Create(new SmtpOptions
        {
            FromAddress = "no-reply@vibedine.ro",
            FromName = "Smart Restaurant",
            Password = "re_test_key",
        });
        return new ResendEmailSender(http, opts, NullLogger<ResendEmailSender>.Instance);
    }

    [Fact]
    public async Task Posts_to_resend_api_with_bearer_auth_and_payload()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """{"id":"abc"}""");
        var sender = CreateSender(handler);

        await sender.SendEmailAsync("alice@example.com", "Hello", "<p>Hi</p>");

        handler.LastRequest!.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest.RequestUri!.ToString().Should().Be("https://api.resend.com/emails");
        handler.LastRequest.Headers.Authorization!.Scheme.Should().Be("Bearer");
        handler.LastRequest.Headers.Authorization.Parameter.Should().Be("re_test_key");

        // Parse the JSON body so assertions are robust to JSON's HTML escaping of < > &.
        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        var root = doc.RootElement;
        root.GetProperty("from").GetString().Should().Be("Smart Restaurant <no-reply@vibedine.ro>");
        root.GetProperty("to")[0].GetString().Should().Be("alice@example.com");
        root.GetProperty("subject").GetString().Should().Be("Hello");
        root.GetProperty("html").GetString().Should().Be("<p>Hi</p>");
    }

    [Fact]
    public async Task Throws_when_resend_returns_error_status()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.UnprocessableEntity, """{"message":"domain not verified"}""");
        var sender = CreateSender(handler);

        var act = async () => await sender.SendEmailAsync("alice@example.com", "Hello", "<p>Hi</p>");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*422*");
    }
}
