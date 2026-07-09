using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using RestaurantApi.Services;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace RestaurantApi.Tests;

/// <summary>
/// The Python service shares no filesystem with the API, so the client must
/// upload the PDF bytes as multipart form-data instead of sending a path.
/// </summary>
public class MenuImportPythonClientTests : IAsyncLifetime
{
    private const string PdfMarker = "%PDF-1.4 fake-pdf-for-client-tests";

    private WireMockServer _python = null!;
    private HttpClient _httpClient = null!;
    private string _pdfPath = null!;

    public Task InitializeAsync()
    {
        _python = WireMockServer.Start();
        _httpClient = new HttpClient { BaseAddress = new Uri(_python.Url!) };
        _pdfPath = Path.Combine(Path.GetTempPath(), $"menu-import-client-{Guid.NewGuid():N}.pdf");
        File.WriteAllText(_pdfPath, PdfMarker, Encoding.ASCII);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _httpClient.Dispose();
        _python.Stop();
        File.Delete(_pdfPath);
        return Task.CompletedTask;
    }

    private PdfJobPayload MakePayload() => new(
        JobId: Guid.Parse("11111111-2222-3333-4444-555555555555"),
        RestaurantId: Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
        StoragePath: _pdfPath,
        ProgressCallbackUrl: "http://backend-api/api/internal/menu-imports/abc/progress?leaseId=x",
        HmacSecret: "supersecret12345",
        Provider: "gemini",
        Locale: "en-US");

    private void StubSuccess() =>
        _python
            .Given(Request.Create().WithPath("/menu/extract-pdf").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""
                { "items": [], "failed_pages": [], "model": "fake-pdf-model", "schema_version": 2 }
                """));

    [Fact]
    public async Task Uploads_pdf_bytes_as_multipart_file_part()
    {
        StubSuccess();
        var client = new MenuImportPythonClient(_httpClient);

        await client.ExtractPdfAsync(MakePayload(), CancellationToken.None);

        var body = _python.LogEntries.Should().ContainSingle().Subject.RequestMessage.Body ?? string.Empty;
        body.Should().MatchRegex("name=\"?file\"?");
        body.Should().Contain(PdfMarker);
    }

    [Fact]
    public async Task Sends_job_metadata_as_snake_case_form_fields()
    {
        StubSuccess();
        var client = new MenuImportPythonClient(_httpClient);
        var payload = MakePayload();

        await client.ExtractPdfAsync(payload, CancellationToken.None);

        var body = _python.LogEntries.Should().ContainSingle().Subject.RequestMessage.Body ?? string.Empty;
        body.Should().MatchRegex("name=\"?job_id\"?");
        body.Should().Contain("11111111-2222-3333-4444-555555555555");
        body.Should().MatchRegex("name=\"?restaurant_id\"?");
        body.Should().Contain("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        body.Should().MatchRegex("name=\"?progress_callback_url\"?");
        body.Should().Contain(payload.ProgressCallbackUrl);
        body.Should().MatchRegex("name=\"?hmac_secret\"?");
        body.Should().Contain(payload.HmacSecret);
        body.Should().MatchRegex("name=\"?provider\"?");
        body.Should().MatchRegex("name=\"?locale\"?");
        body.Should().Contain("en-US");
    }

    [Fact]
    public async Task Does_not_leak_the_local_storage_path_over_the_wire()
    {
        StubSuccess();
        var client = new MenuImportPythonClient(_httpClient);

        await client.ExtractPdfAsync(MakePayload(), CancellationToken.None);

        var body = _python.LogEntries.Should().ContainSingle().Subject.RequestMessage.Body ?? string.Empty;
        body.Should().NotContain("storage_path");
        body.Should().NotContain(_pdfPath);
    }

    [Fact]
    public async Task Returns_parsed_json_element_from_response()
    {
        StubSuccess();
        var client = new MenuImportPythonClient(_httpClient);

        var result = await client.ExtractPdfAsync(MakePayload(), CancellationToken.None);

        result.GetProperty("model").GetString().Should().Be("fake-pdf-model");
        result.GetProperty("schema_version").GetInt32().Should().Be(2);
        result.GetProperty("items").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Throws_on_non_success_response()
    {
        _python
            .Given(Request.Create().WithPath("/menu/extract-pdf").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(503));

        var client = new MenuImportPythonClient(_httpClient);

        var act = async () => await client.ExtractPdfAsync(MakePayload(), CancellationToken.None);
        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task Appends_extract_item_photos_part_only_when_flag_set()
    {
        StubSuccess();
        var client = new MenuImportPythonClient(_httpClient);

        await client.ExtractPdfAsync(MakePayload() with { ExtractItemPhotos = true }, CancellationToken.None);

        var body = _python.LogEntries.Should().ContainSingle().Subject.RequestMessage.Body ?? string.Empty;
        body.Should().MatchRegex("name=\"?extract_item_photos\"?");
    }

    [Fact]
    public async Task Omits_extract_item_photos_part_when_flag_unset()
    {
        StubSuccess();
        var client = new MenuImportPythonClient(_httpClient);

        await client.ExtractPdfAsync(MakePayload(), CancellationToken.None);

        var body = _python.LogEntries.Should().ContainSingle().Subject.RequestMessage.Body ?? string.Empty;
        body.Should().NotContain("extract_item_photos");
    }
}
