# Code Review — Segment 01: Compose & service topology

**Date:** 2026-05-21
**Reviewer:** Claude (Opus 4.7, read-only)
**Scope:** `docker-compose.yml` plus each service's Dockerfile and `.dockerignore`.

## Files reviewed

| Role | Path |
|---|---|
| Compose file | [docker-compose.yml](docker-compose.yml) |
| Backend Dockerfile | [backend/RestaurantApi/Dockerfile](backend/RestaurantApi/Dockerfile) |
| Backend dockerignore | [backend/RestaurantApi/.dockerignore](backend/RestaurantApi/.dockerignore) |
| AI service Dockerfile | [backend/ai-scripts/Dockerfile](backend/ai-scripts/Dockerfile) |
| AI service dockerignore | [backend/ai-scripts/.dockerignore](backend/ai-scripts/.dockerignore) |
| Customer SPA Dockerfile | [frontend-customer/Dockerfile](frontend-customer/Dockerfile) |
| Customer SPA dockerignore | [frontend-customer/.dockerignore](frontend-customer/.dockerignore) |
| Admin SPA Dockerfile | [frontend-admin/Dockerfile](frontend-admin/Dockerfile) |
| Admin SPA dockerignore | [frontend-admin/.dockerignore](frontend-admin/.dockerignore) |

No `CLAUDE.md` files in the repo — checklist-only review.

---

## Checklist outcomes

