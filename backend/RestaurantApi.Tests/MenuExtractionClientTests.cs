using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using RestaurantApi.DTOs.Responses;
using RestaurantApi.Services;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace RestaurantApi.Tests;

public class MenuExtractionClientTests : IAsyncLifetime
{
    private WireMockServer _python = null!;
    private HttpClient _httpClient = null!;

    public Task InitializeAsync()
    {
        _python = WireMockServer.Start();
        _httpClient = new HttpClient { BaseAddress = new Uri(_python.Url!) };
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _httpClient.Dispose();
        _python.Stop();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Forwards_photos_and_returns_parsed_response()
    {
        _python
            .Given(Request.Create().WithPath("/menu/extract").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""
                {
                  "items": [
                    { "name": "Ciorbă", "price": 18.0, "source_page": 0, "source_index": 0,
                      "description": null, "category_hint": "Ciorbe", "confidence": null }
                  ],
                  "failed_pages": [],
                  "model": "claude-sonnet-test",
                  "schema_version": 1
                }
                """));

        var client = new MenuExtractionClient(_httpClient);
        var photos = new List<(string FileName, string ContentType, byte[] Bytes)>
        {
            ("p0.png", "image/png", new byte[] { 1, 2, 3 }),
        };

        var result = await client.ExtractAsync(photos, CancellationToken.None);

        result.Items.Should().HaveCount(1);
        result.Items[0].Name.Should().Be("Ciorbă");
        result.Items[0].CategoryHint.Should().Be("Ciorbe");
        result.FailedPages.Should().BeEmpty();
        result.Model.Should().Be("claude-sonnet-test");
    }

    [Fact]
    public async Task Throws_on_non_success_response()
    {
        _python
            .Given(Request.Create().WithPath("/menu/extract").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(503));

        var client = new MenuExtractionClient(_httpClient);
        var photos = new List<(string, string, byte[])> { ("p0.png", "image/png", new byte[] { 1 }) };

        var act = async () => await client.ExtractAsync(photos, CancellationToken.None);
        await act.Should().ThrowAsync<HttpRequestException>();
    }
}
