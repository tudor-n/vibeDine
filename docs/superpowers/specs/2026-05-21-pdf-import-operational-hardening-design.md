# PDF Import Operational Hardening Design

Date: 2026-05-21
Branch: feature/better-pdf-reading

## Purpose

The better PDF reading branch now handles large uploads and async import flow, but the next sprint needs to harden production behavior for scanned/photo PDFs. A 75 MB, 80 page scanned PDF is slow because every page must be rendered and sent through the vision provider. The current defaults can multiply provider calls across API jobs and Python page tasks, and the service also constructs a new vision provider client for each page.

This sprint keeps the current C# API plus Python extractor architecture. It fixes the highest risk operational issues without replacing the long API-to-Python extraction request with a durable Python job queue.

## Goals

- Prevent two API instances from processing the same menu import job.
- Keep active jobs leased with heartbeats so stalled jobs can be recovered safely.
- Limit provider calls separately from page work scheduling.
- Reuse provider clients within a PDF job instead of constructing a vision client per page.
- Add a benchmark harness for large scanned PDFs without committing large fixture files.
- Keep changes covered by tests written before implementation.

## Non-Goals

- Do not replace `/menu/extract-pdf` with a Python-side durable job queue in this sprint.
- Do not commit a 75 MB test fixture.
- Do not change the public menu import API shape unless needed for observability.
- Do not tune OCR/model prompts beyond what is necessary for concurrency and benchmark work.

## Approach Options Considered

### Option 1: Operational hardening

Add database leasing and heartbeat behavior in C#, add provider backpressure and provider reuse in Python, and add a benchmark harness. This is the selected approach because it addresses duplicate processing and scanned-PDF lag without a full architecture rewrite.

### Option 2: Queue-first rewrite

Move extraction into a durable Python job system and have C# submit and poll instead of holding a long HTTP request. This is cleaner long term, but it changes cancellation, callbacks, deployment, and failure semantics too much for this sprint.

### Option 3: Performance-only pass

Tune only the Python scanned-PDF path. This is faster to land, but leaves the multi-instance duplicate-processing bug and stale recovery behavior unresolved.

## C# Job Leasing

### Data Model

Add lease metadata to `MenuImportJob`:

- `LeaseId`: nullable `Guid`, unique token for the worker that owns the current attempt.
- `LeaseExpiresAt`: nullable UTC timestamp.
- `LastHeartbeatAt`: nullable UTC timestamp.
- `ProcessingAttempts`: integer count incremented each time a job is claimed.

Add `MenuImportOptions` values:

- `LeaseDurationSeconds`, default 120.
- `HeartbeatIntervalSeconds`, default 30.
- `MaxProcessingAttempts`, default 2.

### Claiming Jobs

`MenuImportBackgroundService` should stop selecting pending jobs with a plain LINQ query. Instead, it should atomically claim jobs with row locking:

- Eligible jobs are `pending`, or `extracting` jobs whose `lease_expires_at` is older than the current UTC time.
- Jobs whose `processing_attempts` have reached `MaxProcessingAttempts` are not reclaimed; they are marked `failed` by stale recovery.
- Claimed jobs are ordered by `created_at`.
- The claim operation sets `status = 'extracting'`, `lease_id`, `lease_expires_at`, `last_heartbeat_at`, increments `processing_attempts`, and updates `updated_at`.
- The claim operation must run with tenant filtering suppressed.
- PostgreSQL `FOR UPDATE SKIP LOCKED` is preferred so multiple API instances can poll concurrently without selecting the same row.

The service should process only jobs returned by the claim operation.

### Heartbeats

While the C# worker is waiting on the Python extraction request, a background heartbeat loop should periodically extend the lease:

- Heartbeat updates only the row whose `id` and `lease_id` match the current worker.
- Heartbeat sets `lease_expires_at = now + LeaseDurationSeconds`, `last_heartbeat_at = now`, and `updated_at = now`.
- If the heartbeat update affects zero rows, the worker has lost ownership and should stop writing terminal state.

### Terminal Updates and Cleanup

Completion and failure writes must be guarded by `lease_id`:

- A worker may set `complete` only if the job is still non-terminal and its lease matches.
- A worker may set `failed` only if the job is still non-terminal and its lease matches.
- Terminal updates clear lease fields.
- Uploaded file cleanup should happen only after this worker successfully transitions the job to a terminal state. A worker that lost its lease must not delete the PDF because a reclaimed attempt may still need it.

