using System.Text.Json;
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
    public int LeaseDurationSeconds { get; set; } = 120;
    public int HeartbeatIntervalSeconds { get; set; } = 30;
    public int MaxProcessingAttempts { get; set; } = 2;
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

    private static bool IsTerminalStatus(string status) =>
        string.Equals(status, "cancelled", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "complete", StringComparison.OrdinalIgnoreCase);

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
        await RecoverStaleJobsAsync(ct);
        await ProcessPendingJobsAsync(ct);
    }

    // ── Stale recovery ────────────────────────────────────────────────────────

    private async Task RecoverStaleJobsAsync(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
        var fileStorage = scope.ServiceProvider.GetRequiredService<IFileStorage>();

        using var _ = tenant.SuppressResolution();

        var now = DateTime.UtcNow;
        var cutoff = now.AddMinutes(-_options.StaleAfterMinutes);
        var staleJobs = await db.MenuImportJobs
            .IgnoreQueryFilters()
            .Where(j =>
                j.Status == "extracting" &&
                (
                    (j.UpdatedAt < cutoff &&
                     j.LeaseId == null &&
                     j.LeaseExpiresAt == null &&
                     j.LastHeartbeatAt == null)
                    ||
                    (j.LeaseExpiresAt != null &&
                     j.LeaseExpiresAt < now &&
                     j.ProcessingAttempts >= _options.MaxProcessingAttempts)
                ))
            .Select(j => new { j.Id, j.StorageKey })
            .ToListAsync(ct);

        if (staleJobs.Count == 0) return;

        var recoveredJobs = new List<(Guid Id, string StorageKey)>();
        foreach (var staleJob in staleJobs)
        {
            var recovered = await db.MenuImportJobs
                .IgnoreQueryFilters()
                .Where(j =>
                    j.Id == staleJob.Id &&
                    j.Status == "extracting" &&
                    (
                        (j.UpdatedAt < cutoff &&
                         j.LeaseId == null &&
                         j.LeaseExpiresAt == null &&
                         j.LastHeartbeatAt == null)
                        ||
                        (j.LeaseExpiresAt != null &&
                         j.LeaseExpiresAt < now &&
                         j.ProcessingAttempts >= _options.MaxProcessingAttempts)
                    ))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(j => j.Status, "failed")
                    .SetProperty(j => j.ErrorMessage, "Extraction stalled after the processing lease expired.")
                    .SetProperty(j => j.UpdatedAt, now)
                    .SetProperty(j => j.LeaseId, (Guid?)null)
                    .SetProperty(j => j.LeaseExpiresAt, (DateTime?)null)
                    .SetProperty(j => j.LastHeartbeatAt, (DateTime?)null), ct);

            if (recovered == 1)
                recoveredJobs.Add((staleJob.Id, staleJob.StorageKey));
        }

        if (recoveredJobs.Count > 0)
        {
            _logger.LogWarning("MenuImportBackgroundService recovered {Count} stale job(s)", recoveredJobs.Count);
            foreach (var job in recoveredJobs)
            {
                try
                {
                    await fileStorage.DeleteAsync(job.StorageKey, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not delete storage file for stale menu import job {JobId}", job.Id);
                }
            }
        }
    }

    // ── Main poll loop ────────────────────────────────────────────────────────

    private async Task ProcessPendingJobsAsync(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();

        using var _ = tenant.SuppressResolution();

        var pending = await ClaimJobsAsync(db, ct);

        if (pending.Count == 0) return;

        // Process concurrently; each job gets its own DI scope.
        await Task.WhenAll(pending.Select(job => WorkOneJobAsync(job, ct)));
    }

    private async Task<List<MenuImportJob>> ClaimJobsAsync(AppDbContext db, CancellationToken ct)
    {
        var leaseId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var expiresAt = now.AddSeconds(_options.LeaseDurationSeconds);

        return await db.MenuImportJobs
            .FromSqlInterpolated($"""
                WITH picked AS (
                    SELECT id
                    FROM menu_import_jobs
                    WHERE (
                        status = 'pending'
                        OR (status = 'extracting' AND lease_expires_at IS NOT NULL AND lease_expires_at < {now})
                    )
                    AND processing_attempts < {_options.MaxProcessingAttempts}
                    ORDER BY created_at
                    FOR UPDATE SKIP LOCKED
                    LIMIT {_options.MaxConcurrentJobs}
                )
                UPDATE menu_import_jobs AS j
                SET status = 'extracting',
                    lease_id = {leaseId},
                    lease_expires_at = {expiresAt},
                    last_heartbeat_at = {now},
                    processing_attempts = processing_attempts + 1,
                    error_message = NULL,
                    updated_at = {now}
                FROM picked
                WHERE j.id = picked.id
                RETURNING j.*
                """)
            .IgnoreQueryFilters()
            .AsNoTracking()
            .ToListAsync(ct);
    }

    private async Task WorkOneJobAsync(MenuImportJob job, CancellationToken ct)
    {
        if (job.LeaseId is not { } leaseId)
        {
            _logger.LogWarning("MenuImport job {JobId} was claimed without a lease id; skipping", job.Id);
            return;
        }

        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
        var fileStorage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        var extractor = scope.ServiceProvider.GetRequiredService<IPdfExtractor>();
        var terminalStateWrittenByThisWorker = false;

        // Set tenant context for this job's restaurant.
        tenant.SetRestaurant(job.RestaurantId);

        // Re-check: was the job cancelled while we were picking it?
        {
            using var sup = tenant.SuppressResolution();
            var fresh = await db.MenuImportJobs
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(j => j.Id == job.Id, ct);
            if (fresh is null || IsTerminalStatus(fresh.Status) || fresh.LeaseId != leaseId)
            {
                _logger.LogInformation("Job {JobId} reached a terminal state before extraction started — skipping", job.Id);
                return;
            }
        }

        var callbackUrl = $"{_options.CallbackBaseUrl.TrimEnd('/')}/api/internal/menu-imports/{job.Id}/progress?leaseId={leaseId}";
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
            JsonElement resultElement;
            using (var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                var heartbeatTask = RunHeartbeatAsync(job.Id, leaseId, heartbeatCts.Token);
                try
                {
                    resultElement = await extractor.ExtractPdfAsync(payload, ct);
                }
                finally
                {
                    await heartbeatCts.CancelAsync();
                    try
                    {
                        await heartbeatTask;
                    }
                    catch (OperationCanceledException) when (heartbeatCts.IsCancellationRequested)
                    {
                    }
                }
            }

            using (var sup = tenant.SuppressResolution())
            {
                terminalStateWrittenByThisWorker = await TryCompleteJobAsync(db, job.Id, leaseId, resultElement, ct);
            }

            if (!terminalStateWrittenByThisWorker)
            {
                _logger.LogInformation("Job {JobId} lease was lost before completion; not saving result", job.Id);
                return;
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
            if (terminalStateWrittenByThisWorker)
            {
                _logger.LogError(ex, "MenuImport job {JobId} failed after terminal state was written", job.Id);
                return;
            }

            _logger.LogError(ex, "MenuImport job {JobId} failed", job.Id);

            using var sup = tenant.SuppressResolution();
            var failResult = await TryFailJobAsync(db, job.Id, leaseId, ex.Message, CancellationToken.None);
            terminalStateWrittenByThisWorker = failResult.Written;
            if (terminalStateWrittenByThisWorker)
            {
                await _notifier.EmitFailedAsync(job.RestaurantId, job.Id, new
                {
                    jobId = job.Id,
                    errorMessage = failResult.ErrorMessage,
                }, CancellationToken.None);
            }
        }
        finally
        {
            if (terminalStateWrittenByThisWorker)
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

    private async Task RunHeartbeatAsync(Guid jobId, Guid leaseId, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.HeartbeatIntervalSeconds), ct);

                var now = DateTime.UtcNow;
                var expiresAt = now.AddSeconds(_options.LeaseDurationSeconds);

                using var scope = _services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
                using var _ = tenant.SuppressResolution();

                var updated = await db.MenuImportJobs
                    .IgnoreQueryFilters()
                    .Where(j =>
                        j.Id == jobId &&
                        j.LeaseId == leaseId &&
                        j.Status != "cancelled" &&
                        j.Status != "failed" &&
                        j.Status != "complete")
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(j => j.LeaseExpiresAt, expiresAt)
                        .SetProperty(j => j.LastHeartbeatAt, now)
                        .SetProperty(j => j.UpdatedAt, now), ct);

                if (updated == 0)
                    return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not heartbeat menu import job {JobId}", jobId);
            }
        }
    }

    private async Task<bool> TryCompleteJobAsync(
        AppDbContext db,
        Guid jobId,
        Guid leaseId,
        JsonElement resultElement,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var resultJson = resultElement.GetRawText();

        var updated = await db.MenuImportJobs
            .IgnoreQueryFilters()
            .Where(j =>
                j.Id == jobId &&
                j.LeaseId == leaseId &&
                j.Status != "cancelled" &&
                j.Status != "failed" &&
                j.Status != "complete")
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(j => j.Status, "complete")
                .SetProperty(j => j.Phase, "finalizing")
                .SetProperty(j => j.ResultJson, resultJson)
                .SetProperty(j => j.ErrorMessage, (string?)null)
                .SetProperty(j => j.CompletedAt, now)
                .SetProperty(j => j.UpdatedAt, now)
                .SetProperty(j => j.LeaseId, (Guid?)null)
                .SetProperty(j => j.LeaseExpiresAt, (DateTime?)null)
                .SetProperty(j => j.LastHeartbeatAt, (DateTime?)null), ct);

        return updated == 1;
    }

    private async Task<(bool Written, string ErrorMessage)> TryFailJobAsync(
        AppDbContext db,
        Guid jobId,
        Guid leaseId,
        string errorMessage,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var priorErrorMessage = await db.MenuImportJobs
            .IgnoreQueryFilters()
            .Where(j =>
                j.Id == jobId &&
                j.LeaseId == leaseId &&
                j.Status != "cancelled" &&
                j.Status != "failed" &&
                j.Status != "complete")
            .Select(j => j.ErrorMessage)
            .FirstOrDefaultAsync(ct);
        var effectiveErrorMessage = string.IsNullOrWhiteSpace(priorErrorMessage)
            ? errorMessage
            : priorErrorMessage;

        var updated = await db.MenuImportJobs
            .IgnoreQueryFilters()
            .Where(j =>
                j.Id == jobId &&
                j.LeaseId == leaseId &&
                j.Status != "cancelled" &&
                j.Status != "failed" &&
                j.Status != "complete")
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(j => j.Status, "failed")
                .SetProperty(j => j.ErrorMessage, effectiveErrorMessage)
                .SetProperty(j => j.UpdatedAt, now)
                .SetProperty(j => j.LeaseId, (Guid?)null)
                .SetProperty(j => j.LeaseExpiresAt, (DateTime?)null)
                .SetProperty(j => j.LastHeartbeatAt, (DateTime?)null), ct);

        return (updated == 1, effectiveErrorMessage);
    }
}
