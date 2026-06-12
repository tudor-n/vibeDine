# PDF Import Operational Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **Git is user-managed in this project.** Tasks end with a "Checkpoint" step that lists ready files and a suggested commit message. Do not run `git add` or `git commit` automatically.

**Goal:** Harden the PDF import pipeline so large scanned PDFs cannot overwhelm providers or be processed twice across API instances.

**Architecture:** Keep the current C# background worker calling the Python `/menu/extract-pdf` endpoint. Add PostgreSQL-backed job leasing and heartbeats in C#, process-wide provider backpressure plus per-job vision extractor reuse in Python, and a benchmark script that measures large PDF behavior without committing large fixtures.

**Tech Stack:** ASP.NET Core 8, EF Core + PostgreSQL, xUnit + Testcontainers; Python FastAPI service code, pytest + pytest-asyncio, pypdfium2-backed rendering.

**Spec:** `docs/superpowers/specs/2026-05-21-pdf-import-operational-hardening-design.md`

---

## Task 1: C# Lease Schema and Atomic Claiming

**Files:**
- Modify: `backend/RestaurantApi/Models/MenuImportJob.cs`
- Modify: `backend/RestaurantApi/Data/AppDbContext.cs`
- Create: `backend/RestaurantApi/Migrations/20260521120000_AddMenuImportJobLeases.cs`
- Create: `backend/RestaurantApi/Migrations/20260521120000_AddMenuImportJobLeases.Designer.cs`
- Modify: `backend/RestaurantApi/Migrations/AppDbContextModelSnapshot.cs`
- Modify: `backend/RestaurantApi/Services/MenuImportBackgroundService.cs`
- Modify: `backend/RestaurantApi.Tests/MenuImportJobEntityTests.cs`
- Modify: `backend/RestaurantApi.Tests/MenuImportBackgroundServiceTests.cs`

- [ ] **Step 1: Write the failing entity round-trip test**

Add lease fields to `RoundTrip_all_fields_survive_persist_and_reload` in `backend/RestaurantApi.Tests/MenuImportJobEntityTests.cs`:

```csharp
var leaseId = Guid.NewGuid();
var leaseExpires = now.AddMinutes(2);
var heartbeat = now.AddSeconds(15);

// In the MenuImportJob initializer:
LeaseId = leaseId,
LeaseExpiresAt = leaseExpires,
LastHeartbeatAt = heartbeat,
ProcessingAttempts = 1,

// After CompletedAt assertions:
loaded.LeaseId.Should().Be(leaseId);
loaded.LeaseExpiresAt.Should().NotBeNull();
loaded.LeaseExpiresAt!.Value.Should().BeCloseTo(leaseExpires, TimeSpan.FromSeconds(1));
loaded.LastHeartbeatAt.Should().NotBeNull();
loaded.LastHeartbeatAt!.Value.Should().BeCloseTo(heartbeat, TimeSpan.FromSeconds(1));
loaded.ProcessingAttempts.Should().Be(1);
```

- [ ] **Step 2: Run the entity test and verify RED**

Run:

```bash
dotnet test backend/RestaurantApi.Tests/RestaurantApi.Tests.csproj --filter "FullyQualifiedName~MenuImportJobEntityTests.RoundTrip_all_fields_survive_persist_and_reload"
```

Expected: compile failure because `MenuImportJob` has no lease properties.

- [ ] **Step 3: Add lease properties and EF mapping**

Add to `backend/RestaurantApi/Models/MenuImportJob.cs`:

```csharp
public Guid? LeaseId { get; set; }
public DateTime? LeaseExpiresAt { get; set; }
public DateTime? LastHeartbeatAt { get; set; }
public int ProcessingAttempts { get; set; }
```

Add to `backend/RestaurantApi/Data/AppDbContext.cs` inside the `MenuImportJob` mapping:

