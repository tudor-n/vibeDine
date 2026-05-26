using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantApi.Controllers.Requests;
using RestaurantApi.Data;
using RestaurantApi.Models;
using RestaurantApi.Services;
using RestaurantApi.Tests.Helpers;
using Testcontainers.PostgreSql;
using Xunit;

namespace RestaurantApi.Tests;

/// <summary>
/// End-to-end integration tests for the PDF menu import pipeline.
/// Uses a real Postgres testcontainer and a FakePdfExtractor injected via DI.
/// Background-service ticks are driven explicitly via RunOnceForTestAsync.
///
/// Coverage:
///   1. HappyPath — upload → background tick → status==complete with items.
///   2. Cancellation — upload → cancel → background tick → still cancelled.
///   3. Tenancy isolation — U1 cannot see U2's job via URL path.
///   4. HMAC internal endpoint — bad signature → 401.
///   5. HMAC internal endpoint — missing header → 401.
///   6. Validation — non-PDF content-type → 400.
///   7. Validation — missing file → 400.
///   8. Validation — bad magic bytes → 400.
///   9. Validation — oversized file note (documented below as manual-only).
/// </summary>
public class MenuImportEndToEndTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private const string HmacSecret = "e2e-hmac-secret-32chars-minimum-padX";

    // Shared between tests — seeded once in InitializeAsync.
    private static readonly Guid _userAId = Guid.NewGuid();
    private static readonly Guid _userBId = Guid.NewGuid();
    private static readonly Guid _restA   = Guid.NewGuid();
    private static readonly Guid _restB   = Guid.NewGuid();

    private MenuImportTestFactory _factory = null!;
    private FakePdfExtractor       _fake   = null!;
    private HttpClient             _clientA = null!;
    private HttpClient             _clientB = null!;

    public async Task InitializeAsync()
    {
        await _pg.StartAsync();

        _fake = new FakePdfExtractor();

        _factory = new MenuImportTestFactory
        {
            PostgresConnectionString = _pg.GetConnectionString(),
            HmacSecret               = HmacSecret,
            // Inject fake extractor — this also keeps the background service registered
            // so we can resolve MenuImportBackgroundService from DI and call RunOnceForTestAsync.
            FakeExtractor = _fake,
        };

        // Trigger server start, then migrate + seed.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var uA = new AppUser { Id = _userAId, UserName = "e2e-a@test.com", Email = "e2e-a@test.com", FullName = "E2E User A" };
        var uB = new AppUser { Id = _userBId, UserName = "e2e-b@test.com", Email = "e2e-b@test.com", FullName = "E2E User B" };
        await userManager.CreateAsync(uA, "TestPass1!");
        await userManager.CreateAsync(uB, "TestPass1!");

        var orgA = Guid.NewGuid();
        var orgB = Guid.NewGuid();
        db.Set<Organization>().AddRange(
            new Organization { Id = orgA, Name = "Org A E2E", Slug = "org-a-e2e-" + orgA.ToString("N")[..6], CreatedByUserId = _userAId },
            new Organization { Id = orgB, Name = "Org B E2E", Slug = "org-b-e2e-" + orgB.ToString("N")[..6], CreatedByUserId = _userBId }
        );
        db.Set<Restaurant>().AddRange(
            new Restaurant { Id = _restA, OrganizationId = orgA, Name = "E2E Rest A", Slug = "e2e-ra-" + _restA.ToString("N")[..6], IsActive = true },
            new Restaurant { Id = _restB, OrganizationId = orgB, Name = "E2E Rest B", Slug = "e2e-rb-" + _restB.ToString("N")[..6], IsActive = true }
        );
        await db.SaveChangesAsync();
        db.Set<Membership>().AddRange(
            new Membership { Id = Guid.NewGuid(), UserId = _userAId, OrganizationId = orgA, RestaurantId = _restA, Role = MembershipRole.RestaurantAdmin },
            new Membership { Id = Guid.NewGuid(), UserId = _userBId, OrganizationId = orgB, RestaurantId = _restB, Role = MembershipRole.RestaurantAdmin }
        );
        await db.SaveChangesAsync();

        _clientA = _factory.CreateAuthenticatedClient(_userAId, _restA);
        _clientB = _factory.CreateAuthenticatedClient(_userBId, _restB);
    }

    public async Task DisposeAsync()
    {
        _clientA.Dispose();
        _clientB.Dispose();
        _factory.Dispose();
        await _pg.DisposeAsync();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helper: HMAC
    // ──────────────────────────────────────────────────────────────────────────

    private static string ComputeHmac(string secret, byte[] body)
    {
        using var h = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(h.ComputeHash(body)).ToLowerInvariant();
    }

    /// <summary>Drive one background-service tick.</summary>
    private async Task TickBackgroundServiceAsync(CancellationToken ct = default)
    {
        using var scope = _factory.Services.CreateScope();
        var svc = new MenuImportBackgroundService(
            _factory.Services,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<MenuImportBackgroundService>.Instance,
            scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<MenuImportOptions>>(),
            scope.ServiceProvider.GetRequiredService<MenuImportProgressNotifier>()
        );
        await svc.RunOnceForTestAsync(ct);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 1: HappyPath_UploadProcessesCompletes
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HappyPath_UploadProcessesCompletes()
    {
        // Upload a minimal valid PDF.
        var resp = await _clientA.PostAsync(
            $"/api/restaurants/{_restA}/admin/menu/imports/pdf",
            MenuImportTestFixtures.ValidPdfMultipart("happy.pdf"));

        resp.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var created = await resp.Content.ReadFromJsonAsync<StartPdfImportResponse>();
        created.Should().NotBeNull();
        created!.JobId.Should().NotBeEmpty();

        // Drive one background tick — FakePdfExtractor returns synchronously.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await TickBackgroundServiceAsync(cts.Token);

        // Poll GET until complete (fake is synchronous so one tick is enough).
        var getResp = await _clientA.GetAsync(
            $"/api/restaurants/{_restA}/admin/menu/imports/{created.JobId}");
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var job = await getResp.Content.ReadFromJsonAsync<MenuImportJobResponse>();
        job.Should().NotBeNull();
        job!.Status.Should().Be("complete");
        job.Id.Should().Be(created.JobId);
        // FakePdfExtractor returns 1 item.
        var resultEl = job.Result as JsonElement?;
        resultEl.Should().NotBeNull();
        var items = resultEl!.Value.GetProperty("items");
        items.GetArrayLength().Should().BeGreaterThan(0);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 2: Cancellation_FlipsToCancelled_StopsProcessing
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cancellation_FlipsToCancelled_StopsProcessing()
    {
        // Upload
        var resp = await _clientA.PostAsync(
            $"/api/restaurants/{_restA}/admin/menu/imports/pdf",
            MenuImportTestFixtures.ValidPdfMultipart("cancel.pdf"));
        resp.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var created = await resp.Content.ReadFromJsonAsync<StartPdfImportResponse>();

        // Immediately cancel via DELETE.
        var delResp = await _clientA.DeleteAsync(
            $"/api/restaurants/{_restA}/admin/menu/imports/{created!.JobId}");
        delResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Drive one background tick. The service checks for "cancelled" before and after
        // calling the extractor — the job is already cancelled so it should be skipped.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await TickBackgroundServiceAsync(cts.Token);

        // GET must still show "cancelled", not "complete".
        var getResp = await _clientA.GetAsync(
            $"/api/restaurants/{_restA}/admin/menu/imports/{created.JobId}");
        var job = await getResp.Content.ReadFromJsonAsync<MenuImportJobResponse>();
        job!.Status.Should().Be("cancelled");
        // ResultJson must be null (no overwrite happened).
        job.Result.Should().BeNull();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 3: Tenancy_TwoRestaurants_NoCrossPollination
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Tenancy_TwoRestaurants_NoCrossPollination()
    {
        // User A uploads a PDF.
        var respA = await _clientA.PostAsync(
            $"/api/restaurants/{_restA}/admin/menu/imports/pdf",
            MenuImportTestFixtures.ValidPdfMultipart("tenant-a.pdf"));
        respA.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var createdA = await respA.Content.ReadFromJsonAsync<StartPdfImportResponse>();

        // User B uploads a PDF.
        var respB = await _clientB.PostAsync(
            $"/api/restaurants/{_restB}/admin/menu/imports/pdf",
            MenuImportTestFixtures.ValidPdfMultipart("tenant-b.pdf"));
        respB.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var createdB = await respB.Content.ReadFromJsonAsync<StartPdfImportResponse>();

        // User B cannot access User A's job via User B's restaurant URL.
        var crossResp = await _clientB.GetAsync(
            $"/api/restaurants/{_restB}/admin/menu/imports/{createdA!.JobId}");
        crossResp.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // User A cannot access User B's job.
        var crossRespA = await _clientA.GetAsync(
            $"/api/restaurants/{_restA}/admin/menu/imports/{createdB!.JobId}");
        crossRespA.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Drive ticks — both jobs complete and land under their correct restaurants.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await TickBackgroundServiceAsync(cts.Token);

        // Each owner CAN see their own job.
        var jobA = await (await _clientA.GetAsync(
            $"/api/restaurants/{_restA}/admin/menu/imports/{createdA.JobId}"))
            .Content.ReadFromJsonAsync<MenuImportJobResponse>();
        var jobB = await (await _clientB.GetAsync(
            $"/api/restaurants/{_restB}/admin/menu/imports/{createdB.JobId}"))
            .Content.ReadFromJsonAsync<MenuImportJobResponse>();

        jobA!.Status.Should().Be("complete");
        jobB!.Status.Should().Be("complete");

        // Verify DB-level isolation: each job has the correct RestaurantId.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var dbJobA = await db.MenuImportJobs.IgnoreQueryFilters().FirstAsync(j => j.Id == createdA.JobId);
        var dbJobB = await db.MenuImportJobs.IgnoreQueryFilters().FirstAsync(j => j.Id == createdB.JobId);
        dbJobA.RestaurantId.Should().Be(_restA);
        dbJobB.RestaurantId.Should().Be(_restB);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 4: HmacInternal_RejectsBadSignature
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HmacInternal_RejectsBadSignature()
    {
        var jobId = Guid.NewGuid();
        // Seed a job directly.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.MenuImportJobs.Add(new MenuImportJob
            {
                Id = jobId,
                RestaurantId = _restA,
                Status = "extracting",
                Phase = "parsing",
                StorageKey = $"{_restA:N}/{jobId:N}.pdf",
                OriginalFileName = "hmac.pdf",
                FileSizeBytes = 100,
                ProviderUsed = "gemini",
            });
            await db.SaveChangesAsync();
        }

        var payload = new ProgressCallbackRequest { JobId = jobId, Status = "extracting", Phase = "parsing", ProcessedPages = 1, TotalPages = 3 };
        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        using var anonClient = _factory.CreateClient();

        // Bad signature.
        using var req1 = new HttpRequestMessage(HttpMethod.Post, $"/api/internal/menu-imports/{jobId}/progress");
        req1.Content = new ByteArrayContent(jsonBytes);
        req1.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        req1.Headers.Add("X-Hmac-Signature", "deadbeefdeadbeef");
        (await anonClient.SendAsync(req1)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Correct signature → 200.
        var goodSig = ComputeHmac(HmacSecret, jsonBytes);
        using var req2 = new HttpRequestMessage(HttpMethod.Post, $"/api/internal/menu-imports/{jobId}/progress");
        req2.Content = new ByteArrayContent(jsonBytes);
        req2.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        req2.Headers.Add("X-Hmac-Signature", goodSig);
        var okResp = await anonClient.SendAsync(req2);
        okResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify DB updated.
        using var checkScope = _factory.Services.CreateScope();
        var checkDb = checkScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = await checkDb.MenuImportJobs.IgnoreQueryFilters().FirstAsync(j => j.Id == jobId);
        job.ProcessedPages.Should().Be(1);
        job.TotalPages.Should().Be(3);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 5: HmacInternal_MissingSignature
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HmacInternal_MissingSignature()
    {
        var jobId = Guid.NewGuid();
        var payload = new ProgressCallbackRequest { JobId = jobId, Status = "extracting", Phase = "parsing" };
        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(payload);

        using var anonClient = _factory.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, $"/api/internal/menu-imports/{jobId}/progress");
        req.Content = new ByteArrayContent(jsonBytes);
        req.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        // No X-Hmac-Signature header added.

        var resp = await anonClient.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 6: Validation_RejectsNonPdf
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Validation_RejectsNonPdf()
    {
        var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(new byte[] { 0x89, 0x50, 0x4E, 0x47 });
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        form.Add(content, "pdf", "image.png");

        var resp = await _clientA.PostAsync(
            $"/api/restaurants/{_restA}/admin/menu/imports/pdf", form);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 7: Validation_RejectsMissingFile
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Validation_RejectsMissingFile()
    {
        var resp = await _clientA.PostAsync(
            $"/api/restaurants/{_restA}/admin/menu/imports/pdf",
            new MultipartFormDataContent());
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 8: Validation_RejectsBadMagicBytes
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Validation_RejectsBadMagicBytes()
    {
        var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes("NOTPDF this is not a real PDF file"));
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        form.Add(content, "pdf", "fake.pdf");

        var resp = await _clientA.PostAsync(
            $"/api/restaurants/{_restA}/admin/menu/imports/pdf", form);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("PDF");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 9: Validation_RejectsOversized
    // NOTE: Creating a 110_000_001-byte stream in-process to exercise this path
    // would allocate ~105 MB per test run and exceeds the RequestSizeLimit
    // middleware before hitting our controller code. This is verified manually.
    // Manual check: POST a file > 110 MB; expect 413 Request Entity Too Large.
    // See docs/menu-import-pdf-smoke.md → "Negative tests".
    // ──────────────────────────────────────────────────────────────────────────

    // ──────────────────────────────────────────────────────────────────────────
    // Test 10: HappyPath_persists_hierarchy_on_completed_job_result
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HappyPath_persists_hierarchy_on_completed_job_result()
    {
        // Configure fake to return a canonical hierarchy payload alongside items.
        _fake.Hierarchy = new
        {
            source = "ai",
            categories = new[]
            {
                new
                {
                    name = "Pizza",
                    sort_order = 0,
                    subcategories = Array.Empty<object>(),
                    direct_item_refs = Array.Empty<object>(),
                },
            },
            uncategorized_item_refs = Array.Empty<object>(),
            model_notes = (string?)null,
        };

        // Upload a minimal valid PDF.
        var resp = await _clientA.PostAsync(
            $"/api/restaurants/{_restA}/admin/menu/imports/pdf",
            MenuImportTestFixtures.ValidPdfMultipart("hierarchy.pdf"));
        resp.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var created = await resp.Content.ReadFromJsonAsync<StartPdfImportResponse>();
        created.Should().NotBeNull();
        created!.JobId.Should().NotBeEmpty();

        // Drive one background tick — FakePdfExtractor returns synchronously.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await TickBackgroundServiceAsync(cts.Token);

        // GET the job and inspect the response shape against the actual JSON.
        var getResp = await _clientA.GetAsync(
            $"/api/restaurants/{_restA}/admin/menu/imports/{created.JobId}");
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var rawJson = await getResp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(rawJson);
        var jobJson = doc.RootElement;

        // The job envelope (ASP.NET) is camelCase: "status", "result", etc.
        jobJson.GetProperty("status").GetString().Should().Be("complete");

        // The "result" property is a JsonElement deserialized from a JSONB blob
        // that Python produces with snake_case keys. ASP.NET preserves raw keys
        // when serializing JsonElement, so the inner keys remain snake_case.
        var result = jobJson.GetProperty("result");
        result.GetProperty("schema_version").GetInt32().Should().Be(2);

        var hierarchy = result.GetProperty("hierarchy");
        hierarchy.GetProperty("source").GetString().Should().Be("ai");

        var categories = hierarchy.GetProperty("categories");
        categories.GetArrayLength().Should().Be(1);
        categories[0].GetProperty("name").GetString().Should().Be("Pizza");

        // Locale must have been threaded into the Python payload.
        _fake.Calls.Should().NotBeEmpty();
        var lastCall = _fake.Calls[^1];
        lastCall.Locale.Should().Be("ro-RO");
        lastCall.RestaurantId.Should().Be(_restA);
    }
}
