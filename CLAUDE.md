# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

Multi-tenant restaurant SaaS ("Clarifi" / VibeDine): diners order via a per-table QR code; staff run the restaurant from an admin console; an AI layer digitizes printed menus (photos/PDF), generates recommendations, and drives flash deals. Romanian-first product: the admin UI is Romanian-only, the customer UI is RO/EN.

Two internal control planes sit alongside the tenant-facing product: **vibeDine HQ** (provisioning orgs/restaurants) and **Ops** (infrastructure). Staff also get **Waiter Mode**, a floor-facing surface inside the admin app.

Detailed area guides (auto-loaded when working in those folders): `backend/CLAUDE.md`, `frontend-admin/CLAUDE.md`, `frontend-customer/CLAUDE.md`. ⚠️ **No area guide covers `PlatformApi`, `OpsApi`, `frontend-platform`, or `frontend-ops`** — `backend/CLAUDE.md` describes `RestaurantApi` only, so don't apply its rules to the control planes; use the `docs/` files listed at the bottom.

## Services & ports

| Path | Tech | Port | Role |
|---|---|---|---|
| `backend/RestaurantApi` | .NET 8, EF Core + PostgreSQL, SignalR | **5000** host-run / **5005** via docker | REST API + hubs; multi-tenant core |
| `backend/PlatformApi` | .NET 8, EF Core | 5006 | "vibeDine HQ" control plane — provisions orgs/restaurants/admins **across** tenants |
| `backend/OpsApi` | .NET 8, EF Core | 5007 | infrastructure/ops console (connections, audit log, log archive) |
| `backend/ai-scripts` | Python 3.11, FastAPI | 8000 | AI service (Gemini default / Claude alt): menu extraction, organizer, recommendations |
| `frontend-admin` | Vue 3 + TS, Vite, Pinia | 3001 | staff console (JWT); also hosts **Waiter Mode** — `/waiter*` routes render in `WaiterShell.vue`, not the admin chrome |
| `frontend-customer` | Vue 3 + TS, Vite, Pinia | 3000 | diner QR SPA (anonymous, qrToken-scoped) |
| `frontend-platform` | Vue 3 + TS, Vite | 3002 | HQ control-plane UI (English-only, no i18n) |
| `frontend-ops` | Vue 3 + TS, Vite | 3003 | ops/infra UI (English-only, no i18n) |
| `shared/` | hand-maintained TS contracts | — | imported by the customer + admin frontends as `@shared` |
| postgres (docker) | Postgres 16 | **5433** | database — **all three .NET APIs share the single `restaurant` DB** |
| mailpit (docker) | — | 1025 / UI **8025** | dev email capture (confirmation mails land here) |

⚠️ **The three .NET APIs are not independent services** — they share one Postgres database, and `PlatformApi` reads/writes the *same* `organizations` / `restaurants` / `memberships` / `AspNetUsers` tables that `RestaurantApi` owns. See invariant #1 before touching either project.

## Running

- **Full stack**: `docker compose up` (root compose file wires everything, network `restaurant-net`).
- **Typical split dev**: `docker compose up -d postgres`, then per service: `dotnet run --project backend/RestaurantApi` (auto-migrates + seeds in Development; Swagger at `/swagger`), `cd backend/ai-scripts && uvicorn main:app --reload --port 8000`, `npm run dev` in each frontend.
- ⚠️ **Port mismatch trap**: the customer and admin frontends default `VITE_API_URL`/`VITE_WS_URL` to `localhost:5005` — the *docker* mapping. A host-run API listens on **5000**, so either run the backend in docker or set the env vars.
- The control planes are optional for ordinary feature work — `docker compose up` starts all four, but nothing in the diner/staff flow needs `platform-api`, `ops-api`, `frontend-platform`, or `frontend-ops` running.
- Dev seed login: `supporter@demo.test` / `Demo1234!`. Customer flow needs a table QR token — seeded tables have stable GUIDs (see `backend/RestaurantApi/Data/SeedData.cs`); open `http://localhost:3000/<qrToken>`.

## Commands (per area — details in area guides)