```csharp
e.Property(j => j.LeaseId).HasColumnName("lease_id");
e.Property(j => j.LeaseExpiresAt).HasColumnName("lease_expires_at");
e.Property(j => j.LastHeartbeatAt).HasColumnName("last_heartbeat_at");
e.Property(j => j.ProcessingAttempts).HasColumnName("processing_attempts");
e.HasIndex(j => new { j.Status, j.LeaseExpiresAt });
```

Create the migration using the project convention if `dotnet ef` is available. If not, manually add these columns:

```csharp
migrationBuilder.AddColumn<Guid>(
    name: "lease_id",
    table: "menu_import_jobs",
    type: "uuid",
    nullable: true);

migrationBuilder.AddColumn<DateTime>(
    name: "lease_expires_at",
    table: "menu_import_jobs",
    type: "timestamp with time zone",
    nullable: true);

migrationBuilder.AddColumn<DateTime>(
    name: "last_heartbeat_at",
    table: "menu_import_jobs",
    type: "timestamp with time zone",
    nullable: true);

migrationBuilder.AddColumn<int>(
    name: "processing_attempts",
    table: "menu_import_jobs",
    type: "integer",
    nullable: false,
    defaultValue: 0);

migrationBuilder.CreateIndex(
    name: "IX_menu_import_jobs_status_lease_expires_at",
    table: "menu_import_jobs",
    columns: new[] { "status", "lease_expires_at" });
```

The `Down` migration drops the index and these four columns.

- [ ] **Step 4: Run the entity test and verify GREEN**

Run:

```bash
dotnet test backend/RestaurantApi.Tests/RestaurantApi.Tests.csproj --filter "FullyQualifiedName~MenuImportJobEntityTests.RoundTrip_all_fields_survive_persist_and_reload"
```

Expected: pass.

- [ ] **Step 5: Write the failing duplicate-claim test**

In `backend/RestaurantApi.Tests/MenuImportBackgroundServiceTests.cs`, add a fake client that blocks both workers until both have tried to process:

```csharp
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
```

Add the test:

```csharp
[Fact]
public async Task Concurrent_service_instances_do_not_process_the_same_pending_job()
{
    var client = new BlockingTrackingPythonClient();
    await using var sp = BuildServiceProvider(client);
    using (var scope = sp.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
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
```

- [ ] **Step 6: Run the duplicate-claim test and verify RED**

Run:

```bash
dotnet test backend/RestaurantApi.Tests/RestaurantApi.Tests.csproj --filter "FullyQualifiedName~MenuImportBackgroundServiceTests.Concurrent_service_instances_do_not_process_the_same_pending_job"
```

Expected: fails because the current service can select the same pending row in two concurrent pollers.

- [ ] **Step 7: Implement atomic claiming**

Add options to `MenuImportOptions`:

```csharp
public int LeaseDurationSeconds { get; set; } = 120;
public int HeartbeatIntervalSeconds { get; set; } = 30;
public int MaxProcessingAttempts { get; set; } = 2;
```

Replace the LINQ pending-job selection in `ProcessPendingJobsAsync` with a private `ClaimJobsAsync` method that uses `FOR UPDATE SKIP LOCKED`:

```csharp
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
                updated_at = {now}
            FROM picked
            WHERE j.id = picked.id
            RETURNING j.*
            """)
        .IgnoreQueryFilters()
        .AsNoTracking()
        .ToListAsync(ct);
}
```

`ProcessPendingJobsAsync` should call `ClaimJobsAsync` with tenant resolution suppressed and process only returned rows.

- [ ] **Step 8: Run the duplicate-claim test and verify GREEN**

Run:

```bash
dotnet test backend/RestaurantApi.Tests/RestaurantApi.Tests.csproj --filter "FullyQualifiedName~MenuImportBackgroundServiceTests.Concurrent_service_instances_do_not_process_the_same_pending_job"
```

Expected: pass.

- [ ] **Step 9: Checkpoint**

Ready files: C# model, DbContext, migration/snapshot, background service, entity test, background service race test.