### Stale Recovery

Stale recovery should run periodically, not only on the first service tick:

- Expired extracting jobs with attempts remaining are made claimable by the claim query.
- Expired extracting jobs with attempts exhausted are marked `failed` with a clear stalled-extraction message and lease fields cleared.
- Active non-expired leases are not touched.

## Python Provider Backpressure

### Separate Provider Limit

Keep `PDF_EXTRACT_CONCURRENCY` for page/work-unit scheduling. Add `PDF_PROVIDER_MAX_CONCURRENCY` for outbound provider calls:

- Default to `min(4, PDF_EXTRACT_CONCURRENCY)` when not set.
- Apply the provider limit to vision and text provider calls.
- The limiter is process-wide so multiple simultaneous PDF requests in one Python process share the same provider budget.
- Rendering remains controlled by existing page/work concurrency.

This prevents the worst fan-out pattern: `MenuImport:MaxConcurrentJobs * PDF_EXTRACT_CONCURRENCY` provider calls per Python process, plus retries.

### Provider Client Reuse

Reuse one vision extractor instance per PDF job:

- The first page that needs vision lazily creates the extractor.
- All vision pages for that PDF job share the same extractor.
- The shared extractor is protected only by the provider semaphore around outbound calls; the provider client libraries used here are async clients intended to be reused.
- Tests should assert that scanned PDFs still make one extract call per page, but the vision factory is invoked once per job.

Text extraction may continue to use the existing text factory per batch unless tests show a concrete reuse issue. It still goes through the provider semaphore.

## Benchmark Harness

Add a script under `backend/ai-scripts/scripts/benchmark_pdf_import.py`.

The script should:

- Accept a local PDF path.
- Default to fake providers so it can run without API keys.
- Optionally allow real providers through an explicit flag.
- Allow concurrency inputs: `--extract-concurrency`, `--provider-concurrency`, fake latency, and repeat count.
- Print page count, item count, failed page count, total wall time, peak provider concurrency, and coarse timings for parse, classify, render, text provider, and vision provider work.
- Avoid committing large input PDFs.

Implementation can add lightweight optional metrics hooks to `PdfMenuExtractionService` and `_LazyPageRenderer` if that keeps the benchmark accurate without changing runtime behavior.

## Tests

### C# Tests

Write failing tests before production code for:

- Two service instances polling concurrently do not process the same pending job.
- A claimed job records lease metadata and heartbeat extends the lease while extraction is running.
- Expired extracting jobs with attempts remaining can be reclaimed.
- Expired extracting jobs with exhausted attempts are marked failed.
- A worker that lost its lease does not write completion or delete the uploaded PDF.
- Entity schema round-trip includes the new lease fields.

### Python Tests

Write failing tests before production code for:

- `PDF_PROVIDER_MAX_CONCURRENCY` limits concurrent fake provider calls even when page concurrency is higher.
- Scanned PDFs reuse one vision extractor instance per job while still extracting each image page.
- Text fallback/reclassification vision calls share the same provider limiter.
- The benchmark script runs in fake-provider mode against an existing fixture and prints the expected metrics.

## Verification

Run targeted tests first, then broader checks:

- `pytest backend/ai-scripts/tests/test_pdf_menu_extraction_service.py`
- `pytest backend/ai-scripts/tests/test_extract_pdf_router.py`
- `pytest backend/ai-scripts/tests/test_pdf_benchmark.py` or the benchmark script smoke test path chosen during implementation
- `dotnet test backend/RestaurantApi.Tests/RestaurantApi.Tests.csproj --filter MenuImport`
- `python3 -m py_compile backend/ai-scripts/services/pdf_menu_extraction_service.py backend/ai-scripts/routers/menu_extraction.py`
- `docker compose config --quiet`
- `git diff --check`

If local dependencies such as `dotnet`, `pytest`, or `docker` are unavailable, record the exact command and failure so the missing verification is explicit.

## Implementation Slices

Use subagents after the implementation plan is written:

- C# worker: model, migration, claim/heartbeat/stale recovery behavior, and C# tests.
- Python worker: provider limiter, vision extractor reuse, router environment parsing, and Python service tests.
- Benchmark worker: metrics hooks if needed, benchmark script, smoke test, and documentation note.

The final integration pass reviews cross-slice behavior, runs available verification, and prepares a concise sprint summary.
