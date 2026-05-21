using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantApi.Data;
using RestaurantApi.Models;
using RestaurantApi.Tenancy;
using Testcontainers.PostgreSql;
using Xunit;

namespace RestaurantApi.Tests;

/// <summary>
/// Integration tests for the MenuImportJob entity: schema round-trip,
/// tenant stamping, tenant isolation, suppressed-resolution visibility,
/// and the composite (status, created_at) index path.
///
/// These tests bypass the full WebApplicationFactory / HTTP stack so they
/// can focus purely on EF Core behaviour without needing a JWT key.
/// A real Postgres container is used so that jsonb, indexes and query
/// filters all behave identically to production.
/// </summary>
public class MenuImportJobEntityTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private IServiceProvider _services = null!;

    public async Task InitializeAsync()
    {
        await _pg.StartAsync();

        var sc = new ServiceCollection();
        sc.AddScoped<ICurrentTenant, CurrentTenant>();
        sc.AddDbContext<AppDbContext>(opts =>
            opts.UseNpgsql(_pg.GetConnectionString()));

        _services = sc.BuildServiceProvider();

        // Apply all migrations so the menu_import_jobs table exists
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _pg.DisposeAsync();
    }

    // ── Test 1: RoundTrip ─────────────────────────────────────────────────────
    /// <summary>
    /// All fields survive a round-trip through the database — every column is
    /// persisted and read back with identical values.
    /// </summary>
    [Fact]
    public async Task RoundTrip_all_fields_survive_persist_and_reload()
    {
        var rid = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var completed = now.AddSeconds(30);
        var leaseId = Guid.NewGuid();
        var leaseExpires = now.AddMinutes(2);
        var heartbeat = now.AddSeconds(15);

        using var scope = _services.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        tenant.SetRestaurant(rid);

        var job = new MenuImportJob
        {
            Id = jobId,
            RestaurantId = rid,
            Status = "extracting",
            Phase = "classifying",
            TotalPages = 10,
            ProcessedPages = 3,
            FailedPages = 1,
            StorageKey = "uploads/test.pdf",
            OriginalFileName = "menu.pdf",
            FileSizeBytes = 204800L,
            ProviderUsed = "anthropic",
            ResultJson = """{"items":[]}""",
            ErrorMessage = null,
            CreatedAt = now,
            UpdatedAt = now,
            CompletedAt = completed,
            LeaseId = leaseId,
            LeaseExpiresAt = leaseExpires,
            LastHeartbeatAt = heartbeat,
            ProcessingAttempts = 1,
        };

        db.MenuImportJobs.Add(job);
        await db.SaveChangesAsync();

        // Reload from DB with no tracking
        var loaded = await db.MenuImportJobs
            .AsNoTracking()
            .SingleAsync(j => j.Id == jobId);

        loaded.Id.Should().Be(jobId);
        loaded.RestaurantId.Should().Be(rid);
        loaded.Status.Should().Be("extracting");
        loaded.Phase.Should().Be("classifying");
        loaded.TotalPages.Should().Be(10);
        loaded.ProcessedPages.Should().Be(3);
        loaded.FailedPages.Should().Be(1);
        loaded.StorageKey.Should().Be("uploads/test.pdf");
        loaded.OriginalFileName.Should().Be("menu.pdf");
        loaded.FileSizeBytes.Should().Be(204800L);
        loaded.ProviderUsed.Should().Be("anthropic");
        // jsonb in Postgres normalizes whitespace (e.g. adds space after ':'),
        // so compare the semantic content rather than the raw string.
        loaded.ResultJson.Should().NotBeNull();
        System.Text.Json.JsonDocument.Parse(loaded.ResultJson!)
            .RootElement.GetProperty("items").GetArrayLength()
            .Should().Be(0, "ResultJson jsonb content must round-trip correctly");
        loaded.ErrorMessage.Should().BeNull();
        // DateTime comparisons: within 1 second to absorb Postgres timestamp rounding
        loaded.CreatedAt.Should().BeCloseTo(now, TimeSpan.FromSeconds(1));
        loaded.UpdatedAt.Should().BeCloseTo(now, TimeSpan.FromSeconds(1));
        loaded.CompletedAt.Should().NotBeNull();
        loaded.CompletedAt!.Value.Should().BeCloseTo(completed, TimeSpan.FromSeconds(1));
        loaded.LeaseId.Should().Be(leaseId);
        loaded.LeaseExpiresAt.Should().NotBeNull();
        loaded.LeaseExpiresAt!.Value.Should().BeCloseTo(leaseExpires, TimeSpan.FromSeconds(1));
        loaded.LastHeartbeatAt.Should().NotBeNull();
        loaded.LastHeartbeatAt!.Value.Should().BeCloseTo(heartbeat, TimeSpan.FromSeconds(1));
        loaded.ProcessingAttempts.Should().Be(1);
    }

    // ── Test 2: TenantStamping ────────────────────────────────────────────────
    /// <summary>
    /// When RestaurantId is left at default (Guid.Empty), StampTenant() fills it
    /// in from the ambient ICurrentTenant during SaveChanges.
    /// </summary>
    [Fact]
    public async Task TenantStamping_auto_fills_RestaurantId_from_ambient_tenant()
    {
        var rid = Guid.NewGuid();

        using var scope = _services.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        tenant.SetRestaurant(rid);

        // Intentionally do NOT set RestaurantId — leave it at Guid.Empty
        var job = new MenuImportJob
        {
            StorageKey = "uploads/stamp-test.pdf",
            OriginalFileName = "stamp.pdf",
        };

        db.MenuImportJobs.Add(job);
        await db.SaveChangesAsync();

        // RestaurantId must have been stamped to rid in-memory
        job.RestaurantId.Should().Be(rid);

        // Verify it was persisted to the DB as well
        using var verifyScope = _services.CreateScope();
        var tenantV = verifyScope.ServiceProvider.GetRequiredService<ICurrentTenant>();
        var dbV = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        tenantV.SetRestaurant(rid);

        var loaded = await dbV.MenuImportJobs.AsNoTracking().SingleAsync(j => j.Id == job.Id);
        loaded.RestaurantId.Should().Be(rid);
    }

    // ── Test 3: TenantIsolation ───────────────────────────────────────────────
    /// <summary>
    /// A job inserted under RID-A is invisible when querying as RID-B.
    /// The global query filter must exclude cross-tenant rows.
    /// </summary>
    [Fact]
    public async Task TenantIsolation_job_from_RID_A_is_invisible_to_RID_B()
    {
        var ridA = Guid.NewGuid();
        var ridB = Guid.NewGuid();

        // Insert a job for RID-A
        using (var scope = _services.CreateScope())
        {
            var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            tenant.SetRestaurant(ridA);

            db.MenuImportJobs.Add(new MenuImportJob
            {
                RestaurantId = ridA,
                StorageKey = "uploads/isolation.pdf",
                OriginalFileName = "isolation.pdf",
            });
            await db.SaveChangesAsync();
        }

        // Query as RID-B — should see zero jobs
        using (var scope = _services.CreateScope())
        {
            var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            tenant.SetRestaurant(ridB);

            var count = await db.MenuImportJobs.CountAsync();
            count.Should().Be(0, "RID-B must not see RID-A's jobs");
        }
    }

    // ── Test 4: SuppressResolutionShowsAll ────────────────────────────────────
    /// <summary>
    /// With SuppressResolution() active, jobs from all tenants are visible —
    /// the global query filter is bypassed for deliberate cross-tenant operations.
    /// </summary>
    [Fact]
    public async Task SuppressResolution_shows_jobs_from_all_tenants()
    {
        var ridA = Guid.NewGuid();
        var ridB = Guid.NewGuid();

        // Insert one job per tenant
        using (var scope = _services.CreateScope())
        {
            var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            tenant.SetRestaurant(ridA);
            db.MenuImportJobs.Add(new MenuImportJob
            {
                RestaurantId = ridA,
                StorageKey = "uploads/suppA.pdf",
                OriginalFileName = "suppA.pdf",
            });
            await db.SaveChangesAsync();

            tenant.SetRestaurant(ridB);
            db.MenuImportJobs.Add(new MenuImportJob
            {
                RestaurantId = ridB,
                StorageKey = "uploads/suppB.pdf",
                OriginalFileName = "suppB.pdf",
            });
            await db.SaveChangesAsync();
        }

        // With suppression active, both jobs are visible
        using (var scope = _services.CreateScope())
        {
            var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            using var _ = tenant.SuppressResolution();

            var count = await db.MenuImportJobs.CountAsync();
            count.Should().BeGreaterThanOrEqualTo(2, "suppression must expose all tenant rows");
        }
    }

    // ── Test 5: StatusCreatedAtIndex ──────────────────────────────────────────
    /// <summary>
    /// Querying pending jobs ordered by CreatedAt runs without error.
    /// This exercises the composite index code path used by the background poller.
    /// </summary>
    [Fact]
    public async Task StatusCreatedAtIndex_pending_query_ordered_by_created_at_runs_without_error()
    {
        var rid = Guid.NewGuid();

        using var scope = _services.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        tenant.SetRestaurant(rid);

        // Seed two jobs: one pending, one complete
        db.MenuImportJobs.AddRange(
            new MenuImportJob
            {
                RestaurantId = rid,
                Status = "pending",
                StorageKey = "uploads/idx1.pdf",
                OriginalFileName = "idx1.pdf",
                CreatedAt = DateTime.UtcNow.AddMinutes(-5),
                UpdatedAt = DateTime.UtcNow.AddMinutes(-5),
            },
            new MenuImportJob
            {
                RestaurantId = rid,
                Status = "complete",
                StorageKey = "uploads/idx2.pdf",
                OriginalFileName = "idx2.pdf",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            }
        );
        await db.SaveChangesAsync();

        // This query mirrors the background poller hot path that uses
        // the IX_menu_import_jobs_status_created_at composite index
        var act = async () =>
        {
            var results = await db.MenuImportJobs
                .Where(j => j.Status == "pending")
                .OrderBy(j => j.CreatedAt)
                .ToListAsync();
            results.Should().HaveCount(1);
            results[0].Status.Should().Be("pending");
        };

        await act.Should().NotThrowAsync();
    }
}
