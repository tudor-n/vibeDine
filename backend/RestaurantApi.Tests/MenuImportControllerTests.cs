using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantApi.Controllers.Requests;
using RestaurantApi.Data;
using RestaurantApi.Models;
using RestaurantApi.Tests.Helpers;
using Testcontainers.PostgreSql;
using Xunit;

namespace RestaurantApi.Tests;

/// <summary>
/// Integration tests for MenuImportController. Uses a real Postgres container and
/// TestWebApplicationFactory (with auth bypassed via a known test JWT key).
/// </summary>
public class MenuImportControllerTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private MenuImportTestFactory _factory = null!;
    private HttpClient _client = null!;

    // Stable restaurant + user IDs for the primary test user.
    private static readonly Guid RestaurantA = Guid.NewGuid();
    private static readonly Guid RestaurantB = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid UserBId = Guid.NewGuid();

    private const string HmacSecret = "dev-hmac-secret-32chars-minimum-pad-1234";

    public async Task InitializeAsync()
    {
        await _pg.StartAsync();
        _factory = new MenuImportTestFactory
        {
            PostgresConnectionString = _pg.GetConnectionString(),
            HmacSecret = HmacSecret,
        };

        // Run migrations and seed BEFORE creating the client (which lazy-starts the server).
        // We use Services to trigger EnsureServer, but background services are disabled in the factory.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();

        // Create test users (required by Membership FK constraint).
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var userA = new AppUser { Id = UserId, UserName = "usera@test.com", Email = "usera@test.com", FullName = "User A" };
        var userB = new AppUser { Id = UserBId, UserName = "userb@test.com", Email = "userb@test.com", FullName = "User B" };
        await userManager.CreateAsync(userA, "TestPass1!");
        await userManager.CreateAsync(userB, "TestPass1!");

        // Seed organizations, restaurants, memberships.
        var orgId = Guid.NewGuid();
        db.Set<Organization>().Add(new Organization
        {
            Id = orgId,
            Name = "Test Org",
            Slug = "test-org-" + orgId.ToString("N")[..8],
            CreatedByUserId = UserId,
        });
        db.Set<Restaurant>().AddRange(
            new Restaurant { Id = RestaurantA, OrganizationId = orgId, Name = "RA", Slug = "ra-" + orgId.ToString("N")[..8], IsActive = true },
            new Restaurant { Id = RestaurantB, OrganizationId = orgId, Name = "RB", Slug = "rb-" + orgId.ToString("N")[..8], IsActive = true }
        );
        await db.SaveChangesAsync();

        // Membership for User A → Restaurant A; User B → Restaurant B.
        db.Set<Membership>().AddRange(
            new Membership { Id = Guid.NewGuid(), UserId = UserId, OrganizationId = orgId, RestaurantId = RestaurantA, Role = MembershipRole.RestaurantAdmin },
            new Membership { Id = Guid.NewGuid(), UserId = UserBId, OrganizationId = orgId, RestaurantId = RestaurantB, Role = MembershipRole.RestaurantAdmin }
        );
        await db.SaveChangesAsync();

        // Create authenticated client for User A (RestaurantA).
        _client = _factory.CreateAuthenticatedClient(UserId, RestaurantA);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        await _pg.DisposeAsync();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helper: build a valid minimal PDF multipart form
    // ──────────────────────────────────────────────────────────────────────────
    private static MultipartFormDataContent ValidPdfForm(string fileName = "menu.pdf", int extraBytes = 0)
    {
        // %PDF magic bytes followed by padding
        var bytes = new byte[4 + extraBytes];
        bytes[0] = 0x25; // '%'
        bytes[1] = 0x50; // 'P'
        bytes[2] = 0x44; // 'D'
        bytes[3] = 0x46; // 'F'

        var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(content, "pdf", fileName);
        return form;
    }

    private static string ComputeHmac(string secret, byte[] body)
    {
        using var h = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(h.ComputeHash(body)).ToLowerInvariant();
    }

    private async Task<HttpResponseMessage> SendProgressCallbackAsync(
        Guid routeJobId,
        ProgressCallbackRequest payload,
        Guid? leaseId = null)
    {
        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        var sig = ComputeHmac(HmacSecret, jsonBytes);

        var path = $"/api/internal/menu-imports/{routeJobId}/progress";
        if (leaseId is not null)
            path += $"?leaseId={leaseId.Value}";

        var request = new HttpRequestMessage(HttpMethod.Post,
            path);
        request.Content = new ByteArrayContent(jsonBytes);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.Add("X-Hmac-Signature", sig);

        using var anonClient = _factory.CreateClient();
        return await anonClient.SendAsync(request);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 1: Happy path — 202, DB row created, file on disk
    // ──────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task POST_pdf_happy_path_returns_202_with_jobId_and_creates_pending_row()
    {
        var resp = await _client.PostAsync(
            $"/api/restaurants/{RestaurantA}/admin/menu/imports/pdf",
            ValidPdfForm());

        resp.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var body = await resp.Content.ReadFromJsonAsync<StartPdfImportResponse>();
        body.Should().NotBeNull();
        body!.JobId.Should().NotBeEmpty();

        // Verify DB row exists with correct tenant and status.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = await db.MenuImportJobs
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(j => j.Id == body.JobId);

        job.Should().NotBeNull();
        job!.RestaurantId.Should().Be(RestaurantA);
        job.Status.Should().Be("pending");
        job.Phase.Should().Be("parsing");
        job.OriginalFileName.Should().Be("menu.pdf");
        job.ExtractItemPhotos.Should().BeFalse("the flag defaults off when the part is omitted");
    }

    [Fact]
    public async Task POST_pdf_persists_extract_item_photos_flag_when_set()
    {
        var form = ValidPdfForm();
        form.Add(new StringContent("true"), "extractItemPhotos");

        var resp = await _client.PostAsync(
            $"/api/restaurants/{RestaurantA}/admin/menu/imports/pdf", form);

        resp.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await resp.Content.ReadFromJsonAsync<StartPdfImportResponse>();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = await db.MenuImportJobs.IgnoreQueryFilters().FirstAsync(j => j.Id == body!.JobId);
        job.ExtractItemPhotos.Should().BeTrue();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 2: Bad MIME type → 400
    // ──────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task POST_pdf_wrong_content_type_returns_400()
    {
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // PNG magic
        var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(content, "pdf", "image.png");

        var resp = await _client.PostAsync(
            $"/api/restaurants/{RestaurantA}/admin/menu/imports/pdf",
            form);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 3: Missing file → 400
    // ──────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task POST_pdf_missing_file_returns_400()
    {
        var resp = await _client.PostAsync(
            $"/api/restaurants/{RestaurantA}/admin/menu/imports/pdf",
            new MultipartFormDataContent());

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 4: Bad magic bytes → 400
    // ──────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task POST_pdf_bad_magic_bytes_returns_400()
    {
        var bytes = Encoding.UTF8.GetBytes("NOTPDF - definitely not a pdf file");
        var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(content, "pdf", "fake.pdf");

        var resp = await _client.PostAsync(
            $"/api/restaurants/{RestaurantA}/admin/menu/imports/pdf",
            form);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("message").GetString().Should().Contain("PDF");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 5: Tenancy isolation — user of RID-A cannot GET job created under RID-B
    // ──────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task GET_job_from_different_restaurant_returns_404()
    {
        // Insert a job directly under RestaurantB.
        var jobId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.MenuImportJobs.Add(new MenuImportJob
            {
                Id = jobId,
                RestaurantId = RestaurantB,
                Status = "pending",
                Phase = "parsing",
                StorageKey = $"{RestaurantB:N}/{jobId:N}.pdf",
                OriginalFileName = "b.pdf",
                FileSizeBytes = 100,
                ProviderUsed = "gemini",
            });
            await db.SaveChangesAsync();
        }

        // Client authenticated as User A (RestaurantA) tries to access job from RestaurantA route
        // — the job belongs to B so the tenant filter (RestaurantA in scope) yields 404.
        var resp = await _client.GetAsync(
            $"/api/restaurants/{RestaurantA}/admin/menu/imports/{jobId}");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 6: GET own job — 200 with job details
    // ──────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task GET_job_for_own_restaurant_returns_200()
    {
        // First create a job via POST
        var postResp = await _client.PostAsync(
            $"/api/restaurants/{RestaurantA}/admin/menu/imports/pdf",
            ValidPdfForm("get_test.pdf"));
        postResp.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var created = await postResp.Content.ReadFromJsonAsync<StartPdfImportResponse>();

        var resp = await _client.GetAsync(
            $"/api/restaurants/{RestaurantA}/admin/menu/imports/{created!.JobId}");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var job = await resp.Content.ReadFromJsonAsync<MenuImportJobResponse>();
        job.Should().NotBeNull();
        job!.Id.Should().Be(created.JobId);
        job.Status.Should().Be("pending");
        job.OriginalFileName.Should().Be("get_test.pdf");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 7: DELETE → 200, status becomes cancelled
    // ──────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task DELETE_job_returns_200_and_sets_cancelled()
    {
        var postResp = await _client.PostAsync(
            $"/api/restaurants/{RestaurantA}/admin/menu/imports/pdf",
            ValidPdfForm("delete_test.pdf"));
        var created = await postResp.Content.ReadFromJsonAsync<StartPdfImportResponse>();
        created.Should().NotBeNull();
        var jobId = created!.JobId;

        string storagePath;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var job = await db.MenuImportJobs.IgnoreQueryFilters()
                .FirstAsync(j => j.Id == jobId);
            storagePath = System.IO.Path.Combine(
                _factory.StorageRoot,
                job.StorageKey.Replace('/', System.IO.Path.DirectorySeparatorChar));
        }
        System.IO.File.Exists(storagePath).Should().BeTrue();

        var delResp = await _client.DeleteAsync(
            $"/api/restaurants/{RestaurantA}/admin/menu/imports/{jobId}");
        delResp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var checkScope = _factory.Services.CreateScope();
        var checkDb = checkScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var checkJob = await checkDb.MenuImportJobs.IgnoreQueryFilters()
            .FirstAsync(j => j.Id == jobId);
        checkJob.Status.Should().Be("cancelled");
        System.IO.File.Exists(storagePath).Should().BeFalse();
    }

    [Fact]
    public async Task DELETE_terminal_job_returns_200_without_overwriting_terminal_state()
    {
        var jobId = Guid.NewGuid();
        var completedAt = DateTime.UtcNow.AddMinutes(-1);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.MenuImportJobs.Add(new MenuImportJob
            {
                Id = jobId,
                RestaurantId = RestaurantA,
                Status = "complete",
                Phase = "finalizing",
                StorageKey = $"{RestaurantA:N}/{jobId:N}.pdf",
                OriginalFileName = "already-complete.pdf",
                FileSizeBytes = 100,
                ProviderUsed = "gemini",
                ResultJson = """{"items":[]}""",
                CompletedAt = completedAt,
            });
            await db.SaveChangesAsync();
        }

        var delResp = await _client.DeleteAsync(
            $"/api/restaurants/{RestaurantA}/admin/menu/imports/{jobId}");

        delResp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var checkScope = _factory.Services.CreateScope();
        var checkDb = checkScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = await checkDb.MenuImportJobs.IgnoreQueryFilters()
            .FirstAsync(j => j.Id == jobId);
        job.Status.Should().Be("complete");
        job.ResultJson.Should().NotBeNull();
        job.CompletedAt.Should().NotBeNull();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 8a: Internal progress endpoint — valid HMAC → 200
    // ──────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task POST_internal_progress_valid_hmac_returns_200()
    {
        // Create a job first (suppressing tenant)
        var jobId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.MenuImportJobs.Add(new MenuImportJob
            {
                Id = jobId,
                RestaurantId = RestaurantA,
                Status = "extracting",
                Phase = "parsing",
                StorageKey = $"{RestaurantA:N}/{jobId:N}.pdf",
                OriginalFileName = "hmac_test.pdf",
                FileSizeBytes = 200,
                ProviderUsed = "gemini",
            });
            await db.SaveChangesAsync();
        }

        var payload = new ProgressCallbackRequest
        {
            JobId = jobId,
            Status = "extracting",
            Phase = "parsing",
            ProcessedPages = 3,
            TotalPages = 10,
            FailedPages = 0,
        };
        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        var sig = ComputeHmac(HmacSecret, jsonBytes);

        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"/api/internal/menu-imports/{jobId}/progress");
        request.Content = new ByteArrayContent(jsonBytes);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.Add("X-Hmac-Signature", sig);

        // Use the unauthenticated client for internal endpoints
        using var anonClient = _factory.CreateClient();
        var resp = await anonClient.SendAsync(request);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Internal progress endpoint — route/body jobId mismatch → 400
    // ──────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task POST_internal_progress_route_body_jobId_mismatch_returns_400()
    {
        var routeJobId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.MenuImportJobs.Add(new MenuImportJob
            {
                Id = routeJobId,
                RestaurantId = RestaurantA,
                Status = "extracting",
                Phase = "parsing",
                StorageKey = $"{RestaurantA:N}/{routeJobId:N}.pdf",
                OriginalFileName = "mismatch_test.pdf",
                FileSizeBytes = 200,
                ProviderUsed = "gemini",
            });
            await db.SaveChangesAsync();
        }

        var payload = new ProgressCallbackRequest
        {
            JobId = Guid.NewGuid(),
            Status = "extracting",
            Phase = "parsing",
            ProcessedPages = 3,
            TotalPages = 10,
            FailedPages = 0,
        };

        var resp = await SendProgressCallbackAsync(routeJobId, payload);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var checkScope = _factory.Services.CreateScope();
        var checkDb = checkScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = await checkDb.MenuImportJobs.IgnoreQueryFilters()
            .FirstAsync(j => j.Id == routeJobId);
        job.ProcessedPages.Should().Be(0);
        job.TotalPages.Should().Be(0);
        job.Status.Should().Be("extracting");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Internal progress endpoint — terminal cancelled job ignores later progress
    // ──────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task POST_internal_progress_cancelled_job_ignores_extracting_update()
    {
        var jobId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.MenuImportJobs.Add(new MenuImportJob
            {
                Id = jobId,
                RestaurantId = RestaurantA,
                Status = "cancelled",
                Phase = "cancelled",
                ProcessedPages = 4,
                TotalPages = 8,
                FailedPages = 1,
                StorageKey = $"{RestaurantA:N}/{jobId:N}.pdf",
                OriginalFileName = "cancelled_progress_test.pdf",
                FileSizeBytes = 200,
                ProviderUsed = "gemini",
            });
            await db.SaveChangesAsync();
        }

        var payload = new ProgressCallbackRequest
        {
            JobId = jobId,
            Status = "extracting",
            Phase = "parsing",
            ProcessedPages = 7,
            TotalPages = 10,
            FailedPages = 0,
            EtaSeconds = 12,
        };

        var resp = await SendProgressCallbackAsync(jobId, payload);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var checkScope = _factory.Services.CreateScope();
        var checkDb = checkScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = await checkDb.MenuImportJobs.IgnoreQueryFilters()
            .FirstAsync(j => j.Id == jobId);
        job.Status.Should().Be("cancelled");
        job.Phase.Should().Be("cancelled");
        job.ProcessedPages.Should().Be(4);
        job.TotalPages.Should().Be(8);
        job.FailedPages.Should().Be(1);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Internal progress endpoint — provider complete does not pre-complete the job
    // ──────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task POST_internal_progress_complete_payload_keeps_job_extracting()
    {
        var jobId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.MenuImportJobs.Add(new MenuImportJob
            {
                Id = jobId,
                RestaurantId = RestaurantA,
                Status = "extracting",
                Phase = "extracting",
                ProcessedPages = 7,
                TotalPages = 10,
                StorageKey = $"{RestaurantA:N}/{jobId:N}.pdf",
                OriginalFileName = "complete_progress_test.pdf",
                FileSizeBytes = 200,
                ProviderUsed = "gemini",
            });
            await db.SaveChangesAsync();
        }

        var payload = new ProgressCallbackRequest
        {
            JobId = jobId,
            Status = "complete",
            Phase = "finalizing",
            ProcessedPages = 10,
            TotalPages = 10,
            FailedPages = 0,
        };

        var resp = await SendProgressCallbackAsync(jobId, payload);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var checkScope = _factory.Services.CreateScope();
        var checkDb = checkScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = await checkDb.MenuImportJobs.IgnoreQueryFilters()
            .FirstAsync(j => j.Id == jobId);
        job.Status.Should().Be("extracting");
        job.Phase.Should().Be("finalizing");
        job.ProcessedPages.Should().Be(10);
        job.CompletedAt.Should().BeNull();
        job.ResultJson.Should().BeNull();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Internal progress endpoint — provider failure records progress but does not own terminal state
    // ──────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task POST_internal_progress_failed_payload_keeps_job_extracting_with_error_message()
    {
        var jobId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.MenuImportJobs.Add(new MenuImportJob
            {
                Id = jobId,
                RestaurantId = RestaurantA,
                Status = "extracting",
                Phase = "parsing",
                StorageKey = $"{RestaurantA:N}/{jobId:N}.pdf",
                OriginalFileName = "failed_progress_test.pdf",
                FileSizeBytes = 200,
                ProviderUsed = "gemini",
            });
            await db.SaveChangesAsync();
        }

        var payload = new ProgressCallbackRequest
        {
            JobId = jobId,
            Status = "failed",
            Phase = "parsing",
            ProcessedPages = 0,
            TotalPages = 0,
            FailedPages = 0,
            ErrorMessage = "Unreadable PDF",
        };

        var resp = await SendProgressCallbackAsync(jobId, payload);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var checkScope = _factory.Services.CreateScope();
        var checkDb = checkScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = await checkDb.MenuImportJobs.IgnoreQueryFilters()
            .FirstAsync(j => j.Id == jobId);
        job.Status.Should().Be("extracting");
        job.Phase.Should().Be("parsing");
        job.ErrorMessage.Should().Be("Unreadable PDF");
        job.CompletedAt.Should().BeNull();
    }

    [Fact]
    public async Task POST_internal_progress_without_error_clears_previous_error_message()
    {
        var jobId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.MenuImportJobs.Add(new MenuImportJob
            {
                Id = jobId,
                RestaurantId = RestaurantA,
                Status = "extracting",
                Phase = "parsing",
                StorageKey = $"{RestaurantA:N}/{jobId:N}.pdf",
                OriginalFileName = "progress-clear-error.pdf",
                FileSizeBytes = 100,
                ProviderUsed = "gemini",
                ErrorMessage = "Unreadable PDF",
            });
            await db.SaveChangesAsync();
        }

        var payload = new ProgressCallbackRequest
        {
            JobId = jobId,
            Status = "extracting",
            Phase = "extracting",
            ProcessedPages = 2,
            TotalPages = 4,
            FailedPages = 0,
            EtaSeconds = 12,
        };

        var resp = await SendProgressCallbackAsync(jobId, payload);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var checkScope = _factory.Services.CreateScope();
        var checkDb = checkScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = await checkDb.MenuImportJobs.IgnoreQueryFilters()
            .FirstAsync(j => j.Id == jobId);
        job.ErrorMessage.Should().BeNull();
        job.ProcessedPages.Should().Be(2);
    }

    [Fact]
    public async Task POST_internal_progress_without_matching_lease_is_ignored()
    {
        var jobId = Guid.NewGuid();
        var currentLeaseId = Guid.NewGuid();
        var staleLeaseId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.MenuImportJobs.Add(new MenuImportJob
            {
                Id = jobId,
                RestaurantId = RestaurantA,
                Status = "extracting",
                Phase = "extracting",
                StorageKey = $"{RestaurantA:N}/{jobId:N}.pdf",
                OriginalFileName = "stale-lease-progress.pdf",
                FileSizeBytes = 100,
                ProviderUsed = "gemini",
                LeaseId = currentLeaseId,
                LeaseExpiresAt = DateTime.UtcNow.AddMinutes(2),
                LastHeartbeatAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var payload = new ProgressCallbackRequest
        {
            JobId = jobId,
            Status = "failed",
            Phase = "parsing",
            ProcessedPages = 0,
            TotalPages = 4,
            FailedPages = 1,
            ErrorMessage = "Unreadable PDF from stale attempt",
        };

        var missingLeaseResp = await SendProgressCallbackAsync(jobId, payload);
        var staleLeaseResp = await SendProgressCallbackAsync(jobId, payload, staleLeaseId);

        missingLeaseResp.StatusCode.Should().Be(HttpStatusCode.OK);
        staleLeaseResp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var checkScope = _factory.Services.CreateScope();
        var checkDb = checkScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = await checkDb.MenuImportJobs.IgnoreQueryFilters()
            .FirstAsync(j => j.Id == jobId);
        job.Status.Should().Be("extracting");
        job.Phase.Should().Be("extracting");
        job.ErrorMessage.Should().BeNull();
        job.ProcessedPages.Should().Be(0);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 8b: Internal progress endpoint — invalid HMAC → 401
    // ──────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task POST_internal_progress_invalid_hmac_returns_401()
    {
        var jobId = Guid.NewGuid();
        var payload = new ProgressCallbackRequest { JobId = jobId, Status = "extracting", Phase = "parsing" };
        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(payload);

        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"/api/internal/menu-imports/{jobId}/progress");
        request.Content = new ByteArrayContent(jsonBytes);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.Add("X-Hmac-Signature", "badbadbadbad");

        using var anonClient = _factory.CreateClient();
        var resp = await anonClient.SendAsync(request);

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 8c: Internal progress endpoint — unknown jobId → 404
    // ──────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task POST_internal_progress_unknown_jobId_returns_404()
    {
        var jobId = Guid.NewGuid();
        var payload = new ProgressCallbackRequest { JobId = jobId, Status = "extracting", Phase = "parsing" };
        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        var sig = ComputeHmac(HmacSecret, jsonBytes);

        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"/api/internal/menu-imports/{jobId}/progress");
        request.Content = new ByteArrayContent(jsonBytes);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.Add("X-Hmac-Signature", sig);

        using var anonClient = _factory.CreateClient();
        var resp = await anonClient.SendAsync(request);

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