Suggested commit: `feat(api): add leased menu import job claiming`

---

## Task 2: C# Heartbeat, Stale Recovery, and Lease-Guarded Terminal Writes

**Files:**
- Modify: `backend/RestaurantApi/Services/MenuImportBackgroundService.cs`
- Modify: `backend/RestaurantApi.Tests/MenuImportBackgroundServiceTests.cs`

- [ ] **Step 1: Write the failing heartbeat test**

Set short heartbeat options in `BuildServiceProvider`:

```csharp
LeaseDurationSeconds = 2,
HeartbeatIntervalSeconds = 1,
MaxProcessingAttempts = 2,
```

Add this test:

```csharp
[Fact]
public async Task Active_extraction_extends_lease_with_heartbeat()
{
    var client = new SlowPythonClient(TimeSpan.FromMilliseconds(1500));
    await using var sp = BuildServiceProvider(client);
    using (var scope = sp.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
    }

    var rid = Guid.NewGuid();
    var jobId = await SeedPendingJob(sp, rid, "heartbeat.pdf");
    var svc = CreateService(sp);
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

    await svc.RunOnceForTestAsync(cts.Token);

    using var checkScope = sp.CreateScope();
    var db = checkScope.ServiceProvider.GetRequiredService<AppDbContext>();
    var job = await db.MenuImportJobs.IgnoreQueryFilters().SingleAsync(j => j.Id == jobId, cts.Token);
    job.Status.Should().Be("complete");
    job.LastHeartbeatAt.Should().NotBeNull();
    job.ProcessingAttempts.Should().Be(1);
}
```

- [ ] **Step 2: Run heartbeat test and verify RED**

Run:

```bash
dotnet test backend/RestaurantApi.Tests/RestaurantApi.Tests.csproj --filter "FullyQualifiedName~MenuImportBackgroundServiceTests.Active_extraction_extends_lease_with_heartbeat"
```

Expected: fails because no heartbeat loop exists.

- [ ] **Step 3: Implement heartbeat loop**

In `WorkOneJobAsync`, start a heartbeat task after the final pre-extraction cancellation check and before `ExtractPdfAsync`:

```csharp
using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
var heartbeatTask = RunHeartbeatAsync(job.Id, job.LeaseId!.Value, heartbeatCts.Token);
try
{
    var resultElement = await extractor.ExtractPdfAsync(payload, ct);
    ...
}
finally
{
    await heartbeatCts.CancelAsync();
    try { await heartbeatTask; }
    catch (OperationCanceledException) { }
}
```

Add:

```csharp
private async Task RunHeartbeatAsync(Guid jobId, Guid leaseId, CancellationToken ct)
{
    while (!ct.IsCancellationRequested)
    {
        await Task.Delay(TimeSpan.FromSeconds(_options.HeartbeatIntervalSeconds), ct);
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
        using var _ = tenant.SuppressResolution();

        var now = DateTime.UtcNow;
        await db.MenuImportJobs
            .IgnoreQueryFilters()
            .Where(j => j.Id == jobId && j.LeaseId == leaseId && !IsTerminalStatus(j.Status))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(j => j.LeaseExpiresAt, now.AddSeconds(_options.LeaseDurationSeconds))
                .SetProperty(j => j.LastHeartbeatAt, now)
                .SetProperty(j => j.UpdatedAt, now), ct);
    }
}
```

- [ ] **Step 4: Write stale recovery tests**

Replace `Stale_extracting_job_is_marked_failed_on_first_tick` with two narrower tests:

```csharp
[Fact]
public async Task Expired_extracting_job_with_attempts_remaining_is_reclaimed()
{
    var rid = Guid.NewGuid();
    var jobId = Guid.NewGuid();
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
            Phase = "extracting",
            StorageKey = $"{rid:N}/{jobId:N}.pdf",
            OriginalFileName = "expired.pdf",
            FileSizeBytes = 100,
            ProviderUsed = "gemini",
            LeaseId = Guid.NewGuid(),
            LeaseExpiresAt = DateTime.UtcNow.AddSeconds(-5),
            LastHeartbeatAt = DateTime.UtcNow.AddMinutes(-3),
            ProcessingAttempts = 1,
        });
        await db.SaveChangesAsync();
    }

    var svc = CreateService(_sp);
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    await svc.RunOnceForTestAsync(cts.Token);

    using var checkScope = _sp.CreateScope();
    var dbCheck = checkScope.ServiceProvider.GetRequiredService<AppDbContext>();
    var job = await dbCheck.MenuImportJobs.IgnoreQueryFilters().SingleAsync(j => j.Id == jobId, cts.Token);
    job.Status.Should().Be("complete");
    job.ProcessingAttempts.Should().Be(2);
}

[Fact]
public async Task Expired_extracting_job_with_exhausted_attempts_is_marked_failed()
{
    var rid = Guid.NewGuid();
    var jobId = Guid.NewGuid();
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
            Phase = "extracting",
            StorageKey = $"{rid:N}/{jobId:N}.pdf",
            OriginalFileName = "expired-exhausted.pdf",
            FileSizeBytes = 100,
            ProviderUsed = "gemini",
            LeaseId = Guid.NewGuid(),
            LeaseExpiresAt = DateTime.UtcNow.AddSeconds(-5),
            LastHeartbeatAt = DateTime.UtcNow.AddMinutes(-3),
            ProcessingAttempts = 2,
        });
        await db.SaveChangesAsync();
    }

    var svc = CreateService(_sp);
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    await svc.RunOnceForTestAsync(cts.Token);

    using var checkScope = _sp.CreateScope();
    var dbCheck = checkScope.ServiceProvider.GetRequiredService<AppDbContext>();
    var job = await dbCheck.MenuImportJobs.IgnoreQueryFilters().SingleAsync(j => j.Id == jobId, cts.Token);
    job.Status.Should().Be("failed");
    job.ErrorMessage.Should().Contain("stalled");
    job.LeaseId.Should().BeNull();
    job.LeaseExpiresAt.Should().BeNull();
}
```

- [ ] **Step 5: Run stale recovery tests and verify RED**

Run:

```bash
dotnet test backend/RestaurantApi.Tests/RestaurantApi.Tests.csproj --filter "FullyQualifiedName~MenuImportBackgroundServiceTests.Expired_extracting_job"
```

Expected: one or both fail because stale recovery is first-tick-only and does not use lease attempts.

- [ ] **Step 6: Implement periodic stale recovery**

Remove `_staleRecoveryRun`. Call `RecoverStaleJobsAsync(ct)` every `RunOnceForTestAsync`.

Update `RecoverStaleJobsAsync` so it only fails exhausted expired leases:

```csharp
var now = DateTime.UtcNow;
var failed = await db.MenuImportJobs
    .IgnoreQueryFilters()
    .Where(j =>
        j.Status == "extracting" &&
        j.LeaseExpiresAt != null &&
        j.LeaseExpiresAt < now &&
        j.ProcessingAttempts >= _options.MaxProcessingAttempts)
    .ExecuteUpdateAsync(setters => setters
        .SetProperty(j => j.Status, "failed")
        .SetProperty(j => j.ErrorMessage, "Extraction stalled after retry attempts.")
        .SetProperty(j => j.LeaseId, (Guid?)null)
        .SetProperty(j => j.LeaseExpiresAt, (DateTime?)null)
        .SetProperty(j => j.UpdatedAt, now), ct);
```

Do not mark expired jobs with attempts remaining failed; `ClaimJobsAsync` reclaims them.

- [ ] **Step 7: Write lost-lease terminal/delete test**

Make `FakeFileStorage` track deletes:

