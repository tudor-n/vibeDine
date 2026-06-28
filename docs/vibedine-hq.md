# vibeDine HQ — Platform Control Plane

> Developer / super-admin console with system-wide access and full CRUD over every
> organization, location, location admin and hire on the platform.

This is the **third app** in the repo, alongside:

| App | Frontend | Backend | Audience |
|-----|----------|---------|----------|
| Customer | `frontend-customer` (:3000) | `RestaurantApi` (:5005) | Diners scanning a table QR |
| Admin | `frontend-admin` (:3001) | `RestaurantApi` (:5005) | A restaurant's owner/admin/waiter |
| **HQ** | **`frontend-platform` (:3002)** | **`PlatformApi` (:5006)** | **Your team (platform operators)** |

---

## 1. What it is for

HQ is the back-office for the people running the SaaS. From here the team can:

- See **every** organization (customer account), its locations, admins and hires.
- Create / edit / delete organizations.
- Create / edit / activate / deactivate locations.
- Manage a location's **admin** (assign, remove) and **hires** (approve, reject, remove).
- Generate **one-time registration links** to onboard new clients / new locations.

### Product roadmap (the intended end-state)

Clients will **no longer self-create** organizations or locations. Instead:

1. A client signs up + pays a subscription on the **landing page**.
2. The landing page (or an operator in HQ) generates a **one-time registration link**.
3. The link redirects the client into the **restaurant app** to register and create their
   account. Every account starts with **one default location**.
4. To add another location, the client pays again → a new one-time link is generated →
   after sign-in they get a new, editable location.

> HQ already **generates** these links (`/api/platform/registration-links`). The
> **redemption** side (the restaurant app consuming a token to register / add a location,
> and disabling client self-service) is **not built yet** — see §10.

---

## 2. How it is wired (the request flow)

`http://localhost:5006` is a **headless JSON API** — browsing `/` returns 404 on purpose.
The page you open is **`http://localhost:3002`**.

```
You (browser)
   │  1. open  http://localhost:3002
   ▼
frontend-platform  (Vue 3 SPA, :3002)
   │  2. login → POST :5006/api/platform/auth/login
   ▼
platform-api  (.NET 8, :5006)
   │  3. verifies password in platform_admins, returns a JWT
   │  4. every later request carries  Authorization: Bearer <jwt>
   ▼
postgres  (shared "restaurant" DB, :5433)
   5. reads/writes organizations, restaurants, memberships, AspNetUsers (+ HQ tables)
```

All services run on the `restaurant-net` Docker network and reach each other by service
name (e.g. `Host=postgres`). The browser reaches the APIs via `localhost` host ports.

---

## 3. Data model — how HQ concepts map to the schema

HQ owns **no domain schema**; it reuses the main app's tables.

| HQ concept | Table | Notes |
|------------|-------|-------|
| Organization (customer account) | `organizations` | `1 org → many restaurants` |
| Location | `restaurants` | **A restaurant IS a location.** |
| Location admin | `memberships` where `role = 1` (`RestaurantAdmin`) | scoped to one `restaurant_id` |
| Hire | `memberships` where `role = 2` (`Waiter`) | scoped to one `restaurant_id`; may be `pending_approval` |
| Org owner | `memberships` where `role = 0` (`OrgOwner`) | `restaurant_id` is null |
| User account | `AspNetUsers` | Identity table (PascalCase columns); read-only from HQ |

Membership `role` is an **int** (`0=OrgOwner, 1=RestaurantAdmin, 2=Waiter`).
Membership `status` is a **string** (`active`, `pending_approval`, `rejected`).

### HQ-owned tables (only two)

| Table | Purpose |
|-------|---------|
| `platform_admins` | HQ operator accounts (separate from restaurant users) |
| `platform_registration_links` | one-time provisioning links |

---

## 4. Auth model

HQ auth is **fully decoupled** from restaurant users.

