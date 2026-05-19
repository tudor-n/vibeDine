# Multi-Tenant Refactor Plan

> Status: **Backend phases 0–4 + Frontend phases 5–6 implemented** (phase 7 verification pending)
> Goal: turn the current single-restaurant app into a multi-tenant platform where an
> Organization (restaurant chain) owns one or more Restaurants (locations).

---

## 1. Current state (single-tenant)

- **Backend:** ASP.NET Core 8 + EF Core + PostgreSQL + SignalR. **No auth at all.**
  Core tables (`categories`, `products`, `tables`, `orders`, `order_items`,
  `flash_deal_offers`, `ai_directives`, `recommendation_directives`,
  `similarity_rules`) have **no tenant scoping** — one implicit restaurant everywhere.
- **AI microservice:** Python FastAPI (menu extraction, directives parsing, recommendations) — stateless per call.
- **frontend-admin:** Vue 3 dashboard, no login, hardcoded `"Andrei C."` / `"sala 1"`.
- **frontend-customer:** Vue 3, QR-token-per-table entry, single route, hardcoded fallback token.

## 2. Target hierarchy

```
Organization (chain)        ── created/owned by Organization Owner
   └── Restaurant (1..N)     ── implicitly owned by Org Owner; Co-owners invited per location
          ├── Tables (QR per table)   ── client side (public, no auth)
          ├── Menu / Orders / AI rules
          └── Dashboard side (authenticated)
```

Everything that exists today becomes **one Restaurant inside one Organization**.

## 3. Locked decisions

| Topic | Decision |
|---|---|
| **Auth** | ASP.NET Identity + JWT, in-house (access + refresh tokens) |
| **Menu model** | Per-restaurant independent (each location owns its own categories/products/prices) |
| **Tenant isolation** | Shared PostgreSQL DB with `organization_id` / `restaurant_id` discriminator columns + enforced EF Core global query filters |
| **Roles** | Two roles only (see below) |
| **Existing data** | Clean slate — redesign freely, reseed |

### Role model

- **Organization Owner** — creates the organization; is **automatically owner of every
  location created in that organization**. Derived from a single `OrgOwner` membership
  (no per-restaurant rows needed). Manages org settings, creates restaurants, invites co-owners.
- **Restaurant Co-owner / Admin** — invited per location; gets dashboard access to **that
  one location only**. Cannot touch org settings or other locations.
- Waitstaff stay **loginless** for now (existing SignalR group); authenticated staff
  accounts deferred to a later phase.

## 4. Target data model

### New tables

| Table | Key fields | Notes |
|---|---|---|
| `users` | id, email (unique), password_hash, full_name, created_at | ASP.NET Identity (`IdentityUser<Guid>`) |
| `organizations` | id, name, slug, created_by_user_id, created_at | The chain |
| `restaurants` | id, **organization_id (FK)**, name, slug, address, timezone, currency, is_active | A location |
| `memberships` | id, user_id, organization_id, **restaurant_id (nullable)**, role, invited_by, created_at | The RBAC table |
| `invitations` | id, email, organization_id, restaurant_id, role, token, status, expires_at, invited_by | Co-owner invite flow |

### Membership rules (role model encoded)

- `role = OrgOwner` → `restaurant_id` is **NULL** → grants owner access to the org **and
  implicitly every restaurant in it**. Created automatically when a user creates an organization.
- `role = RestaurantAdmin` → `restaurant_id` **set** → grants dashboard access to **that
  one location only**. Created when an invited co-owner accepts.

### Tenant scoping on existing tables

Add `restaurant_id` FK to: `categories`, `products`, `tables`, `orders`,
`ai_directives`, `recommendation_directives`, `similarity_rules`.
`order_items` / `flash_deal_offers` inherit scope via their parent — still add
`restaurant_id` for query safety.

### QR codes

`tables.qr_token` stays a globally-unique UUID, so the token alone resolves
table → restaurant → org. **No change to the customer URL.** Only customer-side change:
the session endpoint also returns `restaurantId` (+ branding).

## 5. Backend changes (ASP.NET Core)