```csharp
public List<string> DeletedKeys { get; } = new();
public Task DeleteAsync(string storageKey, CancellationToken ct)
{
    lock (DeletedKeys) DeletedKeys.Add(storageKey);
    return Task.CompletedTask;
}
```

Register it via a variable in `BuildServiceProvider` so tests can inspect it. Add a client that steals the lease before returning:

```csharp
private sealed class LeaseStealingPythonClient : IPdfExtractor
{
    private readonly Func<Guid, Task> _steal;
    public LeaseStealingPythonClient(Func<Guid, Task> steal) => _steal = steal;

    public async Task<JsonElement> ExtractPdfAsync(PdfJobPayload payload, CancellationToken ct)
    {
        await _steal(payload.JobId);
        return JsonSerializer.SerializeToDocument(new { items = Array.Empty<object>() }).RootElement;
    }
}
```

Test:

```csharp
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
    sp = BuildServiceProvider(client, fileStorage: storage);
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
}
```

- [ ] **Step 8: Implement lease-guarded terminal writes and cleanup**

Replace direct tracked-entity terminal updates with helper methods that return `bool`.

Completion helper:

```csharp
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
        .Where(j => j.Id == jobId && j.LeaseId == leaseId && !IsTerminalStatus(j.Status))
        .ExecuteUpdateAsync(setters => setters
            .SetProperty(j => j.Status, "complete")
            .SetProperty(j => j.Phase, "finalizing")
            .SetProperty(j => j.ResultJson, resultJson)
            .SetProperty(j => j.CompletedAt, now)
            .SetProperty(j => j.UpdatedAt, now)
            .SetProperty(j => j.LeaseId, (Guid?)null)
            .SetProperty(j => j.LeaseExpiresAt, (DateTime?)null), ct);
    return updated == 1;
}
```

Failure helper follows the same `jobId && leaseId && !terminal` guard and clears lease fields.

In `finally`, call `fileStorage.DeleteAsync` only when `terminalStateWrittenByThisWorker` is true.

- [ ] **Step 9: Run C# menu import tests**

Run:

```bash
dotnet test backend/RestaurantApi.Tests/RestaurantApi.Tests.csproj --filter MenuImport
```

Expected: pass.

- [ ] **Step 10: Checkpoint**

Ready files: background service lease heartbeat/stale recovery code and updated tests.

Suggested commit: `fix(api): guard pdf import completion with job leases`

---

## Task 3: Python Provider Backpressure and Vision Extractor Reuse

**Files:**
- Modify: `backend/ai-scripts/services/pdf_menu_extraction_service.py`
- Modify: `backend/ai-scripts/routers/menu_extraction.py`
- Modify: `backend/ai-scripts/tests/test_pdf_menu_extraction_service.py`
- Modify: `backend/ai-scripts/tests/test_extract_pdf_router.py`

- [ ] **Step 1: Write the failing provider-concurrency test**

Add this helper to `backend/ai-scripts/tests/test_pdf_menu_extraction_service.py`:

```python
class DelayedCountingVisionExtractor:
    model_name = "delayed-counting"

    def __init__(self):
        self.active = 0
        self.peak = 0
        self.calls: list[int] = []
        self._lock = asyncio.Lock()

    async def extract(self, image_bytes: bytes, page_number: int) -> dict[str, Any]:
        async with self._lock:
            self.active += 1
            self.peak = max(self.peak, self.active)
        await asyncio.sleep(0.05)
        async with self._lock:
            self.active -= 1
        self.calls.append(page_number)
        return {"items": [{"name": f"Vision {page_number}", "price": 10.0, "source_index": 0}]}
```

Add:

```python
@pytest.mark.asyncio
async def test_provider_concurrency_limits_vision_calls_even_when_page_concurrency_is_higher():
    extractor = DelayedCountingVisionExtractor()
    factory_calls = 0

    def vision_factory():
        nonlocal factory_calls
        factory_calls += 1
        return extractor

    service = PdfMenuExtractionService(
        text_extractor_factory=lambda: FakeTextExtractor(canned_response={"items": []}),
        vision_extractor_factory=vision_factory,
        progress=StubProgressReporter(),
        concurrency=8,
        provider_concurrency=2,
    )

    result = await service.run(_make_request(SCANNED_3PAGE))

    assert len(result.items) == 3
    assert extractor.peak <= 2
    assert sorted(extractor.calls) == [0, 1, 2]
    assert factory_calls == 1
```

- [ ] **Step 2: Run the test and verify RED**

Run:

```bash
cd backend/ai-scripts
pytest tests/test_pdf_menu_extraction_service.py::test_provider_concurrency_limits_vision_calls_even_when_page_concurrency_is_higher -v
```

Expected: constructor error because `provider_concurrency` does not exist, or `factory_calls` equals 3.

- [ ] **Step 3: Implement process-wide provider limiter and per-job vision reuse**

In `services/pdf_menu_extraction_service.py`, add:

```python
_provider_semaphore: asyncio.Semaphore | None = None
_provider_semaphore_limit: int | None = None


def get_provider_semaphore(limit: int) -> asyncio.Semaphore:
    global _provider_semaphore, _provider_semaphore_limit
    if limit < 1:
        raise ValueError("provider_concurrency must be >= 1")
    if _provider_semaphore is None or _provider_semaphore_limit != limit:
        _provider_semaphore = asyncio.Semaphore(limit)
        _provider_semaphore_limit = limit
    return _provider_semaphore
```

Extend `PdfMenuExtractionService.__init__`:

```python
provider_concurrency: int | None = None,
```

Set:

```python
self._provider_concurrency = provider_concurrency or min(4, concurrency)
self._provider_semaphore = get_provider_semaphore(self._provider_concurrency)
self._vision_extractor: VisionExtractor | None = None
```

Add:

```python
def _get_vision_extractor(self) -> VisionExtractor:
    if self._vision_extractor is None:
        self._vision_extractor = self._vision_factory()
    return self._vision_extractor
```

Wrap provider calls:

```python
async with self._provider_semaphore:
    raw = await extractor.extract(...)
```

Use `_get_vision_extractor()` in `_run_vision_for_page`. Keep rendering outside the provider semaphore.

- [ ] **Step 4: Add text-provider limiter test**

Add a delayed fake text extractor and assert the text path also respects `provider_concurrency=1` when `text_batch_size=1` and `concurrency=3`.

```python
class DelayedCountingTextExtractor:
    model_name = "delayed-text"

    def __init__(self):
        self.active = 0
        self.peak = 0
        self._lock = asyncio.Lock()

    async def extract(self, pages):
        async with self._lock:
            self.active += 1
            self.peak = max(self.peak, self.active)
        await asyncio.sleep(0.05)
        async with self._lock:
            self.active -= 1
        return {"items": [
            {"name": f"Text {p.page_number}", "price": 11.0, "source_page": p.page_number, "source_index": 0}
            for p in pages
        ]}
```

```python
@pytest.mark.asyncio
async def test_provider_concurrency_limits_text_batches():
    extractor = DelayedCountingTextExtractor()
    service = PdfMenuExtractionService(
        text_extractor_factory=lambda: extractor,
        vision_extractor_factory=lambda: FakeVisionExtractor(canned_response={"items": []}),
        progress=StubProgressReporter(),
        concurrency=3,
        text_batch_size=1,
        provider_concurrency=1,
    )

    result = await service.run(_make_request(DIGITAL_3PAGE))

    assert len(result.items) == 3
    assert extractor.peak == 1
```

- [ ] **Step 5: Wire environment parsing in router**

In `backend/ai-scripts/routers/menu_extraction.py`, read:

```python
provider_concurrency = int(os.environ.get(
    "PDF_PROVIDER_MAX_CONCURRENCY",
    str(min(4, concurrency)),
))
```

Pass `provider_concurrency=provider_concurrency` into `PdfMenuExtractionService`.