- Operators live in `platform_admins`, passwords hashed with **PBKDF2** (`Auth/PasswordHasher.cs`).
- Login issues a JWT with its **own** issuer/audience (`vibedine-platform`), distinct from the
  restaurant app's `smart-restaurant` tokens — the two token sets are not interchangeable.
- The JWT `sub` claim is the platform admin id; controllers read it via
  `User.PlatformAdminId()` (`Auth/ClaimsExtensions.cs`).
- All controllers except `POST /auth/login` require `[Authorize]`.
- The frontend stores the token in `localStorage` under `vibedine_platform_auth`
  (`stores/useAuthStore.ts`) and attaches it on every call; a `401` triggers logout
  (`services/api-client.ts`).

**Default seeded operator** (created on first backend start if `platform_admins` is empty):

```
admin@vibedine.local  /  ChangeMe123!
```

Override via env: `PlatformSeed__Email`, `PlatformSeed__Password`.

---

## 5. ⚠️ Critical constraint: HQ does not own the schema

`PlatformApi` connects to the **same database** the main backend migrates, but it must not
fight over the schema:

- The HQ `PlatformDbContext` maps `organizations`, `restaurants`, `memberships`,
  `AspNetUsers` for read/write **but runs no EF migrations** for them — `RestaurantApi`
  owns those tables and their migrations.
- The two HQ-owned tables are created via **idempotent `CREATE TABLE IF NOT EXISTS`** in
  `Data/PlatformBootstrap.cs` on startup. There is **no EF migration in this project**.

**Do NOT** add EF migrations to `PlatformApi`, and **do NOT** change the column mappings
in `PlatformDbContext` without matching the real DB columns. If you need a new HQ-owned
table, add it to the bootstrap SQL (and a mapped entity). If you need a new column on a
main-app table, add it in `RestaurantApi`'s migrations, then map it here.

The exact physical columns (for reference):
- `organizations`: `id, name, slug, created_by_user_id, created_at` (snake_case)
- `restaurants`: `id, organization_id, name, slug, address, timezone, currency, is_active, created_at, locale`
- `memberships`: `id, user_id, organization_id, restaurant_id, role(int), invited_by_user_id, created_at, status`
- `AspNetUsers`: `Id, Email, FullName, EmailConfirmed, CreatedAt, PasswordHash, …` (**PascalCase**)

---

## 6. Backend — `backend/PlatformApi`

.NET 8 + EF Core (Npgsql). Mirrors `RestaurantApi` conventions (controllers, DTO
Requests/Responses, camelCase JSON, JWT bearer).

```
backend/PlatformApi/
├── Program.cs                  # DI, JWT, CORS, runs PlatformBootstrap on startup
├── appsettings.json            # local defaults (overridden by env in docker)
├── Dockerfile                  # sdk:8.0 build → aspnet:8.0 runtime, EXPOSE 5000
├── Auth/
│   ├── JwtOptions.cs
│   ├── PlatformJwtService.cs    # issues access tokens
│   ├── PasswordHasher.cs        # PBKDF2 IPasswordHasher
│   └── ClaimsExtensions.cs      # User.PlatformAdminId()
├── Common/Slug.cs               # name → url slug
├── Data/
│   ├── PlatformDbContext.cs     # maps existing tables + 2 HQ tables (no migrations)
│   └── PlatformBootstrap.cs     # CREATE TABLE IF NOT EXISTS + seed admin
├── Models/                      # Organization, Restaurant, Membership, AppUser,
│                                #   PlatformAdmin, RegistrationLink
├── DTO/
│   ├── Requests/PlatformRequests.cs
│   └── Responses/PlatformResponses.cs
└── Controllers/
    ├── AuthController.cs
    ├── MetricsController.cs
    ├── OrganizationsController.cs
    ├── LocationsController.cs
    ├── MembersController.cs
    └── RegistrationLinksController.cs
```

### Endpoint reference (all under `/api/platform`)