1. **Identity + JWT:** Add ASP.NET Identity, `/api/auth` (register, login, refresh,
   accept-invite). Roles/tenant access are **not** static JWT claims (a user can be in
   multiple orgs/locations) — resolved per request from `memberships`.
2. **Tenant-context middleware:** Dashboard calls carry the active restaurant
   (route `/api/restaurants/{rid}/...`). Middleware verifies the user has a membership
   granting it (RestaurantAdmin for it **or** OrgOwner of its org), sets ambient `ICurrentTenant`.
3. **EF Core global query filters** keyed on `ICurrentTenant.RestaurantId` → automatic
   isolation on every scoped entity; impossible to leak across tenants by forgetting a `.Where`.
4. **Public endpoints** (`/api/menu`, table session, order create/submit) stay
   unauthenticated but resolve the tenant **from `qr_token`** and set the same ambient context.
5. **API restructure:** `/api/auth/*`, `/api/organizations/{oid}/*` (org-owner only:
   create restaurant, invite, list locations), `/api/restaurants/{rid}/*` (scoped admin + public).
6. **SignalR scoping:** namespace groups by restaurant — customer `r:{rid}:t:{tableId}`,
   staff `r:{rid}:WaitStaff`. Connection must carry restaurant context (qr_token for
   customer; JWT + selected restaurant for staff). Prevents cross-restaurant leakage.

## 6. AI microservice (Python)

Largely unchanged — stateless per call. The .NET backend already sends the data it
should operate on; we just ensure it sends **restaurant-scoped** directives/products/
history (those tables become restaurant-scoped). Add `restaurant_id` to its logs.
No tenant model needed inside FastAPI.

## 7. frontend-admin

- Add **login page**, auth store (token + refresh handling), route guards.
- On login → fetch the user's memberships → the orgs/restaurants they can access.
- **Restaurant switcher** in the nav (replaces hardcoded `"sala 1"` / `"Andrei C."`).
  Selected restaurant id injected into every `api-client` call.
- **Org-owner-only screens:** organization settings, create restaurant, invite co-owner.
  A RestaurantAdmin sees only their location's existing dashboard.
- Existing stores (menu mgmt, metrics, rules, orders) get scoped automatically via the
  api-client change.

## 8. frontend-customer

- Almost no change. Remove the hardcoded fallback token; invalid/missing token → graceful error.
- Session response now includes `restaurantId` + restaurant name/logo → optional
  per-restaurant branding.

## 9. Onboarding flow

```
Sign up
  → Create Organization (becomes OrgOwner)
  → Add Restaurant(s) (implicit owner)
  → Invite co-owner by email
  → invitee accepts link
  → RestaurantAdmin membership created
```

## 10. Phasing

| Phase | Deliverable |
|---|---|
| **0** | ✅ Finalize ERD, drop old seed |
| **1** | ✅ Identity + JWT + `/api/auth` (no tenancy yet) |
| **2** | ✅ Tenancy schema + `restaurant_id` columns + EF global filters + reseed one default org/restaurant with existing demo menu |
| **3** | ✅ Tenant-context middleware, `[Authorize]` everything, route restructure, org/invite endpoints |
| **4** | ✅ SignalR scoping |
| **5** | ✅ frontend-admin: auth store, router guards, Login/Onboarding/AcceptInvite views, restaurant switcher + logout, Organization (locations/invitations) view |
| **6** | ✅ frontend-customer: session returns restaurantId/Name, scoped api-client, scoped SignalR JoinTableGroup, removed hardcoded QR fallback (graceful error), title branding |
| **7** | ⏳ AI scoping verification, end-to-end test, final reseed |

## 11. Pending smaller decisions (recommended defaults)

1. **Dashboard URL strategy** — *default:* single login portal + in-app restaurant
   switcher (vs. per-org subdomain). Simpler, no DNS/wildcard cert work.
2. **Co-owner permissions** — *default:* full dashboard for their location (menu +
   orders + AI rules + metrics), but no org settings / other locations.
3. **Invitation delivery** — *default:* copyable invite link in Phase 5; real email
   (SMTP/SendGrid) later, so email infra doesn't block the refactor.
4. **Self-service signup** — *default:* open self-service (anyone can sign up and
   create an org).
