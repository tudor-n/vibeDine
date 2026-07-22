# vibeDine Ops — Server / Infrastructure Management App

> **Status:** implementation plan + living doc. Branch `feature/ops-server-management`.
> **Created:** 2026-07-09.

## Context

The stack is deployed on **Railway** today and will move to a **self-hosted VPS (Hetzner or similar)** soon.
We need a single console to run the infrastructure — deployments, logs, environment variables,
domains, audit — with **full control and full visibility**, and it must be **modular** so that swapping
Railway for a VPS is a provider change, not a rewrite. The onboarding/**connection flow must be dead simple**
(paste a token → validate → auto-discover), and provider credentials must **never** reach the browser.

This app is the **4th service** in the monorepo, mirroring the existing `PlatformApi` + `frontend-platform`
sibling pair (own auth, own JWT issuer, shared Postgres, **no EF migrations** — idempotent SQL bootstrap).

## Architecture

```
frontend-ops (Vue 3, :3003)                     ← login · Connections wizard · Deployments · Logs · Audit
      │  Bearer JWT (ops admin)                     token NEVER sent here
      ▼
backend/OpsApi (.NET 8, :5007→5000)
  ├─ Auth            PBKDF2 + JWT issuer "vibedine-ops"  (ops_admins)
  ├─ Security        AES-GCM token protector (provider creds encrypted at rest)
  ├─ Providers/      ── the modular core ──
  │   ├─ IInfraProvider  + ProviderCapabilities   ← UI shows only what a provider supports
  │   ├─ Railway/       RailwayProvider + rate-limit-aware GraphQL client   (implemented)
  │   ├─ Vps/           VpsProvider (SSH/systemd/journalctl)               (scaffolded, same interface)
  │   └─ ProviderRegistry
  ├─ Services        AuditLogger · ConnectionService · LogTailManager
  ├─ Hubs            LogStreamHub (SignalR live tail, single upstream → fan-out)
  └─ Data            OpsDbContext + OpsBootstrap (CREATE TABLE IF NOT EXISTS, no migrations)
      ▼
Postgres (shared `restaurant` DB) — ops_* tables only
      ▼
backboard.railway.com/graphql/v2   (GraphQL over HTTPS; token held server-side)
```

### Modularity — the provider contract
`IInfraProvider` (in `Providers/Abstractions/`) is the seam. Every capability is a method; a
`ProviderCapabilities` flags object tells the UI what to render. `RailwayProvider` implements it against
the verified GraphQL queries; `VpsProvider` is a same-shaped scaffold (SSH exec → `systemctl`, `docker`,
`journalctl`) ready to fill in when the Hetzner box lands. `ProviderRegistry` resolves provider-by-key from
the stored connection, so **adding a provider = one class + registry line**, no controller/UI rewrite.

## Data (ops_* tables, bootstrapped idempotently — never migrated)
- `ops_admins` — console operators (PBKDF2), seeded `ops@vibedine.local` / `ChangeMe123!`.
- `ops_connections` — a provider connection; **credentials AES-GCM encrypted** (`token_cipher`), plus
  discovered account label, capabilities snapshot, status, last-checked.
- `ops_audit_log` — **our own** audit trail: every action taken through the console (who/what/when/result),
  distinct from the provider's own audit feed.
- `ops_log_archive` — logs ingested from the provider, persisted **past Railway's 7-day retention**.

## Backend surface (`/api/ops/*`)
| Controller | Routes | Notes |
|---|---|---|
| Auth | `auth/login`, `auth/me` | mirrors PlatformApi AuthController |
| Connections | list/get/create/test/delete | **easy connect**: create validates token live, encrypts, auto-discovers |
| Resources | projects, project detail, services, environments, deployments, **overview** | overview = service/process canvas |
| Deployments | `redeploy/restart/rollback/stop/cancel/remove` | audited; destructive ops flagged |
| Logs | build/deploy/http fetch (+ filter), archive query, ingest | Railway filter syntax passthrough |
| Variables | get / upsert | destructive `replace` guarded |
| Audit | ops audit log + provider audit log | |
| Metrics | per-service, per-server aggregate, fleet-wide roll-up (CPU/RAM/network) | **verified** against live Railway 2026-07-09 |

`[DE CONFIRMAT]` Railway queries (network-flow logs, live subscriptions, exact `auditLogs`
signature) can't be introspected without a live token, so they're implemented as clearly-marked,
**gracefully-degrading** methods returning empty/`NotSupported` instead of guessing field names.

## Frontend (`frontend-ops`, Vue 3 + Pinia + Tailwind, mirrors frontend-platform)
`fetch` wrapper (`authed`/`fetcher`), setup-store auth with JWT in localStorage, default-closed router guard,
CSS-var Tailwind kit (`.card`/`.pill-btn`/`.field-input`/`.badge`/`.data-table`). Views: Login,
**Connections (guided connect wizard)**, Dashboard, Project/Deployments, Logs viewer (live tail + filter),
Audit.

## Docker (ready to run)
Add `ops-api` (`5007:5000`) and `frontend-ops` (`3003:3003`) to root `docker-compose.yml`, matching the
`platform-api` / `frontend-platform` env + volume patterns. `docker compose up` brings the whole thing online.

## Verification
1. `DOTNET_ROLL_FORWARD=LatestMajor dotnet build backend/OpsApi` — compiles.
2. `docker compose config` — compose validates; `docker compose up ops-api frontend-ops` — both boot.
3. Login at `http://localhost:3003` with `ops@vibedine.local` / `ChangeMe123!`.
4. Connections → Connect Railway → paste account token → validates via `me { name email }`, auto-discovers projects.
5. Browse deployments, fetch logs, run an audited redeploy; confirm entries land in the Audit view.
