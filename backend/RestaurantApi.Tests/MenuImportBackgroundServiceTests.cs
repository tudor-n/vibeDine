using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RestaurantApi.Data;
using RestaurantApi.Hubs;
using RestaurantApi.Models;
using RestaurantApi.Services;
using RestaurantApi.Storage;
using RestaurantApi.Tenancy;
using RestaurantApi.Tests.Helpers;
using Testcontainers.PostgreSql;
using Xunit;
using System.Linq;

namespace RestaurantApi.Tests;

/// <summary>
/// Unit-style tests for MenuImportBackgroundService.
/// Uses real Postgres (Testcontainers) + in-process service scope to avoid
/// faking EF internals, but keeps the Python client and SignalR hub mocked.
/// </summary>
public class MenuImportBackgroundServiceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private ServiceProvider _sp = null!;

    public async Task InitializeAsync()
    {
        await _pg.StartAsync();
        _sp = BuildServiceProvider(new FakePythonClient() as IPdfExtractor);
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _sp.DisposeAsync();
        await _pg.DisposeAsync();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private ServiceProvider BuildServiceProvider(
        IPdfExtractor pythonClient,
        FakeHubContext? hubContext = null,
        FakeFileStorage? fileStorage = null)
    {
        var sc = new ServiceCollection();
        sc.AddLogging();
        sc.AddScoped<ICurrentTenant, CurrentTenant>();
        sc.AddDbContext<AppDbContext>(opts => opts.UseNpgsql(_pg.GetConnectionString()));
        sc.AddSingleton<IFileStorage>(fileStorage ?? new FakeFileStorage());
        sc.AddSingleton<IHubContext<StaffHub>>(hubContext ?? new FakeHubContext());
        sc.AddSingleton<MenuImportProgressNotifier>();
        sc.AddSingleton<IPdfExtractor>(pythonClient);
        // Item-photo merge dependencies — used only when a job opts in.
        sc.AddSingleton<IWebHostEnvironment>(new TestWebHostEnv());
        sc.AddSingleton(provider => new UploadPaths(Path.Combine(
            provider.GetRequiredService<IWebHostEnvironment>().ContentRootPath, "wwwroot", "uploads")));
        sc.AddSingleton<ItemPhotoStorage>();
        sc.AddScoped<MenuImportPhotoMerger>();
        sc.AddSingleton<IOptions<MenuImportOptions>>(Options.Create(new MenuImportOptions
        {
            MaxConcurrentJobs = 3,
            PollIntervalMs = 100,
            HmacSecret = "test-secret-32chars-minimum-pad!!",
            CallbackBaseUrl = "http://localhost:9999",
            StaleAfterMinutes = 5,
            LeaseDurationSeconds = 2,
            HeartbeatIntervalSeconds = 1,
            MaxProcessingAttempts = 2,
        }));
        return sc.BuildServiceProvider();
    }

    private MenuImportBackgroundService CreateService(ServiceProvider sp)
    {
        return new MenuImportBackgroundService(
            sp,
            NullLogger<MenuImportBackgroundService>.Instance,
            sp.GetRequiredService<IOptions<MenuImportOptions>>(),
            sp.GetRequiredService<MenuImportProgressNotifier>()
        );
    }

    private async Task<Guid> SeedPendingJob(
        ServiceProvider sp, Guid restaurantId, string fileName = "test.pdf", bool extractItemPhotos = false)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
        using var _ = tenant.SuppressResolution();

        var job = new MenuImportJob
        {
            Id = Guid.NewGuid(),
            RestaurantId = restaurantId,
            Status = "pending",
            Phase = "parsing",
            StorageKey = $"{restaurantId:N}/test.pdf",
            OriginalFileName = fileName,
            FileSizeBytes = 100,
            ProviderUsed = "gemini",
            ExtractItemPhotos = extractItemPhotos,
        };
        db.MenuImportJobs.Add(job);
        await db.SaveChangesAsync();
        return job.Id;
    }

    // FF D8 FF .. — passes ItemPhotoStorage's JPEG magic check.
    private static readonly string TinyJpegBase64 =
        Convert.ToBase64String(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46 });

    // ── Item photo merge (B5) ────────────────────────────────────────────────

    [Fact]
    public async Task Item_photos_are_merged_into_result_and_base64_is_stripped()
    {
        var fake = new FakePdfExtractor
        {
            ItemPhotos = new[]
            {
                new { source_page = 0, source_index = 0, image_base64 = TinyJpegBase64, confidence = 0.9 },
            },
        };
        await using var sp = BuildServiceProvider(fake);
        using (var scope = sp.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();

        var rid = Guid.NewGuid();
        var jobId = await SeedPendingJob(sp, rid, "with-photos.pdf", extractItemPhotos: true);

        var svc = CreateService(sp);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await svc.RunOnceForTestAsync(cts.Token);

        using var checkScope = sp.CreateScope();
        var db = checkScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = await db.MenuImportJobs.IgnoreQueryFilters().FirstAsync(j => j.Id == jobId, cts.Token);
        job.Status.Should().Be("complete");
        job.ResultJson.Should().Contain("image_url");
        job.ResultJson.Should().NotContain("image_base64");
        job.ResultJson.Should().NotContain("item_photos");

        CleanupPhotos(sp, rid);
    }

    [Fact]
    public async Task Invalid_base64_photo_is_dropped_and_job_still_completes()
    {
        var fake = new FakePdfExtractor
        {
            ItemPhotos = new[]
            {
                new { source_page = 0, source_index = 0, image_base64 = "!!!not-base64!!!", confidence = 0.9 },
            },
        };
        await using var sp = BuildServiceProvider(fake);
        using (var scope = sp.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();

        var rid = Guid.NewGuid();
        var jobId = await SeedPendingJob(sp, rid, "bad-photo.pdf", extractItemPhotos: true);

        var svc = CreateService(sp);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await svc.RunOnceForTestAsync(cts.Token);

        using var checkScope = sp.CreateScope();
        var db = checkScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = await db.MenuImportJobs.IgnoreQueryFilters().FirstAsync(j => j.Id == jobId, cts.Token);
        job.Status.Should().Be("complete");
        // The bad photo is dropped (no stamp) but base64 is still stripped.
        job.ResultJson.Should().NotContain("image_url");
        job.ResultJson.Should().NotContain("image_base64");
        job.ResultJson.Should().NotContain("item_photos");

        CleanupPhotos(sp, rid);
    }

    private static void CleanupPhotos(ServiceProvider sp, Guid restaurantId)
    {
        var env = sp.GetRequiredService<IWebHostEnvironment>();
        var dir = Path.Combine(env.ContentRootPath, "wwwroot", "uploads", "menu-items", restaurantId.ToString("N"));
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    // ── Test 1: Happy path state machine ─────────────────────────────────────
    [Fact]
    public async Task Pending_job_transitions_to_complete_when_python_returns_success()
    {
        var ridA = Guid.NewGuid();
        var jobId = await SeedPendingJob(_sp, ridA);

        var svc = CreateService(_sp);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Run one tick
        await svc.RunOnceForTestAsync(cts.Token);

        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = await db.MenuImportJobs.IgnoreQueryFilters()
            .FirstAsync(j => j.Id == jobId, cts.Token);

        job.Status.Should().Be("complete");
        job.ResultJson.Should().NotBeNullOrEmpty();
        job.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Active_extraction_extends_lease_with_heartbeat()
    {
        ServiceProvider? sp = null;
        DateTime? initialHeartbeat = null;
        DateTime? observedHeartbeat = null;
        var client = new SlowPythonClient(
            TimeSpan.FromMilliseconds(1500),
            onStarted: async payload =>
            {
                using var scope = sp!.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
                using var _ = tenant.SuppressResolution();
                var job = await db.MenuImportJobs.IgnoreQueryFilters().SingleAsync(j => j.Id == payload.JobId);
                initialHeartbeat = job.LastHeartbeatAt;
            },
            onBeforeReturn: async payload =>
            {
                using var scope = sp!.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
                using var _ = tenant.SuppressResolution();
                var job = await db.MenuImportJobs.IgnoreQueryFilters().SingleAsync(j => j.Id == payload.JobId);
                observedHeartbeat = job.LastHeartbeatAt;
            });

        sp = BuildServiceProvider(client);
        await using var dispose = sp;
        using (var scope = sp.CreateScope())
        {
            var migrateDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await migrateDb.Database.MigrateAsync();
        }

        var rid = Guid.NewGuid();
        var jobId = await SeedPendingJob(sp, rid, "heartbeat.pdf");
        var svc = CreateService(sp);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        await svc.RunOnceForTestAsync(cts.Token);

        initialHeartbeat.Should().NotBeNull();
        observedHeartbeat.Should().NotBeNull();
        observedHeartbeat.Should().BeAfter(initialHeartbeat!.Value, "heartbeat should update metadata while extraction is still running");

        using var checkScope = sp.CreateScope();
        var db = checkScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = await db.MenuImportJobs.IgnoreQueryFilters().SingleAsync(j => j.Id == jobId, cts.Token);
        job.Status.Should().Be("complete");
        job.LastHeartbeatAt.Should().BeNull("terminal states clear lease metadata after completion");
        job.ProcessingAttempts.Should().Be(1);
    }

    // ── Test 2: Cancellation — status flip aborts work ────────────────────────
    [Fact]
    public async Task Cancelled_job_is_not_overwritten_after_cancellation_flip()
    {
        var ridA = Guid.NewGuid();
        var jobId = await SeedPendingJob(_sp, ridA, "cancel_test.pdf");

        // Use a slow Python client that checks for cancellation before responding
        var slowClient = new SlowPythonClient(delay: TimeSpan.FromMilliseconds(50));
        var sp2 = BuildServiceProvider(slowClient);
        using var scope0 = sp2.CreateScope();
        var db0 = scope0.ServiceProvider.GetRequiredService<AppDbContext>();
        await db0.Database.MigrateAsync(); // share same PG container, second SP just for this test

        // Directly seed the same DB
        using (var scope = sp2.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
            using var _ = tenant.SuppressResolution();
            db.MenuImportJobs.Add(new MenuImportJob
            {
                Id = jobId,
                RestaurantId = ridA,
                Status = "pending",
                Phase = "parsing",
                StorageKey = $"{ridA:N}/{jobId:N}.pdf",
                OriginalFileName = "cancel_test.pdf",
                FileSizeBytes = 100,
                ProviderUsed = "gemini",
            });
            // suppress unique constraint - jobId was already inserted in outer sp
        }

        // Use the original SP: manually flip to cancelled before processing completes
        // We do this by manipulating the fake client to flip status mid-flight.
        var ridB = Guid.NewGuid();
        var jobId2 = Guid.NewGuid();
        using (var scope = _sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
            using var _ = tenant.SuppressResolution();

            var job = await db.MenuImportJobs.IgnoreQueryFilters()
                .FirstAsync(j => j.Id == jobId);
            job.Status = "cancelled";
            job.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        var svc = CreateService(_sp);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await svc.RunOnceForTestAsync(cts.Token);

        // The job was cancelled before being picked up (status was already cancelled)
        // so it should remain cancelled, not transition to complete/extracting.
        using var checkScope = _sp.CreateScope();
        var checkDb = checkScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var finalJob = await checkDb.MenuImportJobs.IgnoreQueryFilters()
            .FirstAsync(j => j.Id == jobId);

        finalJob.Status.Should().Be("cancelled");

        await sp2.DisposeAsync();
    }

    // ── Test 3: Multi-tenant isolation ───────────────────────────────────────
    [Fact]
    public async Task Two_pending_jobs_from_different_tenants_both_complete_under_correct_restaurant()
    {
        var ridA = Guid.NewGuid();
        var ridB = Guid.NewGuid();

        var trackingClient = new TrackingPythonClient();
        var sp = BuildServiceProvider(trackingClient as IPdfExtractor);
        using var scope0 = sp.CreateScope();
        var db0 = scope0.ServiceProvider.GetRequiredService<AppDbContext>();
        await db0.Database.MigrateAsync();

        var jobIdA = Guid.NewGuid();
        var jobIdB = Guid.NewGuid();

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
            using var _ = tenant.SuppressResolution();

            db.MenuImportJobs.AddRange(
                new MenuImportJob
                {
                    Id = jobIdA,
                    RestaurantId = ridA,
                    Status = "pending",
                    Phase = "parsing",
                    StorageKey = $"{ridA:N}/{jobIdA:N}.pdf",
                    OriginalFileName = "a.pdf",
                    FileSizeBytes = 100,
                    ProviderUsed = "gemini",
                },
                new MenuImportJob
                {
                    Id = jobIdB,
                    RestaurantId = ridB,
                    Status = "pending",
                    Phase = "parsing",
                    StorageKey = $"{ridB:N}/{jobIdB:N}.pdf",
                    OriginalFileName = "b.pdf",
                    FileSizeBytes = 100,
                    ProviderUsed = "gemini",
                }
            );
            await db.SaveChangesAsync();
        }

        var svc = CreateService(sp);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await svc.RunOnceForTestAsync(cts.Token);

        // Both jobs should be complete
        using var checkScope = sp.CreateScope();
        var checkDb = checkScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jobs = await checkDb.MenuImportJobs.IgnoreQueryFilters()
            .Where(j => j.Id == jobIdA || j.Id == jobIdB)
            .ToListAsync();

        jobs.Should().HaveCount(2);
        jobs.All(j => j.Status == "complete").Should().BeTrue();

        // Each job's ResultJson should reflect the restaurant the client received
        var recordedRestaurantIds = trackingClient.RecordedRestaurantIds;
        recordedRestaurantIds.Should().Contain(ridA);
        recordedRestaurantIds.Should().Contain(ridB);

        await sp.DisposeAsync();
    }

    // ── Test 4: Stale recovery ────────────────────────────────────────────────
    [Fact]
    public async Task Stale_extracting_job_is_marked_failed_on_first_tick()
    {
        var storage = new FakeFileStorage();
        await using var sp = BuildServiceProvider(new FakePythonClient(), fileStorage: storage);
        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.MigrateAsync();
        }

        var rid = Guid.NewGuid();
        var staleJobId = Guid.NewGuid();
        var storageKey = $"{rid:N}/{staleJobId:N}.pdf";

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
            using var _ = tenant.SuppressResolution();

            db.MenuImportJobs.Add(new MenuImportJob
            {
                Id = staleJobId,
                RestaurantId = rid,
                Status = "extracting",
                Phase = "parsing",
                StorageKey = storageKey,
                OriginalFileName = "stale.pdf",
                FileSizeBytes = 100,
                ProviderUsed = "gemini",
                UpdatedAt = DateTime.UtcNow.AddMinutes(-10), // 10 minutes stale
            });
            await db.SaveChangesAsync();
        }

        var svc = CreateService(sp);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await svc.RunOnceForTestAsync(cts.Token);

        using var checkScope = sp.CreateScope();
        var checkDb = checkScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = await checkDb.MenuImportJobs.IgnoreQueryFilters()
            .FirstAsync(j => j.Id == staleJobId);

        job.Status.Should().Be("failed");
        job.ErrorMessage.Should().Contain("stalled");
        storage.DeletedKeys.Should().Contain(storageKey);
    }

    [Fact]
    public async Task Expired_extracting_job_with_attempts_remaining_is_reclaimed()
    {
        var rid = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        using (var scope = _sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
            using var _ = tenant.SuppressResolution();

            db.MenuImportJobs.Add(new MenuImportJob
            {
                Id = jobId,
                RestaurantId = rid,
                Status = "extracting",
                Phase = "parsing",
                StorageKey = $"{rid:N}/{jobId:N}.pdf",
                OriginalFileName = "expired-lease.pdf",
                FileSizeBytes = 100,
                ProviderUsed = "gemini",
                UpdatedAt = now.AddMinutes(-10),
                LeaseId = Guid.NewGuid(),
                LeaseExpiresAt = now.AddMinutes(-1),
                LastHeartbeatAt = now.AddMinutes(-10),
                ProcessingAttempts = 1,
                ErrorMessage = "Previous attempt failed",
            });
            await db.SaveChangesAsync();
        }

        var svc = CreateService(_sp);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await svc.RunOnceForTestAsync(cts.Token);

        using var checkScope = _sp.CreateScope();
        var checkDb = checkScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = await checkDb.MenuImportJobs.IgnoreQueryFilters()
            .FirstAsync(j => j.Id == jobId);

        job.Status.Should().Be("complete");
        job.ErrorMessage.Should().BeNull();
        job.ProcessingAttempts.Should().Be(2);
        job.LeaseId.Should().BeNull();
        job.LeaseExpiresAt.Should().BeNull();
        job.LastHeartbeatAt.Should().BeNull();
    }

    [Fact]
    public async Task Expired_extracting_retry_uses_current_failure_when_previous_attempt_had_progress_error()
    {
        await using var sp = BuildServiceProvider(new ThrowingPythonClient("provider timed out"));
        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.MigrateAsync();
        }

        var rid = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
            using var _ = tenant.SuppressResolution();

            db.MenuImportJobs.Add(new MenuImportJob
            {
                Id = jobId,
                RestaurantId = rid,
                Status = "extracting",
                Phase = "parsing",
                StorageKey = $"{rid:N}/{jobId:N}.pdf",
                OriginalFileName = "expired-retry-fails.pdf",
                FileSizeBytes = 100,
                ProviderUsed = "gemini",
                UpdatedAt = now.AddMinutes(-10),
                LeaseId = Guid.NewGuid(),
                LeaseExpiresAt = now.AddMinutes(-1),
                LastHeartbeatAt = now.AddMinutes(-10),
                ProcessingAttempts = 1,
                ErrorMessage = "Unreadable PDF",
            });
            await db.SaveChangesAsync();
        }

        var svc = CreateService(sp);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await svc.RunOnceForTestAsync(cts.Token);

        using var checkScope = sp.CreateScope();
        var checkDb = checkScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = await checkDb.MenuImportJobs.IgnoreQueryFilters()
            .FirstAsync(j => j.Id == jobId);

        job.Status.Should().Be("failed");
        job.ErrorMessage.Should().Be("provider timed out");
        job.ProcessingAttempts.Should().Be(2);
    }

    [Fact]
    public async Task Expired_extracting_job_with_exhausted_attempts_is_marked_failed()
    {
        var storage = new FakeFileStorage();
        await using var sp = BuildServiceProvider(new FakePythonClient(), fileStorage: storage);
        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.MigrateAsync();
        }

        var rid = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
            using var _ = tenant.SuppressResolution();

            db.MenuImportJobs.Add(new MenuImportJob
            {
                Id = jobId,
                RestaurantId = rid,
                Status = "extracting",
                Phase = "parsing",
                StorageKey = $"{rid:N}/{jobId:N}.pdf",
                OriginalFileName = "expired-exhausted.pdf",
                FileSizeBytes = 100,
                ProviderUsed = "gemini",
                UpdatedAt = now.AddMinutes(-10),
                LeaseId = Guid.NewGuid(),
                LeaseExpiresAt = now.AddMinutes(-1),
                LastHeartbeatAt = now.AddMinutes(-10),
                ProcessingAttempts = 2,
            });
            await db.SaveChangesAsync();
        }

        var svc = CreateService(sp);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await svc.RunOnceForTestAsync(cts.Token);

        using var checkScope = sp.CreateScope();
        var checkDb = checkScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = await checkDb.MenuImportJobs.IgnoreQueryFilters()
            .FirstAsync(j => j.Id == jobId);

        job.Status.Should().Be("failed");
        job.ErrorMessage.Should().Contain("stalled");
        job.LeaseId.Should().BeNull();
        job.LeaseExpiresAt.Should().BeNull();
        job.LastHeartbeatAt.Should().BeNull();
        storage.DeletedKeys.Should().Contain($"{rid:N}/{jobId:N}.pdf");
    }

    // ── Test 5: Failed SignalR contract ─────────────────────────────────────
    [Fact]
    public async Task Failed_job_event_payload_uses_errorMessage()
    {
        var hub = new FakeHubContext();
        await using var sp = BuildServiceProvider(new ThrowingPythonClient("extractor exploded"), hub);
        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.MigrateAsync();
        }

        var rid = Guid.NewGuid();
        var jobId = await SeedPendingJob(sp, rid, "failed_contract_test.pdf");

        var svc = CreateService(sp);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await svc.RunOnceForTestAsync(cts.Token);

        var failed = hub.SentMessages.Should()
            .ContainSingle(m => m.Method == "MenuImportFailed")
            .Which;
        var payload = JsonSerializer.SerializeToElement(failed.Args.Should().ContainSingle().Which);

        payload.TryGetProperty("errorMessage", out var errorMessage).Should().BeTrue();
        errorMessage.GetString().Should().Be("extractor exploded");
        payload.TryGetProperty("error", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Failed_job_preserves_progress_error_message_when_extractor_throws_later()
    {
        var hub = new FakeHubContext();
        var rid = Guid.NewGuid();
        ServiceProvider? sp = null;
        var client = new ProgressErrorThenThrowingPythonClient(
            "Response status code does not indicate success: 400",
            async jobId =>
            {
                using var scope = sp!.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
                using var _ = tenant.SuppressResolution();

                var job = await db.MenuImportJobs.IgnoreQueryFilters()
                    .FirstAsync(j => j.Id == jobId);
                job.ErrorMessage = "Unreadable PDF";
                job.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
            });

        sp = BuildServiceProvider(client, hub);
        await using var dispose = sp;
        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.MigrateAsync();
        }

        var jobId = await SeedPendingJob(sp, rid, "progress_error_test.pdf");

        var svc = CreateService(sp);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await svc.RunOnceForTestAsync(cts.Token);

        using var checkScope = sp.CreateScope();
        var checkDb = checkScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = await checkDb.MenuImportJobs.IgnoreQueryFilters()
            .FirstAsync(j => j.Id == jobId);
        job.Status.Should().Be("failed");
        job.ErrorMessage.Should().Be("Unreadable PDF");

        var failed = hub.SentMessages.Should()
            .ContainSingle(m => m.Method == "MenuImportFailed")
            .Which;
        var payload = JsonSerializer.SerializeToElement(failed.Args.Should().ContainSingle().Which);
        payload.GetProperty("errorMessage").GetString().Should().Be("Unreadable PDF");
    }

    [Fact]
    public async Task Cancelled_job_during_extractor_failure_does_not_emit_failed_event()
    {
        var hub = new FakeHubContext();
        var rid = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        ServiceProvider? sp = null;
        var client = new CancellingThrowingPythonClient(
            "extractor exploded after cancel",
            async () =>
            {
                using var scope = sp!.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
                using var _ = tenant.SuppressResolution();

                var job = await db.MenuImportJobs.IgnoreQueryFilters()
                    .FirstAsync(j => j.Id == jobId);
                job.Status = "cancelled";
                job.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
            });

        sp = BuildServiceProvider(client, hub);
        await using var _sp = sp;
        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.MigrateAsync();
            var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
            using var _ = tenant.SuppressResolution();
            db.MenuImportJobs.Add(new MenuImportJob
            {
                Id = jobId,
                RestaurantId = rid,
                Status = "pending",
                Phase = "parsing",
                StorageKey = $"{rid:N}/{jobId:N}.pdf",
                OriginalFileName = "cancel_then_fail.pdf",
                FileSizeBytes = 100,
                ProviderUsed = "gemini",
            });
            await db.SaveChangesAsync();
        }

        var svc = CreateService(sp);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await svc.RunOnceForTestAsync(cts.Token);

        hub.SentMessages.Should().NotContain(m => m.Method == "MenuImportFailed");
    }

    [Fact]
    public async Task Concurrent_service_instances_do_not_process_the_same_pending_job()
    {
        var client = new BlockingTrackingPythonClient();
        await using var sp = BuildServiceProvider(client);
        using (var scope = sp.CreateScope())
        {
            var migrateDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await migrateDb.Database.MigrateAsync();
        }

        var rid = Guid.NewGuid();
        var jobId = await SeedPendingJob(sp, rid, "lease-race.pdf");
        var svc1 = CreateService(sp);
        var svc2 = CreateService(sp);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var first = svc1.RunOnceForTestAsync(cts.Token);
        await client.Entered.WaitAsync(cts.Token);
        var second = svc2.RunOnceForTestAsync(cts.Token);

        client.Release();
        await Task.WhenAll(first, second);

        client.Calls.Should().Be(1);

        using var checkScope = sp.CreateScope();
        var db = checkScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = await db.MenuImportJobs.IgnoreQueryFilters().SingleAsync(j => j.Id == jobId, cts.Token);
        job.Status.Should().Be("complete");
        job.LeaseId.Should().BeNull();
        job.LeaseExpiresAt.Should().BeNull();
    }

    [Fact]
    public async Task Worker_that_loses_lease_does_not_complete_or_delete_file()
    {
        ServiceProvider? sp = null;
        var client = new LeaseStealingPythonClient(async jobId =>
        {
            using var scope = sp!.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
            using var _ = tenant.SuppressResolution();
            var job = await db.MenuImportJobs.IgnoreQueryFilters().SingleAsync(j => j.Id == jobId);
            job.LeaseId = Guid.NewGuid();
            job.LeaseExpiresAt = DateTime.UtcNow.AddMinutes(2);
            await db.SaveChangesAsync();
        });

        var storage = new FakeFileStorage();
        var hub = new FakeHubContext();
        sp = BuildServiceProvider(client, hub, storage);
        await using var dispose = sp;
        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.MigrateAsync();
        }

        var rid = Guid.NewGuid();
        var jobId = await SeedPendingJob(sp, rid, "lost-lease.pdf");
        var svc = CreateService(sp);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await svc.RunOnceForTestAsync(cts.Token);

        using var checkScope = sp.CreateScope();
        var dbCheck = checkScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = await dbCheck.MenuImportJobs.IgnoreQueryFilters().SingleAsync(j => j.Id == jobId, cts.Token);
        job.Status.Should().Be("extracting");
        storage.DeletedKeys.Should().BeEmpty();
        hub.SentMessages.Should().NotContain(m => m.Method == "MenuImportCompleted" || m.Method == "MenuImportFailed");
    }

    // ── Fakes ─────────────────────────────────────────────────────────────────

    private sealed class FakePythonClient : IPdfExtractor
    {
        public Task<JsonElement> ExtractPdfAsync(PdfJobPayload payload, CancellationToken ct)
        {
            var json = JsonSerializer.SerializeToDocument(new { items = Array.Empty<object>(), provider = payload.Provider })
                .RootElement;
            return Task.FromResult(json);
        }
    }

    private sealed class SlowPythonClient : IPdfExtractor
    {
        private readonly TimeSpan _delay;
        private readonly Func<PdfJobPayload, Task>? _onStarted;
        private readonly Func<PdfJobPayload, Task>? _onBeforeReturn;

        public SlowPythonClient(
            TimeSpan delay,
            Func<PdfJobPayload, Task>? onStarted = null,
            Func<PdfJobPayload, Task>? onBeforeReturn = null)
        {
            _delay = delay;
            _onStarted = onStarted;
            _onBeforeReturn = onBeforeReturn;
        }

        public async Task<JsonElement> ExtractPdfAsync(PdfJobPayload payload, CancellationToken ct)
        {
            if (_onStarted is not null)
                await _onStarted(payload);

            await Task.Delay(_delay, ct);

            if (_onBeforeReturn is not null)
                await _onBeforeReturn(payload);

            var json = JsonSerializer.SerializeToDocument(new { items = Array.Empty<object>() }).RootElement;
            return json;
        }
    }

    private sealed class ThrowingPythonClient : IPdfExtractor
    {
        private readonly string _message;
        public ThrowingPythonClient(string message) => _message = message;

        public Task<JsonElement> ExtractPdfAsync(PdfJobPayload payload, CancellationToken ct)
            => throw new InvalidOperationException(_message);
    }

    private sealed class CancellingThrowingPythonClient : IPdfExtractor
    {
        private readonly string _message;
        private readonly Func<Task> _cancel;

        public CancellingThrowingPythonClient(string message, Func<Task> cancel)
        {
            _message = message;
            _cancel = cancel;
        }

        public async Task<JsonElement> ExtractPdfAsync(PdfJobPayload payload, CancellationToken ct)
        {
            await _cancel();
            throw new InvalidOperationException(_message);
        }
    }

    private sealed class ProgressErrorThenThrowingPythonClient : IPdfExtractor
    {
        private readonly string _message;
        private readonly Func<Guid, Task> _recordProgressError;

        public ProgressErrorThenThrowingPythonClient(string message, Func<Guid, Task> recordProgressError)
        {
            _message = message;
            _recordProgressError = recordProgressError;
        }

        public async Task<JsonElement> ExtractPdfAsync(PdfJobPayload payload, CancellationToken ct)
        {
            await _recordProgressError(payload.JobId);
            throw new InvalidOperationException(_message);
        }
    }

    private sealed class TrackingPythonClient : IPdfExtractor
    {
        public List<Guid> RecordedRestaurantIds { get; } = new();

        public Task<JsonElement> ExtractPdfAsync(PdfJobPayload payload, CancellationToken ct)
        {
            lock (RecordedRestaurantIds)
                RecordedRestaurantIds.Add(payload.RestaurantId);

            var json = JsonSerializer.SerializeToDocument(new { items = Array.Empty<object>() }).RootElement;
            return Task.FromResult(json);
        }
    }

    private sealed class BlockingTrackingPythonClient : IPdfExtractor
    {
        private readonly TaskCompletionSource _entered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);
        public Task Entered => _entered.Task;
        public void Release() => _release.TrySetResult();

        public async Task<JsonElement> ExtractPdfAsync(PdfJobPayload payload, CancellationToken ct)
        {
            if (Interlocked.Increment(ref _calls) == 1)
                _entered.TrySetResult();

            await _release.Task.WaitAsync(ct);
            return JsonSerializer.SerializeToDocument(new { items = Array.Empty<object>() }).RootElement;
        }
    }

    private sealed class LeaseStealingPythonClient : IPdfExtractor
    {
        private readonly Func<Guid, Task> _stealLease;

        public LeaseStealingPythonClient(Func<Guid, Task> stealLease) => _stealLease = stealLease;

        public async Task<JsonElement> ExtractPdfAsync(PdfJobPayload payload, CancellationToken ct)
        {
            await _stealLease(payload.JobId);
            return JsonSerializer.SerializeToDocument(new { items = Array.Empty<object>() }).RootElement;
        }
    }

    private sealed class TestWebHostEnv : IWebHostEnvironment
    {
        public TestWebHostEnv()
        {
            ContentRootPath = Path.Combine(Path.GetTempPath(), "menu-import-bg-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(ContentRootPath);
        }

        public string ApplicationName { get; set; } = "Test";
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
        public string ContentRootPath { get; set; }
        public string EnvironmentName { get; set; } = "Test";
        public IFileProvider WebRootFileProvider { get; set; } = null!;
        public string WebRootPath { get; set; } = null!;
    }

    private sealed class FakeFileStorage : IFileStorage
    {
        public List<string> DeletedKeys { get; } = new();

        public Task<string> WriteAsync(Guid restaurantId, Guid jobId, string fileName, System.IO.Stream content, CancellationToken ct)
            => Task.FromResult($"{restaurantId:N}/{jobId:N}.pdf");

        public Task<System.IO.Stream> OpenAsync(string storageKey, CancellationToken ct)
            => Task.FromResult<System.IO.Stream>(new System.IO.MemoryStream());

        public Task DeleteAsync(string storageKey, CancellationToken ct)
        {
            lock (DeletedKeys)
                DeletedKeys.Add(storageKey);

            return Task.CompletedTask;
        }

        public string ResolveLocalPath(string storageKey) => $"C:\\fake\\{storageKey.Replace('/', '\\')}";
    }

    private sealed class FakeHubContext : IHubContext<StaffHub>
    {
        public List<SentHubMessage> SentMessages { get; } = new();
        public IHubClients Clients { get; }
        public IGroupManager Groups { get; } = new FakeGroupManager();

        public FakeHubContext()
        {
            Clients = new FakeHubClients(this);
        }

        public sealed record SentHubMessage(string Method, object?[] Args);

        private sealed class FakeHubClients : IHubClients
        {
            private readonly FakeHubContext _context;
            public FakeHubClients(FakeHubContext context) => _context = context;

            public IClientProxy All => new FakeClientProxy(_context);
            public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => new FakeClientProxy(_context);
            public IClientProxy Client(string connectionId) => new FakeClientProxy(_context);
            public IClientProxy Clients(IReadOnlyList<string> connectionIds) => new FakeClientProxy(_context);
            public IClientProxy Group(string groupName) => new FakeClientProxy(_context);
            public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => new FakeClientProxy(_context);
            public IClientProxy Groups(IReadOnlyList<string> groupNames) => new FakeClientProxy(_context);
            public IClientProxy User(string userId) => new FakeClientProxy(_context);
            public IClientProxy Users(IReadOnlyList<string> userIds) => new FakeClientProxy(_context);
        }

        private sealed class FakeClientProxy : IClientProxy
        {
            private readonly FakeHubContext _context;
            public FakeClientProxy(FakeHubContext context) => _context = context;

            public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
            {
                lock (_context.SentMessages)
                    _context.SentMessages.Add(new SentHubMessage(method, args));

                return Task.CompletedTask;
            }
        }

        private sealed class FakeGroupManager : IGroupManager
        {
            public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
                => Task.CompletedTask;
            public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
                => Task.CompletedTask;
        }
    }
}
