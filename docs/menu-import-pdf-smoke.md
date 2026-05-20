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
$env:GEMINI_API_KEY            = "your-gemini-api-key"
# or
$env:ANTHROPIC_API_KEY         = "your-anthropic-api-key"
```

> The `MenuImport__HmacSecret` must be at least 32 characters. The `Jwt__Key` must be at least 32 characters.

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

The background service calls `IFileStorage.DeleteAsync` in its `finally` block for every job regardless of success or failure. Verify the directory is empty after all jobs finish.

If a file remains, it indicates the delete path threw an exception that was silently swallowed — check the `MenuImportBackgroundService` warning logs.

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
