using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantApi.Data;
using RestaurantApi.Models;
using RestaurantApi.Storage;
using RestaurantApi.Tenancy;

namespace RestaurantApi.Services;

/// <summary>
/// Configuration for the menu-import pipeline.
/// Bound from the "MenuImport" configuration section.
/// </summary>
public sealed class MenuImportOptions
{
    public string? StorageRoot { get; set; }
    public int MaxConcurrentJobs { get; set; } = 3;
    public int PollIntervalMs { get; set; } = 2000;
    public long MaxFileBytes { get; set; } = 110_000_000;
    public string HmacSecret { get; set; } = "";
    public string CallbackBaseUrl { get; set; } = "";
    public string DefaultProvider { get; set; } = "gemini";
    public int StaleAfterMinutes { get; set; } = 5;
}

/// <summary>
/// Background service that polls for pending menu-import jobs and drives them
/// through the extraction pipeline by calling the Python microservice.
/// Mirrors the pattern established by <see cref="OrderFinalizationService"/>.
/// </summary>
public sealed class MenuImportBackgroundService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<MenuImportBackgroundService> _logger;
    private readonly MenuImportOptions _options;
    private readonly MenuImportProgressNotifier _notifier;

    private bool _staleRecoveryRun;

    public MenuImportBackgroundService(
        IServiceProvider services,
        ILogger<MenuImportBackgroundService> logger,
        IOptions<MenuImportOptions> options,
        MenuImportProgressNotifier notifier)
    {
        _services = services;
        _logger = logger;
        _options = options.Value;
        _notifier = notifier;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceForTestAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MenuImportBackgroundService poll iteration failed");
            }

            try
            {
                await Task.Delay(_options.PollIntervalMs, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Exposed for unit-testing: runs exactly one poll iteration synchronously.
    /// </summary>
    public async Task RunOnceForTestAsync(CancellationToken ct)
    {
        // First run: recover stale jobs stuck in "extracting" with no progress.
        if (!_staleRecoveryRun)
        {
            await RecoverStaleJobsAsync(ct);
            _staleRecoveryRun = true;
        }

        await ProcessPendingJobsAsync(ct);
    }

    // ── Stale recovery ────────────────────────────────────────────────────────

    private async Task RecoverStaleJobsAsync(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();

        using var _ = tenant.SuppressResolution();

        var cutoff = DateTime.UtcNow.AddMinutes(-_options.StaleAfterMinutes);
        var stale = await db.MenuImportJobs
            .Where(j => j.Status == "extracting" && j.UpdatedAt < cutoff)
            .ToListAsync(ct);

        if (stale.Count == 0) return;

        var now = DateTime.UtcNow;
        foreach (var job in stale)
        {
            job.Status = "failed";
            job.ErrorMessage = "Extraction stalled (no progress).";
            job.UpdatedAt = now;
        }
        await db.SaveChangesAsync(ct);

        _logger.LogWarning("MenuImportBackgroundService recovered {Count} stale job(s)", stale.Count);
    }

    // ── Main poll loop ────────────────────────────────────────────────────────

    private async Task ProcessPendingJobsAsync(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();

        using var _ = tenant.SuppressResolution();

        var pending = await db.MenuImportJobs
            .Where(j => j.Status == "pending")
            .OrderBy(j => j.CreatedAt)
            .Take(_options.MaxConcurrentJobs)
            .ToListAsync(ct);

        if (pending.Count == 0) return;

        // Mark all as "extracting" in one batch before releasing the scope.
        var now = DateTime.UtcNow;
        foreach (var job in pending)
        {
            job.Status = "extracting";
            job.UpdatedAt = now;
        }
        await db.SaveChangesAsync(ct);

        // Process concurrently; each job gets its own DI scope.
        await Task.WhenAll(pending.Select(job => WorkOneJobAsync(job, ct)));
    }

    private async Task WorkOneJobAsync(MenuImportJob job, CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
        var fileStorage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        var extractor = scope.ServiceProvider.GetRequiredService<IPdfExtractor>();

        // Set tenant context for this job's restaurant.
        tenant.SetRestaurant(job.RestaurantId);

        // Re-check: was the job cancelled while we were picking it?
        {
            using var sup = tenant.SuppressResolution();
            var fresh = await db.MenuImportJobs
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(j => j.Id == job.Id, ct);
            if (fresh is null || fresh.Status == "cancelled")
            {
                _logger.LogInformation("Job {JobId} cancelled before extraction started — skipping", job.Id);
                return;
            }
        }

        var callbackUrl = $"{_options.CallbackBaseUrl.TrimEnd('/')}/api/internal/menu-imports/{job.Id}/progress";
        var storagePath = fileStorage.ResolveLocalPath(job.StorageKey);

        var payload = new PdfJobPayload(
            JobId: job.Id,
            RestaurantId: job.RestaurantId,
            StoragePath: storagePath,
            ProgressCallbackUrl: callbackUrl,
            HmacSecret: _options.HmacSecret,
            Provider: job.ProviderUsed);

        try
        {
            var resultElement = await extractor.ExtractPdfAsync(payload, ct);
            var now = DateTime.UtcNow;

            // Re-check cancellation after the (potentially long) extraction call.
            {
                using var sup = tenant.SuppressResolution();
                var fresh = await db.MenuImportJobs
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(j => j.Id == job.Id, ct);

                if (fresh is null || fresh.Status == "cancelled")
                {
                    _logger.LogInformation("Job {JobId} cancelled during extraction — not saving result", job.Id);
                    return;
                }

                fresh.Status = "complete";
                fresh.Phase = "finalizing";
                fresh.ResultJson = resultElement.GetRawText();
                fresh.CompletedAt = now;
                fresh.UpdatedAt = now;
                await db.SaveChangesAsync(ct);
            }

            await _notifier.EmitCompletedAsync(job.RestaurantId, job.Id, new
            {
                jobId = job.Id,
                restaurantId = job.RestaurantId,
            }, ct);

            _logger.LogInformation("MenuImport job {JobId} completed for restaurant {RestaurantId}", job.Id, job.RestaurantId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "MenuImport job {JobId} failed", job.Id);

            using var sup = tenant.SuppressResolution();
            var fresh = await db.MenuImportJobs
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(j => j.Id == job.Id, CancellationToken.None);

            if (fresh is not null && fresh.Status != "cancelled")
            {
                fresh.Status = "failed";
                fresh.ErrorMessage = ex.Message;
                fresh.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(CancellationToken.None);
            }

            await _notifier.EmitFailedAsync(job.RestaurantId, job.Id, new
            {
                jobId = job.Id,
                error = ex.Message,
            }, CancellationToken.None);
        }
        finally
        {
            // Best-effort cleanup of the uploaded file.
            try
            {
                await fileStorage.DeleteAsync(job.StorageKey, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not delete storage file for job {JobId}", job.Id);
            }
        }
    }
}