| Area | Test | Type-check / lint | Notes |
|---|---|---|---|
| .NET API | `dotnet test backend/RestaurantApi.Tests` (one class: `--filter "FullyQualifiedName~X"`) | build warnings | **Requires Docker** (Testcontainers Postgres); Python service is WireMock-faked |
| OpsApi | `dotnet test backend/OpsApi.Tests` | build warnings | small suite; `PlatformApi` has **no test project at all** |
| ai-scripts | `cd backend/ai-scripts && pytest` (one: `pytest tests/f.py::t`) | — | `asyncio_mode=auto` |
| frontend-admin | `npm test` (one file: `npx vitest run <path>`) | `npm run type-check` (no lint) | vitest + MSW; covers Waiter Mode specs |
| frontend-customer | **no tests exist** (`npm test` is a stub) | `npm run type-check`, `npm run lint` | oxlint + eslint |
| frontend-platform / frontend-ops | **no tests** | `npm run type-check` | internal consoles |
| EF migration | `dotnet ef migrations add <Name> --project backend/RestaurantApi` | | prod never auto-migrates; **`PlatformApi`/`OpsApi` use raw-SQL bootstrap, not migrations** (invariant #1) |

CI (`.github/workflows/`: `backend.yml`, `frontend.yml`, `contracts.yml` — the last only validates JSON files) builds + type-checks + tests on path-filtered pushes/PRs, but has **false-green traps**: the backend job runs `dotnet test` from `backend/RestaurantApi` (the web project — discovers zero tests) and the customer `npm test` is a stub. Never take green CI as proof tests passed — run them locally.

## Cross-cutting invariants

1. **Multi-tenancy is the #1 invariant — in `RestaurantApi`.** Every tenant-owned entity implements `ITenantScoped`, gets a global query filter + `restaurant_id` index in `AppDbContext`, and is auto-stamped on insert. `TenantResolutionMiddleware` (not `[Authorize]` attributes or role policies) is what scopes staff (`restaurantId` route + membership) and customers (`qrToken`). Any new endpoint or entity must be reviewed for cross-tenant leakage first.
   - **`PlatformApi` and `OpsApi` deliberately opt out.** Neither uses `ITenantScoped`, a query filter, or `TenantResolutionMiddleware` — they are control planes, and cross-tenant reach *is* the feature. They gate on an auth fallback policy instead (`platformAdminOnly` in `PlatformApi/Program.cs`; a default policy + `login` policy in `OpsApi`).
   - **The hazard**: `PlatformDbContext` maps its own entities onto `RestaurantApi`'s core tables (`organizations`, `restaurants`, `memberships`, `AspNetUsers`) with **no tenant filter**. A careless query there returns every tenant's rows and no filter will stop it. `OpsApi` is safe by construction — its `ops_*` tables share no data with tenant tables.
   - **Schema ownership**: `RestaurantApi`'s EF migrations own the shared schema. `PlatformApi` and `OpsApi` have **no `Migrations/` folder** — their own tables (`platform_admins`, `platform_registration_links`, `ops_*`) are created by raw-SQL `CREATE TABLE IF NOT EXISTS` at startup (`Data/PlatformBootstrap.cs`, `Data/OpsBootstrap.cs`). Never add an EF migration to those two projects; extend the bootstrap instead. Changing a shared table means a `RestaurantApi` migration **plus** checking the `PlatformApi` entity that maps it.
2. **Contract changes are full-stack slices.** The wire contract lives in three hand-synced places: C# DTOs (`backend/RestaurantApi/DTO/`), `shared/types/*.ts` (edit the `.ts`, not the committed generated `.js`/`.d.ts`), and validation constants (`shared/types/menu-validation.ts` ↔ C# validators). A feature touching the API almost always touches backend + `shared/types` + both frontends in the same change.
3. **Money is `decimal`** (`decimal(10,2)` prices, `decimal(12,3)` stock) — never float/double, and round with `utils/money.ts` on the customer side.
4. **Order statuses are exact string literals** (`draft`, `pending_dispatch`, `submitted`, `preparing`, `ready`, `completed`, `cancelled`, `merged`) — no enum backs them; typos fail silently. Stock decrements at kitchen **dispatch** (not submit), idempotently; cancel restores it.
   - **Order state changes go through the `order_events` outbox.** `Services/OrderEventWriter.cs` writes the event in the *same transaction* as the order mutation (migration `AddWaiterModeAndOrderEvents`). Don't emit an order change by calling SignalR directly and skipping the writer — the outbox is what keeps Waiter Mode and the kitchen consistent when a connection drops.
5. **SignalR event names are PascalCase, JSON payloads camelCase.** The customer app implements the SignalR protocol *by hand* (`useWebSocket.ts`), so renaming events or changing casing breaks it with no compile error. Group names always come from `Hubs/TenantGroups.cs`.
6. **UI language**: admin strings (including Waiter Mode) are Romanian; customer strings need both RO and EN (`useLocale.ts`). The two internal consoles — `frontend-platform` and `frontend-ops` — are **English-only and have no i18n layer**; don't add Romanian strings or a `useLocale` there.
7. **AI providers are pluggable** (`AI_*_PROVIDER` env vars); default models are cost-driven (`gemini-3.1-flash-lite`) — don't switch to expensive models without being asked.

## Git policy

**Never run git state-changing commands** (`add`, `commit`, `push`, `checkout`, `branch`, `merge`, `reset`, `stash`, …) unless the user explicitly asks in that moment — the user manages all git state manually. Read-only git (`status`, `diff`, `log`, `show`) is always fine.

When commits are requested: loose Conventional Commits (`feat(inventory): …`, `fix: …`, scopes seen: `inventory`, `menu-import`, `tests`); branches `feature/*` / `fix/*`. Large features land as numbered single-concern "chunks" (see the `feat(inventory)` series) — each chunk a coherent full-stack slice.

## Where the real documentation lives

- `docs/superpowers/specs/*.md` — **design specs, the architecture source of truth** for the AI menu-import pipeline (photo import, PDF hardening/leasing, AI categorization) and email deliverability; paired TDD plans in `docs/superpowers/plans/`.
- `docs/menu-import-pdf-smoke.md` — manual smoke runbook + all PDF tuning env vars. `docs/email-deliverability-setup.md` — prod email (Resend; Railway blocks SMTP). `docs/railway-persistent-uploads.md` — upload volume setup.
- **The newer verticals, each with its own doc**: `docs/waiter-app.md` (Waiter Mode — table sessions, floor plan, order taking), `docs/logistic-app.md`, `docs/vibedine-hq.md` (PlatformApi + frontend-platform), `docs/ops-server-management-app-plan.md` (OpsApi + frontend-ops), `docs/delivery-embedded-storefront-plan.md`. Read the relevant one before touching those services — they are **not** covered by the `backend/CLAUDE.md` area guide.
- `CR_notes/` — a 25-segment code review; `CODE_REVIEW_HIGH_PRIORITY.md` is the tiered blocker list. Check the relevant segment before touching auth, tenancy, SignalR, or migrations.
- The inventory/stock system has **no doc** — its design is summarized in `backend/CLAUDE.md`.
- ⚠️ **Stale docs — do not trust**: `frontend-customer/FRONTEND_DEV_GUIDE.md` + `README.md` (describe a dead architecture), `backend/ai-scripts/PLAN.md` (pre-LLM design), `backend/*-guide.md` (predate refactors), `shared/contracts/api-contract-ai.json` (describes a dead API).

## Code-review focus (recurring risk themes from CR_notes)

Tenant isolation on every new query/endpoint (filter + membership check, incl. SignalR group joins) · **`PlatformApi` queries have no safety net** — its unfiltered access to the shared core tables means every new query there needs a hand-written scope check · customer endpoints trust `qrToken` only — never accept a client-supplied `restaurantId`/`orderId` without ownership checks · input caps on bulk endpoints and quantity/availability validation on order writes · file uploads need magic-byte checks, not client MIME · refresh-token and email-confirmation flows have known weaknesses (see `CODE_REVIEW_SEGMENT_05_AUTH.md`) before extending them · AI-service failures must not be swallowed into `200 {}` responses · timestamps UTC (`timestamptz`).