| # | Item | Result |
|---|---|---|
| 1 | 🔥 Every secret defaulted in compose is documented as dev-only and a prod override path exists | **FAIL** — see Finding 1 |
| 2 | `depends_on: service_healthy` set where boot-time calls occur; frontends independent | **PASS** — backend waits for `postgres` (healthy) + `mailpit` (started); frontends have no depends_on (browser-side connection) |
| 3 | `./backend/RestaurantApi:/src` mount is intentional for hot-reload, not shipped to prod | **FAIL** — see Finding 2 |
| 4 | `AllowedOrigins__0/1` matches exposed host ports (3000, 3001) | **PASS** — [docker-compose.yml:25-26](docker-compose.yml#L25-L26) vs ports 3000/3001 |
| 5 | `VITE_API_URL` and `VITE_WS_URL` agree on host/port; `ws://` vs `wss://` matches the API scheme | **PASS** — both `localhost:5005`, both unencrypted (`http://` + `ws://`) for dev. Admin has only `VITE_API_URL` (no SignalR wired yet — segment 23 confirmed polling-only) |
| 6 | `.dockerignore` excludes `bin/`, `obj/`, `node_modules/`, `.env` | **PASS** — see verification below |
| 7 | Mailpit only exposes port 8025 to host; not reachable from prod | **PARTIAL** — see Finding 3 |
| 8 | `pgdata` is a named volume; `docker compose down -v` behavior known | **PASS** — [docker-compose.yml:90-91](docker-compose.yml#L90-L91) |
| 9 | Each Dockerfile pins base-image tags (no `:latest` except mailpit) | **PASS** — see verification below |
| 10 | Healthcheck on `backend-api` exists, or absence is recorded as explicit decision | **FAIL** — see Finding 4 |

### `.dockerignore` verification

| File | Excludes |
|---|---|
| [backend/RestaurantApi/.dockerignore](backend/RestaurantApi/.dockerignore) | `bin/`, `obj/`, `*.user`, `*.suo`, `.vs/`, `appsettings.Development.json` ✓ |
| [backend/ai-scripts/.dockerignore](backend/ai-scripts/.dockerignore) | `__pycache__/`, `*.pyc`, `*.pyo`, `.venv/`, `venv/`, `.env`, `pytest_cache/`, `*.egg-info/` ✓ |
| [frontend-admin/.dockerignore](frontend-admin/.dockerignore) | `node_modules/`, `dist/`, `.env`, `.env.local`, `*.log` ✓ |
| [frontend-customer/.dockerignore](frontend-customer/.dockerignore) | identical to admin ✓ |

Note: the AI service ignores `.env`, then [docker-compose.yml:63-64](docker-compose.yml#L63-L64) re-supplies it via `env_file:` from the host. That's the correct shape — the env file isn't baked into the image, it's mounted at runtime.

### Base image pinning verification

| Service | Image | Pinned? |
|---|---|---|
| `backend-api` (build) | `mcr.microsoft.com/dotnet/sdk:8.0` | major-version pinned (`:8.0`) — auto-takes patch updates |
| `backend-api` (runtime) | `mcr.microsoft.com/dotnet/aspnet:8.0` | same |
| `ai-service` | `python:3.11-slim` | major-version pinned |
| `frontend-admin` / `frontend-customer` | `node:20-alpine` | major-version pinned |
| `postgres` | `postgres:16` | major-version pinned |
| `mailpit` | `axllent/mailpit:latest` | **`:latest`** — explicitly accepted for dev by the roadmap |

Major-version-only pinning auto-pulls security patches on rebuild without breaking on a major release. Acceptable for this stage of the project; tighten to digest pinning once a deploy pipeline exists.

---

## Findings

### Finding 1 — 🔥 Dev secrets in compose with no documented prod override path (HIGH)

[docker-compose.yml:23-42](docker-compose.yml#L23-L42) sets every secret inline:

```yaml
ConnectionStrings__Default: "Host=postgres;Database=restaurant;Username=dev;Password=devpass"
Jwt__Key: "dev-only-super-secret-signing-key-change-me-32+chars"
POSTGRES_PASSWORD: devpass
```

The `Jwt__Key` value itself says `dev-only-…-change-me`, which is **partial** documentation. `POSTGRES_PASSWORD`, `Jwt__Issuer`, `Jwt__Audience`, `Smtp__FromAddress`, `App__AdminBaseUrl` are not labelled.

The repo contains:
- [backend/RestaurantApi/.env.example](backend/RestaurantApi/.env.example) — lists `ConnectionStrings__Default` and `AiService__BaseUrl` only, two of the ~10 env vars the API actually needs.
- No `docker-compose.prod.yml`.
- No `docker-compose.override.yml`.
- No `.env.example` at the repo root.

There is no path for a deployer to spin this stack up in production without forking the compose file. A new developer onboarding cannot tell which values are dev-only and which need genuine prod-grade secrets.

Today `frontend-admin` (3001), `frontend-customer` (3000), and `backend-api` (5005) all bind to host ports — running this compose on a host with public IP would expose Postgres-via-API (no JWT secret rotation), Mailpit UI (8025), and SMTP (1025) to the internet.

**Recommendation:**
1. Add `docker-compose.prod.yml` that overrides every secret with `${VAR}` and either removes Mailpit (see Finding 3) or fronts it with an internal-network-only setup.
2. Add `.env.example` at the repo root that enumerates **every** environment variable the stack accepts, with `# dev-only`/`# prod-required` markers.
3. Add a startup check in `Program.cs`: refuse to boot if `ASPNETCORE_ENVIRONMENT != "Development"` and `Jwt__Key` still contains `"change-me"` or `"dev-only"`.

---

### Finding 2 — `./backend/RestaurantApi:/src` bind mount is dead code (MEDIUM)

[docker-compose.yml:48-49](docker-compose.yml#L48-L49):

```yaml
volumes:
  - ./backend/RestaurantApi:/src
```

But [backend/RestaurantApi/Dockerfile](backend/RestaurantApi/Dockerfile) is a multi-stage build whose **runtime** stage uses:

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "RestaurantApi.dll"]
```

The runtime container's working directory is `/app`. The bind mount targets `/src`, which is only the **build stage's** workdir and doesn't exist in the runtime image at all. So:

- The bind mount does nothing useful at runtime — the container reads its compiled DLL from `/app`, not from `/src`.
- There is no hot-reload happening for the backend, despite the apparent intent.
- A developer who edits `Program.cs` thinks the bind mount will pick it up; nothing changes until they `docker compose build`.

Two ways to fix, in increasing order of effort:
- **Cheap fix:** delete the volume mount. It's misleading.
- **Real fix:** change the runtime command to `dotnet watch run` against a bind-mounted source tree, exposed only in dev via `docker-compose.override.yml`. That's the conventional shape for hot-reload in .NET dev containers.

Either way, this is a footgun: anyone debugging "why don't my changes show up" will spend time on it before realising.

---

### Finding 3 — Mailpit exposes both ports to the host (LOW/MEDIUM)

[docker-compose.yml:51-55](docker-compose.yml#L51-L55):

```yaml
mailpit:
  image: axllent/mailpit:latest
  ports:
    - "1025:1025"   # SMTP
    - "8025:8025"   # Web UI
```

Both ports bind to the host. In dev that's the point — you click `http://localhost:8025` to see captured emails. But the same compose file is the only deployment artefact in the repo (Finding 1). If a deployer copies this to a server and forgets to remove Mailpit, the UI is on the internet, and SMTP 1025 accepts mail without auth.

The right shape is to gate Mailpit behind `profiles: ["dev"]` so `docker compose up` in prod (without `--profile dev`) silently omits it:

```yaml
mailpit:
  image: axllent/mailpit:latest
  profiles: ["dev"]
  ports:
    - "1025:1025"
    - "8025:8025"
```

Also covered as the dev side of [Segment 25 Finding 5](CODE_REVIEW_SEGMENT_25_SECURITY.md).

---

### Finding 4 — No healthcheck on `backend-api`; no health endpoint either (MEDIUM)

[docker-compose.yml:18-49](docker-compose.yml#L18-L49) for `backend-api` has no `healthcheck:` block, no comment recording the absence. Cross-referenced with [Program.cs](backend/RestaurantApi/Program.cs) — there is no `MapHealthChecks("/healthz")` call and no `/health` endpoint at all (the segment-03 verification command `Invoke-WebRequest http://localhost:5005/healthz` would 404).

Consequences:
- `depends_on: condition: service_healthy` on any future service that needs the API can never be satisfied.
- Orchestrators (Compose v2's `restart: on-failure` works, but Kubernetes/Nomad/ECS need liveness + readiness probes) have no signal to know the API is up beyond "process is running" — a deadlocked API or a hung DB connection will sit forever without being noticed.
- The existing Postgres service does the right thing ([docker-compose.yml:12-16](docker-compose.yml#L12-L16)) using `pg_isready`. Backend should match the pattern.

**Recommendation:** add a thin `/healthz` endpoint (`app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));`) and a compose healthcheck. Optional: a `/readyz` that does a 1-row DB ping for genuine readiness.

---

### Finding 5 — Frontend containers run `npm run dev`; no prod build path (MEDIUM, cross-cutting with Finding 1)

[frontend-admin/Dockerfile](frontend-admin/Dockerfile) and [frontend-customer/Dockerfile](frontend-customer/Dockerfile) both end with:

```dockerfile
CMD ["npm", "run", "dev", "--", "--host", "0.0.0.0", "--port", "3001"]
```

So the production-shaped image runs the Vite dev server, with HMR enabled, source maps, and every dev dependency bundled. This is fine in dev (and presumably the intent), but it means the same Dockerfile cannot ship to prod — there's no `npm run build` + `nginx`/`caddy` static-serve stage anywhere.

This is consistent with Finding 1: the entire repo's container story is dev-only.

---

### Finding 6 — No non-root user in any Dockerfile (LOW)

Every Dockerfile runs as root inside the container. For a dev stack this is conventional and acceptable; for prod it's the baseline mitigation that lets you survive a container-escape CVE in your runtime.

Pattern, applied to the backend runtime stage:

```dockerfile
RUN addgroup --system app && adduser --system --ingroup app app
USER app
```

…and similar `useradd -m -u 1001 node` in the Node images. Schedule for the prod hardening pass alongside Finding 1 / Finding 5.

---

### Finding 7 — `shared/` is not mounted into `frontend-customer` despite the SPA importing from `@shared/*` (HIGH — same as Segment 24 F3)

Carry-over from [Segment 24 Finding 3](CODE_REVIEW_SEGMENT_24_SHARED_CONTRACTS.md). [docker-compose.yml:77-88](docker-compose.yml#L77-L88) for `frontend-admin` mounts `./shared:/shared:ro`; [:66-75](docker-compose.yml#L66-L75) for `frontend-customer` does not. The customer SPA imports from `@shared/types/...` in 9+ files, which resolve via Vite alias to `../shared` → `/shared` inside the container. So either the customer Docker target is silently broken or the team only runs the customer app on the host.

One-line fix; documented in segment 24.

---

### Finding 8 — `frontend-customer` `.dockerignore` doesn't include `.git`, build artifacts (LOW NIT)

The `.dockerignore` files cover the obvious cases. Modern best practice is to also exclude:

```
.git/
.gitignore
README.md
CODE_REVIEW_*.md
docs/
.vscode/
.idea/
```

…so they don't bloat the build context or get baked into the image. The current frontend `.dockerignore` files miss `.git`. Not a security or functionality issue, just image bloat (~hundreds of MB if the `.git` history is heavy).

---

## Verification commands (per roadmap)

The roadmap suggested running:
```powershell
docker compose config
docker compose up -d --build
docker compose ps
docker compose logs backend-api --tail 50
Invoke-WebRequest http://localhost:5005/swagger/index.html
Invoke-WebRequest http://localhost:8000/health
Invoke-WebRequest http://localhost:8025
```

Not executed in this read-only pass. Recommend running once after Findings 1, 2, 4 are fixed and recording outputs in the eventual sign-off summary.

---

## Summary

**Top three risks**
1. **F1** — dev secrets baked in, no prod override path. Resolves jointly with F3 (Mailpit profile) and F5 (frontend prod build). Touches every deploy decision the project still has to make.
2. **F2** — dead bind mount masquerading as hot reload. Cheap to fix; one of those small bugs that costs new contributors a half-day to diagnose.
3. **F4** — no healthcheck and no health endpoint. Limits future orchestration choice and hides API liveness regressions.

**Top three strengths**
- Postgres healthcheck + `depends_on: service_healthy` on backend-api — exactly the right shape; backend waits for the DB before starting.
- `appsettings.Development.json` is `.dockerignore`d on the backend image, and the matching env var (`Jwt__Key`) is supplied via compose. Clear separation of "what ships" vs "what's injected".
- Multi-stage Dockerfile for the .NET API keeps the runtime image lean (aspnet runtime only, not the SDK).

**Recommended fix order**
1. F2 (drop the dead `/src` mount — one-line PR).
2. F4 (add `/healthz` endpoint + compose healthcheck).
3. F7 (mount `shared/` into the customer container — one-line PR, also closes Segment 24 F3).
4. F3 (gate Mailpit behind `profiles: ["dev"]`).
5. F1 + F5 + F6 — prod hardening pass: `docker-compose.prod.yml`, prod Dockerfiles for the frontends with `npm run build` + static-serve, non-root users, `.env.example` at repo root, startup secret-guard check.
6. F8 — `.dockerignore` polish.

No code modified during this review.
