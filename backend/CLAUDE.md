# CLAUDE.md — backend

Two services live here and talk over HTTP: the .NET API (`RestaurantApi/`) and the Python AI microservice (`ai-scripts/`). Tests for the API are in `RestaurantApi.Tests/`.

> `BACKEND_DEV_GUIDE.md`, `beny-backend-guide.md`, `tudor-backend-guide.md`, and `ai-scripts/PLAN.md` predate major refactors (PLAN.md describes a sklearn design that no longer exists). Prefer this file and the code.

## RestaurantApi (.NET 8 / ASP.NET Core)

Attribute-routed MVC controllers — no minimal APIs, no MediatR, **no repository layer**: controllers and `Services/` use `AppDbContext` directly. All DI lives in `Program.cs` (top-level statements; it ends with `public partial class Program;` — required by the test factory, don't remove).

### Commands

| Task | Command |
|---|---|
| Start Postgres | `docker compose up -d postgres` (host port **5433**) |
| Run API | `dotnet run --project backend/RestaurantApi` → http://localhost:5000, Swagger at `/swagger` (Dev only) |
| All tests | `dotnet test backend/RestaurantApi.Tests` — **Docker must be running** (Testcontainers) |
| One test class | `dotnet test backend/RestaurantApi.Tests --filter "FullyQualifiedName~StockServiceTests"` |
| One test method | `dotnet test backend/RestaurantApi.Tests --filter "Name=MethodName"` |
| Add migration | `dotnet ef migrations add <Name> --project backend/RestaurantApi` |
| Apply migrations | `dotnet ef database update --project backend/RestaurantApi` |

Migrations + seeding run automatically at startup **in Development only** (prod deploys must apply migrations explicitly). Dev seed login: `supporter@demo.test` / `Demo1234!` (org "Demo Chain", 18 Romanian products, 4 tables with stable QR GUIDs — `Data/SeedData.cs`).

Startup **fail-fasts**: `Jwt:Key` < 32 bytes and (outside Testing) `MenuImport:HmacSecret` < 32 chars kill the app. Config env-var override convention: `Section__Key` (double underscore). Kestrel body limit is raised to 110 MB for PDF uploads.

### Multi-tenancy — the core invariant

Everything is scoped to a restaurant. `Tenancy/TenantResolutionMiddleware` (runs after auth) resolves the tenant from the `restaurantId` route value (staff) or `qrToken` (customers), verifies membership, and sets `ICurrentTenant.RestaurantId`. `Data/AppDbContext.cs` applies a **global query filter** (`RestaurantId == tenant`) to every `ITenantScoped` entity and auto-stamps `RestaurantId` on insert.

Checklist for any new tenant-owned entity: implement `Models/ITenantScoped.cs` → add the `HasQueryFilter` + snake_case mapping + `restaurant_id` index in `AppDbContext` → migration. Note the filter guards *reads*; nothing stops changing `RestaurantId` on a tracked entity — never write code that does.

Authorization is **not** ASP.NET role policies. Controllers use bare `[Authorize]`; the real gate is the membership model (`Models/Membership.cs`: `OrgOwner` / `RestaurantAdmin` / `Waiter`, status `active|pending_approval|rejected`) enforced by the middleware + `Tenancy/TenantAccess.cs`. Waiters are path-restricted to order endpoints. Customer endpoints are anonymous, scoped only by QR token. Background/hosted services and internal callbacks run without a tenant: use `ICurrentTenant.SuppressResolution()` + `IgnoreQueryFilters()` as the existing ones do.

### Database

PostgreSQL via Npgsql. Tables/columns are **snake_case, explicitly mapped** in `AppDbContext.OnModelCreating` (~530 lines — all Fluent config in one place; follow the local style). `jsonb` for `MenuImportJob.ResultJson`. Money is `decimal(10,2)`; stock quantities `decimal(12,3)`. Connection string key: `ConnectionStrings:Default` (dev value in `appsettings.Development.json`, port 5433).

### Order lifecycle & stock engine

Order `Status` is a **free-form string** (no enum — keep the exact literals):

`draft` → submit (`CustomerController`: sets `pending_dispatch`, computes total, assigns per-restaurant `OrderNumber`, opens a 2-minute flash-deal window) → `FlashDealService.DispatchToKitchenAsync` sets `submitted` and **decrements stock here, not at submit** → `preparing` → `ready` (stamps `ReadyAt`) → `completed` (archived + soft-deleted by `OrderFinalizationService` after a **45s grace**, 5s poll). Side paths: `cancelled` (restores stock) and `merged` (party bill merge). The kitchen kanban shows `submitted|preparing|ready`; `pending_dispatch` is intentionally invisible.

`Services/StockService.cs` (`IStockService`): decrement is **idempotent** (`Order.StockDecremented` flag) and **atomic** (`ExecuteUpdateAsync` `quantity = quantity - delta` in a transaction; transient negatives are allowed by design). `StockTrackingMode` per product: `Untracked` (always available), `Recipe` (consumes `RecipeItem` ingredients per portion), `Simple` (product is its own stock row via `Ingredient.LinkedProductId`, unique). A product is out-of-stock when any recipe ingredient can't cover one portion (or a Simple product has < 1). Availability changes broadcast `MenuItemAvailabilityChanged` to the customer menu group; `MenuController` merges out-of-stock state into menu reads. Admin CRUD: `Controllers/InventoryController.cs` (`.../admin/inventory`).

### SignalR

Two hubs (mapped in `Program.cs`): `Hubs/CustomerHub.cs` → `/hubs/customer` (**anonymous**; `JoinTableGroup`, `JoinRestaurantMenuGroup`) and `Hubs/StaffHub.cs` → `/hubs/staff` (`[Authorize]`; `JoinRoleGroup`/`LeaveRoleGroup`, only roles `admin`/`WaitStaff`, membership-checked via `ITenantAccess`). Group names come from `Hubs/TenantGroups.cs`: `r:{rid}:t:{tableId}`, `r:{rid}:{role}`, `r:{rid}:menu` — always build names through it. JWT for hubs is read from the `access_token` query string.

Server→client events (PascalCase names, camelCase JSON payloads — both SPAs depend on this): `OrderStatusUpdated`, `OrderSubmitted`, `OrderFinalized`, `MenuItemAvailabilityChanged`, `FlashDealAvailable/Claimed/Expired`, `PartyMemberJoined/Left/Updated`, `PartyMerged`, `HelpRequested`, `HelpAcknowledged`, `WaiterOnTheWay`, `MenuImportProgress/Completed/Failed`. The customer SPA implements SignalR by hand — renaming an event or changing payload casing breaks it silently.

### Testing conventions (`RestaurantApi.Tests/`)

xUnit + FluentAssertions. Integration tests boot the real app via `Helpers/TestWebApplicationFactory.cs` (`UseEnvironment("Testing")`) against a **real Postgres from Testcontainers** (`postgres:16-alpine`, one container per test class via `IAsyncLifetime`) — hence Docker is required. The Python AI service is faked with **WireMock.Net**. Reusable fakes in `Helpers/`: `RecordingEmailSender`, `FailingEmailSender`, `FakePdfExtractor`, `FakeHttpMessageHandler`, plus auth/menu-import factories. Follow these patterns rather than mocking `AppDbContext`.

### Email, storage, internal callbacks

- Email: `Services/Email/IEmailSender.cs` — `SmtpEmailSender` (MailKit; dev → mailpit, UI http://localhost:8025) or `ResendEmailSender` (prod; Railway blocks SMTP ports — see `docs/email-deliverability-setup.md`). Selected by `Email:Provider`.
- Files: user uploads (branding/avatars/menu-items) under the `Storage/UploadPaths.cs` root — default `wwwroot/uploads`, overridable via `Uploads__Root` (prod MUST point it at a Railway volume or every redeploy wipes them; see `docs/railway-persistent-uploads.md`); menu-import staging via `Storage/LocalDiskFileStorage.cs` under `MenuImport:StorageRoot` (compose volume `menu_imports`, shared with ai-service).
- Python→.NET progress callback: `POST api/internal/menu-imports/{jobId}/progress` is `[AllowAnonymous]` but HMAC-SHA256-validated (`Security/HmacRequestValidator.cs`, header `X-Hmac-Signature`, secret `MenuImport:HmacSecret`); Python signs in `ai-scripts/services/progress_reporter.py`.

## ai-scripts (Python 3.11 / FastAPI)

A **standalone HTTP service** (never invoked as a subprocess). The .NET side reaches it via three HttpClients (10s for recommendations/similarity, 6 min for photo extraction, 30 min for PDF import); base URL `AiService:BaseUrl` (`http://localhost:8000` dev, `http://ai-service:8000` in compose).

| Task | Command |
|---|---|
| Run | `cd backend/ai-scripts && uvicorn main:app --reload --port 8000` |
| All tests | `pytest` (from `backend/ai-scripts`; `pytest.ini` sets `asyncio_mode=auto`) |
| One test | `pytest tests/test_menu_organizer_fallback.py::test_name` |
| Setup | copy `.env.example` → `.env`, set `GEMINI_API_KEY` (and/or `ANTHROPIC_API_KEY`) |

Endpoints: `POST /recommend`, `POST /similarity/auto-generate`, `POST /directives/parse`, `POST /recommendation-directives/parse`, `POST /menu/extract` (photos), `POST /menu/extract-pdf` (async; reports progress back to .NET via the HMAC callback), `GET /health`. Pydantic **v2** models in `models/`; engine logic in `services/`.

**Providers are pluggable, Gemini is default, Claude the alternative**: selection via `AI_VISION_PROVIDER`, `AI_TEXT_PROVIDER`, `AI_ORGANIZER_PROVIDER` (defaults to `AI_TEXT_PROVIDER`; value `none` forces the hint-only fallback); per-role model overrides `GEMINI_*_MODEL` / `ANTHROPIC_*_MODEL`. Defaults: `gemini-3.1-flash-lite` (vision/text/photos — a deliberate **cost** decision, commit b47d798; don't bump to Pro-class models casually) and `claude-sonnet-4-6` (organizer via tool-calling). A missing API key degrades gracefully (organizer → hint fallback, recommendations → empty) rather than erroring.

PDF pipeline specifics:
- Large menus are organized in **chunks** (`PDF_ORGANIZER_CHUNK_SIZE`, default 40, merged by `merge_organizer_results` in `services/menu_organizer.py`) because single-call organizing silently dropped 50–77% of items on ~300-item menus. The admin frontend additionally re-buckets any dropped item into a "Necategorizate" category — zero product loss is an invariant to preserve.
- Tuning env vars (`PDF_RENDER_SCALE`, `PDF_VISION_BATCH_SIZE`, `PDF_EXTRACT_CONCURRENCY`, `PDF_PROVIDER_MAX_CONCURRENCY`, `PDF_PROVIDER_TIMEOUT_SECONDS`, …) and the manual smoke procedure live in `docs/menu-import-pdf-smoke.md`.
- Job processing on the .NET side is **lease-based** (`MenuImportJob.LeaseId/LeaseExpiresAt/LastHeartbeatAt`, `FOR UPDATE SKIP LOCKED`) so multiple API instances never double-process; keep heartbeat semantics intact when touching `MenuImportBackgroundService`.
- A useful real-world test fixture: `C:\Users\Tudor\Desktop\vibedine example photos\Meniu-Romana-Supporter-Aprilie.pdf` — 78 pages (content only ~1–24), **no text layer** (all pages take the vision path), decorative-ingredient styling that stresses photo attribution.