| Method & path | Purpose |
|---------------|---------|
| `POST /auth/login` | email+password → `{ accessToken, expiresAt, admin }` |
| `GET  /auth/me` | current operator |
| `GET  /metrics` | counts (orgs, locations, active, admins, hires, pending hires) |
| `GET  /organizations?search=` | list with location/admin/hire counts + owner |
| `GET  /organizations/{id}` | detail: owners + locations |
| `POST /organizations` | create `{ name, slug? }` |
| `PATCH /organizations/{id}` | update `{ name?, slug? }` |
| `DELETE /organizations/{id}` | delete (409 if it still has locations) |
| `GET  /locations?organizationId=&search=&status=` | list (status = active\|inactive) |
| `GET  /locations/{id}` | detail: admins[] + hires[] |
| `POST /locations` | create `{ organizationId, name, address?, timezone?, currency?, locale? }` |
| `PATCH /locations/{id}` | update fields |
| `PATCH /locations/{id}/status` | `{ isActive }` activate/deactivate |
| `DELETE /locations/{id}?force=true` | hard delete (409 without `force`; deactivate is preferred) |
| `GET  /locations/{id}/members` | admins + hires of a location |
| `POST /locations/{id}/members` | assign an **existing** user as admin `{ email }` (404 if no account) |
| `PATCH /locations/{id}/members/{membershipId}/status` | `{ status }` approve/reject |
| `DELETE /locations/{id}/members/{membershipId}` | remove membership |
| `GET  /registration-links?status=` | list links |
| `POST /registration-links` | `{ type: new_organization\|new_location, organizationId?, label?, intendedEmail?, expiresInDays? }` |
| `POST /registration-links/{id}/revoke` | revoke an active link |

Swagger (dev): **http://localhost:5006/swagger**

---

## 7. Frontend — `frontend-platform`

Vue 3 + Vite + Pinia + Tailwind. Same design tokens / look as `frontend-admin`
(light + dark via `data-theme`).

```
frontend-platform/src/
├── main.ts
├── App.vue                      # shell (sidebar + topbar) vs bare (login)
├── style.css                    # design tokens + component classes
├── router/index.ts             # routes + auth guard
├── services/api-client.ts      # fetch wrapper, bearer token, 401 → logout
├── stores/useAuthStore.ts      # token + admin in localStorage
├── composables/useToast.ts
├── components/ToastHost.vue
├── types/platform.ts           # TS mirrors of the backend DTOs
├── utils/format.ts             # date + status/role label helpers
└── views/
    ├── LoginView.vue
    ├── DashboardView.vue
    ├── OrganizationsView.vue
    ├── OrganizationDetailView.vue
    ├── LocationsView.vue
    ├── LocationDetailView.vue
    └── RegistrationLinksView.vue
```

### Routes (`router/index.ts`)

| Path | View | Notes |
|------|------|-------|
| `/login` | LoginView | `meta.public` — only public route |
| `/` | DashboardView | counts → quick links |
| `/organizations` | OrganizationsView | list + create + delete |
| `/organizations/:id` | OrganizationDetailView | owners, locations, add location |
| `/locations` | LocationsView | list + filters + create |
| `/locations/:id` | LocationDetailView | admins + hires + status + delete |
| `/registration-links` | RegistrationLinksView | generate + copy + revoke |

The guard redirects unauthenticated users to `/login` and authenticated users away
from `/login`.

### Frontend conventions to follow

- **Views are setup-style** `<script setup lang="ts">`; each owns `loading` / `error`
  state and calls its `load()` in `onMounted(load)` (don't forget the call — a missing
  `onMounted(load)` is what caused the "stuck on Loading…" bug originally).
- API calls go through `apiClient` / `authApi` in `services/api-client.ts`; never `fetch`
  directly from a view.
- Use the existing component classes: `.card`, `.pill-btn`, `.btn-ghost`, `.btn-danger`,
  `.field-input`, `.field-label`, `.badge` (+ `.badge-green/amber/red`), `.data-table`,
  `.modal-backdrop`/`.modal`, `.eyebrow`, `.text-muted`/`.text-faint`.
