# PDF Menu Import — Manual Smoke-Test Runbook

## Pre-flight

Set the following environment variables before starting any service.

```powershell
# .NET API
$env:MenuImport__HmacSecret    = "replace-with-32-char-min-secret-padX"
$env:MenuImport__CallbackBaseUrl = "http://localhost:5000"
$env:MenuImport__StorageRoot   = "C:\Temp\menu-import-uploads"   # writable path
$env:Jwt__Key                  = "replace-with-32-char-min-jwt-key-padX"

# Python AI service — choose ONE provider
$env:MENU_IMPORT_STORAGE_ROOT  = "C:\Temp\menu-import-uploads"   # same path the API uses locally
$env:MENU_IMPORT_CALLBACK_HOSTS = "localhost,127.0.0.1"
$env:GEMINI_API_KEY            = "your-gemini-api-key"
# or
$env:ANTHROPIC_API_KEY         = "your-anthropic-api-key"
```

> The `MenuImport__HmacSecret` must be at least 32 characters. The `Jwt__Key` must be at least 32 characters.
> When running with Docker Compose, the API and AI containers share the named `menu_imports` volume at `/menu-imports`, and the callback URL is `http://backend-api:5000`.

---

## Run the Three Services

Open three separate terminals.

### 1 — .NET REST API

```powershell
dotnet run --project backend/RestaurantApi
# Listens on https://localhost:5001 / http://localhost:5000
```

### 2 — Python AI microservice

```powershell
cd backend/ai-scripts
uvicorn main:app --reload --port 8000
# Health check: GET http://localhost:8000/health
```

### 3 — Vue Admin Frontend

```powershell
cd frontend-admin
npm run dev
# Vite dev server typically starts on http://localhost:5173
```

---

## Happy Path Manual Test

1. Navigate to `http://localhost:5173` and log in as a restaurant admin.
2. Go to **Menu** → **Import from PDF** (or navigate to the import page directly).
3. Drag a real digital PDF (e.g., a 2–5 page digital menu) onto the upload zone.
4. Expected milestones (watch the progress bar and status text):
   - **Parsing** — "Reading PDF pages…" (phase: parsing)
   - **Classifying** — "Analysing page types…" (phase: classifying)
   - **Extracting** — percentage counter advances per page (phase: extracting)
   - **Complete** — success toast fires; UI transitions to the review step.
5. On the review step, confirm that extracted items appear in the list with names, prices, and category hints.

---

## Two-Tenant Test

1. Open two separate browser profiles (or incognito windows).
2. Log in as **Admin A** (restaurant R-A) in one window, **Admin B** (restaurant R-B) in the other.
3. Both upload a PDF simultaneously.
4. Observe:
   - Admin A's progress bar and toast are independent of Admin B's.
   - Admin A **cannot** access Admin B's job URL (`/api/restaurants/{R-B}/admin/menu/imports/{jobId}` returns 404 for Admin A's token).
   - SignalR events are isolated: only the respective admin sees their own job's progress events.

---

## Negative Tests

| Scenario | How to Trigger | Expected Result |
|----------|---------------|-----------------|
| 0-byte PDF | Upload an empty file renamed `.pdf` | 400 Bad Request — "Fișierul PDF este obligatoriu." |
| Non-PDF file | Upload a PNG renamed `.pdf` | 400 Bad Request — MIME check fails |
| Bad magic bytes | Upload a `.txt` file with `application/pdf` content-type | 400 Bad Request — "Fișierul nu este un PDF valid." |
| Password-protected PDF | Upload an encrypted PDF | The Python service will return 0 pages; job transitions to `failed` with "Unreadable PDF" |
| Corrupted PDF | Upload a truncated PDF binary | Same as above — `failed` |
| Oversized PDF (> 110 MB) | `POST` a > 110 MB file | 413 Request Entity Too Large (enforced by `[RequestSizeLimit]` before controller code) |
| Mid-job DELETE | Upload, then immediately `DELETE /api/restaurants/{R}/admin/menu/imports/{jobId}` | 200 on DELETE; subsequent GET returns `status: "cancelled"`; `result` is null |

