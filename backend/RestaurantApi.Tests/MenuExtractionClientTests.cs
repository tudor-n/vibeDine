using System;
using System.Collections.Generic;
using System.Linq;
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

    [Fact]
    public async Task Forwards_locale_as_form_field()
    {
        _python
            .Given(Request.Create().WithPath("/menu/extract").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""
                { "items": [], "failed_pages": [], "model": "fake", "schema_version": 2 }
                """));

        var client = new MenuExtractionClient(_httpClient);
        var photos = new List<(string FileName, string ContentType, byte[] Bytes)>
        {
            ("p0.png", "image/png", new byte[] { 1, 2, 3 }),
        };

        var result = await client.ExtractAsync(photos, CancellationToken.None, locale: "fr-FR");
        result.Model.Should().Be("fake");

        // Inspect what was actually sent by WireMock.
        var log = _python.LogEntries.Single();
        var body = log.RequestMessage.Body ?? string.Empty;
        body.Should().MatchRegex("name=\"?locale\"?");
        body.Should().Contain("fr-FR");
    }

    [Fact]
    public async Task Defaults_locale_to_ro_RO_when_not_specified()
    {
        _python
            .Given(Request.Create().WithPath("/menu/extract").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""
                { "items": [], "failed_pages": [], "model": "fake", "schema_version": 2 }
                """));

        var client = new MenuExtractionClient(_httpClient);
        var photos = new List<(string FileName, string ContentType, byte[] Bytes)>
        {
            ("p0.png", "image/png", new byte[] { 1, 2, 3 }),
        };

        var result = await client.ExtractAsync(photos, CancellationToken.None);
        result.Model.Should().Be("fake");

        var log = _python.LogEntries.Single();
        var body = log.RequestMessage.Body ?? string.Empty;
        body.Should().MatchRegex("name=\"?locale\"?");
        body.Should().Contain("ro-RO");
    }

    [Fact]
    public async Task Passes_hierarchy_through_as_opaque_json()
    {
        _python
            .Given(Request.Create().WithPath("/menu/extract").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""
                {
                  "items": [
                    { "name": "Margherita", "price": 28.0, "source_page": 0, "source_index": 0,
                      "description": null, "category_hint": "Pizza", "confidence": 0.9 }
                  ],
                  "failed_pages": [],
                  "model": "fake",
                  "schema_version": 2,
                  "hierarchy": {
                    "source": "ai",
                    "categories": [
                      { "name": "Pizza", "sort_order": 0, "subcategories": [],
                        "direct_item_refs": [{ "source_page": 0, "source_index": 0, "confidence": 0.9 }] }
                    ],
                    "uncategorized_item_refs": [],
                    "model_notes": null
                  }
                }
                """));

        var client = new MenuExtractionClient(_httpClient);
        var photos = new List<(string FileName, string ContentType, byte[] Bytes)>
        {
            ("p0.png", "image/png", new byte[] { 1, 2, 3 }),
        };

        var result = await client.ExtractAsync(photos, CancellationToken.None);

        result.Items.Should().HaveCount(1);
        result.Hierarchy.Should().NotBeNull();
        var hierarchy = result.Hierarchy!.Value;
        hierarchy.GetProperty("source").GetString().Should().Be("ai");
        // snake_case is preserved through to the frontend (mirrors PDF flow)
        var categories = hierarchy.GetProperty("categories");
        categories.GetArrayLength().Should().Be(1);
        categories[0].GetProperty("name").GetString().Should().Be("Pizza");
        categories[0].GetProperty("direct_item_refs").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task ExtractWithPhotos_appends_flag_and_parses_item_photos()
    {
        _python
            .Given(Request.Create().WithPath("/menu/extract").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""
                {
                  "items": [
                    { "name": "Pizza", "price": 28.0, "source_page": 0, "source_index": 0,
                      "description": null, "category_hint": "Pizza", "confidence": 0.9 }
                  ],
                  "failed_pages": [],
                  "model": "fake",
                  "schema_version": 2,
                  "item_photos": [
                    { "source_page": 0, "source_index": 0, "image_base64": "QUJD", "confidence": 0.9 }
                  ]
                }
                """));

        var client = new MenuExtractionClient(_httpClient);
        var photos = new List<(string FileName, string ContentType, byte[] Bytes)>
        {
            ("p0.png", "image/png", new byte[] { 1, 2, 3 }),
        };

        var result = await client.ExtractWithPhotosAsync(photos, CancellationToken.None, extractItemPhotos: true);

        result.ItemPhotos.Should().HaveCount(1);
        result.ItemPhotos[0].SourcePage.Should().Be(0);
        result.ItemPhotos[0].SourceIndex.Should().Be(0);
        result.ItemPhotos[0].ImageBase64.Should().Be("QUJD");

        var body = _python.LogEntries.Single().RequestMessage.Body ?? string.Empty;
        body.Should().MatchRegex("name=\"?extract_item_photos\"?");
    }

    [Fact]
    public async Task ExtractAsync_omits_extract_item_photos_part()
    {
        _python
            .Given(Request.Create().WithPath("/menu/extract").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""
                { "items": [], "failed_pages": [], "model": "fake", "schema_version": 2 }
                """));

        var client = new MenuExtractionClient(_httpClient);
        var photos = new List<(string FileName, string ContentType, byte[] Bytes)>
        {
            ("p0.png", "image/png", new byte[] { 1 }),
        };

        await client.ExtractAsync(photos, CancellationToken.None);

        var body = _python.LogEntries.Single().RequestMessage.Body ?? string.Empty;
        body.Should().NotContain("extract_item_photos");
    }

    [Fact]
    public async Task Hierarchy_is_null_when_python_omits_it()
    {
        _python
            .Given(Request.Create().WithPath("/menu/extract").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""
                { "items": [], "failed_pages": [], "model": "fake", "schema_version": 1 }
                """));

        var client = new MenuExtractionClient(_httpClient);
        var photos = new List<(string FileName, string ContentType, byte[] Bytes)>
        {
            ("p0.png", "image/png", new byte[] { 1 }),
        };

        var result = await client.ExtractAsync(photos, CancellationToken.None);
        result.Hierarchy.Should().BeNull();
    }
}