Add or update a router factory test that monkeypatches `PDF_EXTRACT_CONCURRENCY=8` and `PDF_PROVIDER_MAX_CONCURRENCY=2`, builds the service through `get_pdf_extraction_service_factory()`, and asserts private value `_provider_concurrency == 2`.

- [ ] **Step 6: Run Python tests**

Run:

```bash
cd backend/ai-scripts
pytest tests/test_pdf_menu_extraction_service.py tests/test_extract_pdf_router.py -v
```

Expected: pass.

- [ ] **Step 7: Checkpoint**

Ready files: Python service, router env parsing, service/router tests.

Suggested commit: `fix(ai): throttle pdf provider calls and reuse vision clients`

---

## Task 4: PDF Import Benchmark Harness

**Files:**
- Create: `backend/ai-scripts/scripts/benchmark_pdf_import.py`
- Create: `backend/ai-scripts/tests/test_pdf_benchmark.py`
- Modify: `docs/menu-import-pdf-smoke.md`

- [ ] **Step 1: Write the failing benchmark smoke test**

Create `backend/ai-scripts/tests/test_pdf_benchmark.py`:

```python
import subprocess
import sys
from pathlib import Path


def test_benchmark_pdf_import_fake_provider_smoke():
    script = Path(__file__).parents[1] / "scripts" / "benchmark_pdf_import.py"
    fixture = Path(__file__).parent / "fixtures" / "sample_menus" / "scanned_3page.pdf"

    result = subprocess.run(
        [
            sys.executable,
            str(script),
            str(fixture),
            "--fake-provider",
            "--extract-concurrency",
            "3",
            "--provider-concurrency",
            "2",
            "--fake-latency-ms",
            "1",
        ],
        text=True,
        capture_output=True,
        check=False,
    )

    assert result.returncode == 0, result.stderr
    assert "pages=3" in result.stdout
    assert "items=3" in result.stdout
    assert "peak_provider_concurrency=" in result.stdout
    assert "total_seconds=" in result.stdout
```

- [ ] **Step 2: Run benchmark test and verify RED**

Run:

```bash
cd backend/ai-scripts
pytest tests/test_pdf_benchmark.py -v
```

Expected: fails because `scripts/benchmark_pdf_import.py` does not exist.

- [ ] **Step 3: Implement fake-provider benchmark script**

Create `backend/ai-scripts/scripts/benchmark_pdf_import.py`:

```python
#!/usr/bin/env python3
import argparse
import asyncio
import sys
import time
from pathlib import Path
from uuid import uuid4

ROOT = Path(__file__).resolve().parents[1]
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from models.menu_extraction import PdfJobRequest
from services.pdf_menu_extraction_service import PdfMenuExtractionService
from services.text_extractor import FakeTextExtractor


class NoopProgressReporter:
    async def send(self, event):
        return None


class BenchmarkVisionExtractor:
    model_name = "benchmark-fake-vision"

    def __init__(self, latency_ms: int):
        self.latency = latency_ms / 1000
        self.active = 0
        self.peak = 0
        self.calls = 0
        self._lock = asyncio.Lock()

    async def extract(self, image_bytes: bytes, page_number: int):
        async with self._lock:
            self.active += 1
            self.peak = max(self.peak, self.active)
        await asyncio.sleep(self.latency)
        async with self._lock:
            self.active -= 1
        self.calls += 1
        return {"items": [{"name": f"Benchmark Item {page_number}", "price": 10.0, "source_index": 0}]}


async def main_async(args):
    pdf_path = Path(args.pdf_path).resolve()
    vision = BenchmarkVisionExtractor(args.fake_latency_ms)
    service = PdfMenuExtractionService(
        text_extractor_factory=lambda: FakeTextExtractor(canned_response={"items": []}),
        vision_extractor_factory=lambda: vision,
        progress=NoopProgressReporter(),
        concurrency=args.extract_concurrency,
        provider_concurrency=args.provider_concurrency,
    )
    request = PdfJobRequest(
        job_id=uuid4(),
        restaurant_id=uuid4(),
        storage_path=str(pdf_path),
        progress_callback_url="http://localhost/api/internal/menu-imports/benchmark/progress",
        hmac_secret="benchmark-secret",
        provider="fake",
    )

    started = time.perf_counter()
    result = await service.run(request)
    elapsed = time.perf_counter() - started
    pages = len({item.source_page for item in result.items} | {page.source_page for page in result.failed_pages})
    print(
        f"pages={pages} items={len(result.items)} failed_pages={len(result.failed_pages)} "
        f"vision_calls={vision.calls} peak_provider_concurrency={vision.peak} "
        f"total_seconds={elapsed:.3f}"
    )


def parse_args():
    parser = argparse.ArgumentParser(description="Benchmark PDF menu import extraction.")
    parser.add_argument("pdf_path")
    parser.add_argument("--fake-provider", action="store_true", default=True)
    parser.add_argument("--extract-concurrency", type=int, default=8)
    parser.add_argument("--provider-concurrency", type=int, default=4)
    parser.add_argument("--fake-latency-ms", type=int, default=25)
    return parser.parse_args()


def main():
    asyncio.run(main_async(parse_args()))


if __name__ == "__main__":
    main()
```

