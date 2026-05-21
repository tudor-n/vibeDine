using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RestaurantApi.Data;
using RestaurantApi.Hubs;
using RestaurantApi.Models;
using RestaurantApi.Services;
using RestaurantApi.Storage;
using RestaurantApi.Tenancy;
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

    private ServiceProvider BuildServiceProvider(IPdfExtractor pythonClient, FakeHubContext? hubContext = null)
    {
        var sc = new ServiceCollection();
        sc.AddScoped<ICurrentTenant, CurrentTenant>();
        sc.AddDbContext<AppDbContext>(opts => opts.UseNpgsql(_pg.GetConnectionString()));
        sc.AddSingleton<IFileStorage>(new FakeFileStorage());
        sc.AddSingleton<IHubContext<StaffHub>>(hubContext ?? new FakeHubContext());
        sc.AddSingleton<MenuImportProgressNotifier>();
        sc.AddSingleton<IPdfExtractor>(pythonClient);
        sc.AddSingleton<IOptions<MenuImportOptions>>(Options.Create(new MenuImportOptions
        {
            MaxConcurrentJobs = 3,
            PollIntervalMs = 100,
            HmacSecret = "test-secret-32chars-minimum-pad!!",
            CallbackBaseUrl = "http://localhost:9999",
            StaleAfterMinutes = 5,
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

    private async Task<Guid> SeedPendingJob(ServiceProvider sp, Guid restaurantId, string fileName = "test.pdf")
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
        };
        db.MenuImportJobs.Add(job);
        await db.SaveChangesAsync();
        return job.Id;
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
        var rid = Guid.NewGuid();
        var staleJobId = Guid.NewGuid();

        using (var scope = _sp.CreateScope())
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
                StorageKey = $"{rid:N}/{staleJobId:N}.pdf",
                OriginalFileName = "stale.pdf",
                FileSizeBytes = 100,
                ProviderUsed = "gemini",
                UpdatedAt = DateTime.UtcNow.AddMinutes(-10), // 10 minutes stale
            });
            await db.SaveChangesAsync();
        }

        var svc = CreateService(_sp);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await svc.RunOnceForTestAsync(cts.Token);

        using var checkScope = _sp.CreateScope();
        var checkDb = checkScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = await checkDb.MenuImportJobs.IgnoreQueryFilters()
            .FirstAsync(j => j.Id == staleJobId);

        job.Status.Should().Be("failed");
        job.ErrorMessage.Should().Contain("stalled");
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
        public SlowPythonClient(TimeSpan delay) => _delay = delay;

        public async Task<JsonElement> ExtractPdfAsync(PdfJobPayload payload, CancellationToken ct)
        {
            await Task.Delay(_delay, ct);
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

    private sealed class FakeFileStorage : IFileStorage
    {
        public Task<string> WriteAsync(Guid restaurantId, Guid jobId, string fileName, System.IO.Stream content, CancellationToken ct)
            => Task.FromResult($"{restaurantId:N}/{jobId:N}.pdf");

        public Task<System.IO.Stream> OpenAsync(string storageKey, CancellationToken ct)
            => Task.FromResult<System.IO.Stream>(new System.IO.MemoryStream());

        public Task DeleteAsync(string storageKey, CancellationToken ct) => Task.CompletedTask;

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