- User-facing toasts via `useToast()`.

---

## 8. Local development

Everything runs through `docker-compose.yml`.

```bash
# bring up just the HQ services (deps start automatically)
docker compose up -d --build platform-api frontend-platform

# or the whole stack
docker compose up -d

# logs
docker compose logs -f platform-api
docker compose logs -f frontend-platform
```

Then open **http://localhost:3002** and log in with `admin@vibedine.local` / `ChangeMe123!`.

- **Frontend** runs the Vite dev server with the source volume-mounted → **edits hot-reload
  instantly**, no rebuild needed.
- **Backend** runs the published DLL (like `RestaurantApi`) → **code changes need a rebuild**:
  `docker compose up -d --build platform-api`.
- Compile the backend without the runtime headache via Docker: `docker build backend/PlatformApi`.
  (Local `dotnet` needs `DOTNET_ROLL_FORWARD=LatestMajor` — see `docs`/memory for the net8/net9 note.)

### Config / env (docker-compose `platform-api`)

| Env var | Default | Meaning |
|---------|---------|---------|
| `ConnectionStrings__Default` | `Host=postgres;Database=restaurant;…` | shared DB |
| `Jwt__Key` / `Jwt__Issuer` / `Jwt__Audience` | `…` / `vibedine-platform` / `vibedine-platform` | token signing |
| `Jwt__AccessTokenMinutes` | `720` | token lifetime |
| `PlatformSeed__Email` / `PlatformSeed__Password` | `admin@vibedine.local` / `ChangeMe123!` | default operator |
| `RegistrationLink__BaseUrl` | `http://localhost:3001/register` | base of generated links |
| `AllowedOrigins__0` | `http://localhost:3002` | CORS origin (the HQ frontend) |

Frontend env: `VITE_API_URL=http://localhost:5006/`.

---

## 9. How to add a feature (the pattern)

**Add a read endpoint + screen**, e.g. "list all org owners":

1. **Backend** — add a record to `DTO/Responses/PlatformResponses.cs`, add an action to the
   relevant controller (project with EF, return the DTO). Keep `[Authorize]`.
2. **Frontend** — add the TS type to `types/platform.ts`, a method to `services/api-client.ts`
   (use `authed<T>`), then a view/section that calls it in `onMounted(load)`.
3. **Verify** — rebuild the backend (`docker compose up -d --build platform-api`), then hit it:
   `curl -s :5006/api/platform/... -H "Authorization: Bearer <token>"`. Frontend hot-reloads.

**Add an HQ-owned table** → add the entity + mapping in `PlatformDbContext`, add a
`CREATE TABLE IF NOT EXISTS` to `PlatformBootstrap.cs`. **No EF migration.**

---

## 10. Not built yet / next steps

1. **Registration-link redemption** in the restaurant app: a public route that takes
   `?token=…`, validates it against `platform_registration_links` (active + not expired),
   and drives either new-org registration or add-location; mark the link `used`.
2. **Disable client self-service**: remove org/location creation from the admin app
   (`frontend-admin`) once provisioning moves entirely to HQ + links.
3. **Operator management**: CRUD for `platform_admins` (currently seed-only) + password reset.
4. **Subscription/billing** linkage from the landing page that auto-generates links on payment.
5. **Audit log** of operator actions (who created/deleted what).
6. **Refresh tokens** (currently a single long-lived access token; fine for an internal tool).

---

## 11. Gotchas

- `:5006/` is **404** — it's an API, not a page. Use `:3002` for the UI, `/swagger` for the API.
- HQ JWTs and restaurant-app JWTs are **not interchangeable** (different issuer/audience).
- Hard-deleting a location cascades to its menu/tables/orders — prefer **deactivate**;
  `DELETE` requires `?force=true` and returns 409 if related data blocks it.
- Deleting an organization is blocked (409) while it still has locations.
- "Admins" and "hires" only appear once such memberships exist; a fresh DB with only
  `OrgOwner` rows shows zeros — that's expected.