- [ ] **Step 4: Run benchmark test and verify GREEN**

Run:

```bash
cd backend/ai-scripts
pytest tests/test_pdf_benchmark.py -v
```

Expected: pass.

- [ ] **Step 5: Document benchmark command**

Append to `docs/menu-import-pdf-smoke.md`:

````markdown
## Large scanned PDF benchmark

Run the fake-provider benchmark without committing large PDFs:

```bash
cd backend/ai-scripts
python scripts/benchmark_pdf_import.py /path/to/local/scanned-menu.pdf \
  --fake-provider \
  --extract-concurrency 8 \
  --provider-concurrency 4 \
  --fake-latency-ms 25
```

The output reports page count, item count, failed pages, peak provider concurrency, and total wall time. Use a local 75 MB / 80 page scanned PDF for sprint measurements.
````

- [ ] **Step 6: Checkpoint**

Ready files: benchmark script, smoke test, docs note.

Suggested commit: `chore(ai): add pdf import benchmark harness`

---

## Task 5: Final Verification and Review

**Files:**
- Review all changed files from Tasks 1-4.

- [ ] **Step 1: Run targeted C# verification**

Run:

```bash
dotnet test backend/RestaurantApi.Tests/RestaurantApi.Tests.csproj --filter MenuImport
```

Expected: pass. If `dotnet` is unavailable, record the exact shell error.

- [ ] **Step 2: Run targeted Python verification**

Run:

```bash
cd backend/ai-scripts
pytest tests/test_pdf_menu_extraction_service.py tests/test_extract_pdf_router.py tests/test_pdf_benchmark.py -v
```

Expected: pass. If `pytest` is unavailable, record the exact shell error.

- [ ] **Step 3: Run static checks**

Run:

```bash
python3 -m py_compile backend/ai-scripts/services/pdf_menu_extraction_service.py backend/ai-scripts/routers/menu_extraction.py backend/ai-scripts/scripts/benchmark_pdf_import.py
docker compose config --quiet
git diff --check
```

Expected: all pass. If `docker` is unavailable, record the exact shell error.

- [ ] **Step 4: Review implementation against spec**

Confirm:

- C# uses atomic PostgreSQL job claiming.
- Terminal job writes are lease-guarded.
- File deletion happens only after this worker writes terminal state.
- Python provider limiter is separate from page concurrency.
- Vision extractor factory is called once per PDF job.
- Benchmark can run against a local large PDF without adding one to git.

- [ ] **Step 5: Checkpoint**

Ready files: all sprint changes.

Suggested commit: `fix: harden pdf import operational pipeline`