---

## Cleanup Verification

After a job completes (status `complete` or `failed`), the uploaded PDF must no longer be present in the storage root:

```powershell
# After a successful import:
dir $env:MenuImport__StorageRoot
# The job's PDF file (e.g., {restaurantId}/{jobId}.pdf) should be absent.
```

The background service calls `IFileStorage.DeleteAsync` after the worker writes a terminal job state. Verify the directory is empty after all jobs finish.

If a file remains, it indicates the delete path likely threw an exception — check the `MenuImportBackgroundService` warning logs.

---

## Large scanned PDF benchmark

Run the fake-provider benchmark without committing large PDFs:

```powershell
cd backend/ai-scripts
python scripts/benchmark_pdf_import.py /path/to/local/scanned-menu.pdf `
  --fake-provider `
  --extract-concurrency 8 `
  --provider-concurrency 4 `
  --render-scale 1.5 `
  --render-quality 85 `
  --fake-latency-ms 25 `
  --repeat 3
```

The output reports one line per run with page count, item count, failed pages, peak provider concurrency, render scale/quality, rendered JPEG bytes, average rendered bytes per page, parse/classify/render/provider timings, measured timing total, and wall time. Provider timings are cumulative across concurrent provider calls, so they can exceed wall time when concurrency is greater than one. Use a local 75 MB / 80 page scanned PDF for sprint measurements.

For scanned PDFs, compare `--render-scale` and `--render-quality` values before changing production defaults. Lower values reduce rendered payload size and provider upload time, but they can also reduce OCR quality.

Large PDF parsing and page rendering are offloaded from the Python event loop. Rendering still uses one PDFium worker per job, so page rasterization stays serial while progress callbacks and other requests can keep moving.

Image-dominant pages skip pdfplumber text extraction and route straight to the vision path. This removes wasted parse work for photo/scanned PDFs while preserving text extraction for digital pages with small embedded images.

Production uses the matching `PDF_RENDER_SCALE` and `PDF_RENDER_JPEG_QUALITY` environment variables. Keep `PDF_EXTRACT_CONCURRENCY`, `PDF_PROVIDER_MAX_CONCURRENCY`, `PDF_TEXT_BATCH_SIZE`, `PDF_RENDER_SCALE`, and `PDF_RENDER_JPEG_QUALITY` positive and finite; invalid values fail fast instead of letting a large import hang.

To benchmark real providers, set the provider API key and opt in explicitly:

```powershell
cd backend/ai-scripts
$env:GEMINI_API_KEY = "your-gemini-api-key"
python scripts/benchmark_pdf_import.py /path/to/local/scanned-menu.pdf `
  --real-provider `
  --provider gemini `
  --extract-concurrency 8 `
  --provider-concurrency 2 `
  --render-scale 1.5 `
  --render-quality 85 `
  --repeat 1
```

Use `--provider claude` with `ANTHROPIC_API_KEY` for Claude. Keep fake-provider runs for repeatable local smoke checks; use real-provider runs sparingly because every scanned page can call the provider.

---

## Quick API Smoke via curl

```powershell
# 1. Get a JWT (replace with real login credentials)
$token = (Invoke-RestMethod -Method Post `
  -Uri "http://localhost:5000/api/auth/login" `
  -ContentType "application/json" `
  -Body '{"email":"admin@example.com","password":"YourPassword1!"}').token

# 2. Upload a PDF
$jobId = (Invoke-RestMethod -Method Post `
  -Uri "http://localhost:5000/api/restaurants/{RESTAURANT_ID}/admin/menu/imports/pdf" `
  -Headers @{Authorization="Bearer $token"} `
  -Form @{pdf=Get-Item "path\to\menu.pdf"}).jobId

Write-Host "Job ID: $jobId"

# 3. Poll status
Invoke-RestMethod `
  -Uri "http://localhost:5000/api/restaurants/{RESTAURANT_ID}/admin/menu/imports/$jobId" `
  -Headers @{Authorization="Bearer $token"} | ConvertTo-Json -Depth 5
```
