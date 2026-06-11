# Segment 04 — Multi-tenancy enforcement: review findings

Scope review against the [Segment 04 checklist](CODE_REVIEW_ROADMAP.md#segment-04--multi-tenancy-enforcement-) of [CODE_REVIEW_ROADMAP.md](CODE_REVIEW_ROADMAP.md). This segment is the highest-risk surface in the backend; the roadmap marks several items 🔥.

Reviewer: Claude (Opus 4.7). Date: 2026-05-21. Branch reviewed: `main` at `fb59c7e`.

## Verdict

**5 findings.** One high-severity gap directly contradicts a 🔥 checklist item ([F1](#f1--high--savechanges-does-not-forbid-restaurantid-changes-on-modified-entities-)). Two medium-severity gaps weaken defense-in-depth ([F2](#f2--medium--stamptenant-silently-accepts-mismatched-restaurantid-on-added-entities), [F3](#f3--medium--qrtoken-resolution-branch-does-not-reject-inactive-restaurants)). Two are documentation/defensiveness nits ([F4](#f4--low--missing-justifying-comment-on-ignorequeryfilters-in-submitorder), [F5](#f5--low--invitation-carries-restaurantid-but-is-deliberately-not-itenantscoped--undocumented)).

The marker interface coverage, the `HasQueryFilter` registrations, and the fail-closed behavior of the query filter when the tenant is unresolved all check out.

---

## Checklist coverage

| Item | Status | Evidence |
|---|---|---|
| Every tenant-owned entity implements `ITenantScoped` | ⚠ Partial — see [F5](#f5--low--invitation-carries-restaurantid-but-is-deliberately-not-itenantscoped--undocumented) | 9 of the 10 expected entities implement it; `Invitation` does not. |
| `AppDbContext.OnModelCreating` registers `HasQueryFilter` for every `ITenantScoped` entity | ✓ Pass | [AppDbContext.cs:299-307](backend/RestaurantApi/Data/AppDbContext.cs#L299-L307) covers all 9 implementers. |
| `SaveChangesAsync` stamps `RestaurantId` on Added entities | ⚠ Partial — see [F2](#f2--medium--stamptenant-silently-accepts-mismatched-restaurantid-on-added-entities) | Stamp only fires when `RestaurantId == Guid.Empty`. |
| `SaveChangesAsync` forbids changing `RestaurantId` on Modified entities | ❌ Fail — see [F1](#f1--high--savechanges-does-not-forbid-restaurantid-changes-on-modified-entities-) | No `Modified` branch in [StampTenant](backend/RestaurantApi/Data/AppDbContext.cs#L323-L332). |
| `SuppressResolution()` call sites are bounded and justified | ✓ Pass | 3 sites, all justified — see [Bypass audit](#bypass-audit). |
| Middleware reads `restaurantId` or `qrToken` with documented precedence | ⚠ Partial — see [F3](#f3--medium--qrtoken-resolution-branch-does-not-reject-inactive-restaurants) | Precedence documented; `qrToken` branch missing `IsActive` check. |
| Membership check enforced in middleware + at controllers | ✓ Pass in practice | Middleware check covers authenticated callers; admin controllers all carry `[Authorize]`. Caveat in [F2](#f2--medium--stamptenant-silently-accepts-mismatched-restaurantid-on-added-entities)'s discussion. |
| Anonymous QR-token requests cannot reach admin endpoints | ✓ Pass | All `/admin/*` routes carry `[Authorize]` ([grep](#routes-and-authorize-coverage)). |
| `IgnoreQueryFilters()` bypasses are justified | ⚠ Partial — see [F4](#f4--low--missing-justifying-comment-on-ignorequeryfilters-in-submitorder) | One call site, lacks a comment. |
| When tenant is unresolved, the filter returns zero rows | ✓ Pass | Filter compares `x.RestaurantId == null`; all tenant columns are non-nullable, so zero rows. Fail-closed. |

---

## F1 — HIGH — `SaveChanges` does not forbid `RestaurantId` changes on Modified entities 🔥

The roadmap is explicit (Segment 04, 🔥 checklist item):
> `SaveChangesAsync` override stamps `RestaurantId` on **Added** entities **and forbids changing it** on **Modified** entities (a tenant transfer is a bug, not a feature).

The current implementation only handles `Added`:

[backend/RestaurantApi/Data/AppDbContext.cs:323-332](backend/RestaurantApi/Data/AppDbContext.cs#L323-L332)

```csharp
private void StampTenant()
{
    if (_tenant.RestaurantId is not { } rid) return;

    foreach (var entry in ChangeTracker.Entries<ITenantScoped>())
    {
        if (entry.State == EntityState.Added && entry.Entity.RestaurantId == Guid.Empty)
            entry.Entity.RestaurantId = rid;
    }
}
```

A grep confirms no other `EntityState.Modified` enforcement exists in the project — the loop above is the only `EntityState.*` check in the backend.

**Impact.** If any code path (controller bug, future refactor, or in-progress flow under `SuppressResolution`) mutates `entity.RestaurantId` on a tracked entity, `SaveChanges` accepts it silently. The global query filter normally prevents loading another tenant's row, but:

- During a suppressed block (background services, seeding), the filter is off — a single line like `entity.RestaurantId = otherRid` would persist a cross-tenant transfer.
- An entity already in scope is reloadable by ID; subsequent code that mutates its `RestaurantId` would commit the change without any guard.

**Recommended fix.** Add a `Modified` branch that throws when `entry.Property(nameof(ITenantScoped.RestaurantId)).IsModified` and the original/current values differ. See also [F2](#f2--medium--stamptenant-silently-accepts-mismatched-restaurantid-on-added-entities) for the Added-side complement.

---

## F2 — MEDIUM — `StampTenant` silently accepts mismatched `RestaurantId` on Added entities

[backend/RestaurantApi/Data/AppDbContext.cs:323-332](backend/RestaurantApi/Data/AppDbContext.cs#L323-L332) stamps the ambient tenant only when the entity's `RestaurantId` is `Guid.Empty`. If a caller (controller, service) sets a different `RestaurantId` explicitly — by intent, by bug, or by binding from untrusted input — the stamp is a no-op and the entity persists under the wrong tenant.

```csharp
if (entry.State == EntityState.Added && entry.Entity.RestaurantId == Guid.Empty)
    entry.Entity.RestaurantId = rid;
// else: entity keeps whatever caller set, including a foreign tenant id
```

This is the Added-side companion to [F1](#f1--high--savechanges-does-not-forbid-restaurantid-changes-on-modified-entities-). The fail-open posture trusts every caller to either leave `RestaurantId` empty or set it to the ambient value — there is no enforcement.

**Why this matters now.** The codebase's mitigation today is "controllers must not bind `RestaurantId` from request bodies" (Segment 08 checklist item). That's a per-controller invariant with no centralized enforcement, and DTO leakage is exactly the kind of regression a future change can introduce without tripping any test.

**Recommended fix.** In the `Added` branch, after the empty-id stamp, reject mismatched ids:

```csharp
if (entry.Entity.RestaurantId != Guid.Empty && entry.Entity.RestaurantId != rid)
    throw new InvalidOperationException("ITenantScoped entity added with mismatched RestaurantId.");
```

Skip the throw when `IsResolutionSuppressed` so seeding/background flows continue to work.

---

## F3 — MEDIUM — `qrToken` resolution branch does not reject inactive restaurants

The middleware's two resolution branches handle `IsActive` asymmetrically:

[backend/RestaurantApi/Tenancy/TenantResolutionMiddleware.cs:32-68](backend/RestaurantApi/Tenancy/TenantResolutionMiddleware.cs#L32-L68) (restaurantId branch) returns `403` when `!restaurant.IsActive`:

```csharp
if (!restaurant.IsActive)
{
    ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
    await ctx.Response.WriteAsJsonAsync(new { message = "This restaurant is not active." });
    return;
}
```

[backend/RestaurantApi/Tenancy/TenantResolutionMiddleware.cs:69-80](backend/RestaurantApi/Tenancy/TenantResolutionMiddleware.cs#L69-L80) (qrToken branch) does no such check:

```csharp
var table = await db.Tables
    .FirstOrDefaultAsync(t => t.QrToken == qrToken);

if (table is not null)
    tenant.SetRestaurant(table.RestaurantId);
```

**Impact.** A customer scanning a QR code for a deactivated restaurant is admitted and the tenant is set. Whether downstream controllers reject the request depends entirely on whether each customer endpoint re-checks `IsActive` — none do today. Net effect: a deactivated restaurant remains operationally reachable through the customer surface.

**Recommended fix.** Join `Tables` to `Restaurants` in the lookup (or fetch the restaurant after the table) and apply the same `IsActive`/`404`/`403` logic as the restaurantId branch.

---

## F4 — LOW — Missing justifying comment on `IgnoreQueryFilters` in `SubmitOrder`

Roadmap checklist item:
> Query filters can be bypassed with `IgnoreQueryFilters()` — grep the codebase. **Each call needs justification.**

There is exactly one bypass site:

[backend/RestaurantApi/Controllers/CustomerController.cs:50-58](backend/RestaurantApi/Controllers/CustomerController.cs#L50-L58)

```csharp
// Per-restaurant sequence. The (RestaurantId, OrderNumber) index isn't unique
// because a race between two concurrent submits could collide here; for the
// single-host dev setup this is fine, and a unique index would convert a
// collision into a 500 with no easy retry path.
var lastNumber = await _context.Orders
    .IgnoreQueryFilters()
    .Where(o => o.RestaurantId == order.RestaurantId && o.OrderNumber != null)
    .MaxAsync(o => (int?)o.OrderNumber) ?? 0;
```

The comment explains the missing unique index, not the filter bypass. Since `order.RestaurantId` matches the ambient tenant at this point in the flow, the bypass is functionally redundant — the explicit `Where` clause does the same scoping the filter would. The roadmap rule still applies: either remove the `IgnoreQueryFilters()` call, or add a one-line comment explaining why it is needed (e.g., a defensive guard against a future tenant-shift code path). As written, a reader can't tell whether the bypass is load-bearing.

---

## F5 — LOW — `Invitation` carries `RestaurantId` but is deliberately not `ITenantScoped` — undocumented

The roadmap's expected `ITenantScoped` list includes `Invitation`:
> `Product`, `Category`, `Order`, `OrderItem`, `RestaurantTable`, `AIDirective`, `RecommendationDirective`, `SimilarityRules`, `FlashDealOffer`, `Invitation`.

Actual implementers (`grep "class .* : ITenantScoped"`): the first nine — `Invitation` is absent.

[backend/RestaurantApi/Models/Invitation.cs:9-34](backend/RestaurantApi/Models/Invitation.cs#L9-L34)

```csharp
public class Invitation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = string.Empty;
    public Guid OrganizationId { get; set; }
    public Guid? RestaurantId { get; set; }   // nullable — org-level invites carry no restaurant
    ...
}
```

`RestaurantId` is nullable here because OrgOwner invitations are org-scoped, not restaurant-scoped. The accept-invite flow at [AuthController.cs:189](backend/RestaurantApi/Controllers/AuthController.cs#L189) queries by `Token` — before the invitee has any membership and therefore before any tenant can be resolved. Making `Invitation` `ITenantScoped` would break that flow (the filter would yield zero rows for org-level invites, and the accept path has no ambient tenant anyway).

**So the omission is intentional**, but two things make it worth a small change:

1. The roadmap explicitly expects `Invitation` on the list, so the next reviewer will hit the same surprise.
2. There is no code comment on the class explaining why it is *not* tenant-scoped despite holding `RestaurantId`. Other tenancy decisions ([Membership.cs:12-16](backend/RestaurantApi/Models/Membership.cs#L12-L16)) carry a doc comment that explains the nullable design; `Invitation` should too.

**Recommended fix.** Add a one-line XML doc on `Invitation` (and on the roadmap) stating that the entity is deliberately *not* `ITenantScoped` because it is queried by `Token` across tenants during the accept flow.

---

## Appendix

### Bypass audit

`SuppressResolution()` call sites (3 outside the interface/impl):

| Site | Justification | OK? |
|---|---|---|
| [TenantResolutionMiddleware.cs:35](backend/RestaurantApi/Tenancy/TenantResolutionMiddleware.cs#L35) | Restaurant lookup before tenant is known. | ✓ self-evident |
| [TenantResolutionMiddleware.cs:72](backend/RestaurantApi/Tenancy/TenantResolutionMiddleware.cs#L72) | Table-by-QR-token lookup before tenant is known. | ✓ self-evident |
| [OrderFinalizationService.cs:65-67](backend/RestaurantApi/Services/OrderFinalizationService.cs#L65-L67) | Background service with no request → tenant must be suppressed; commented in code. | ✓ |

`IgnoreQueryFilters()` call sites (1):

| Site | Justification | OK? |
|---|---|---|
| [CustomerController.cs:55](backend/RestaurantApi/Controllers/CustomerController.cs#L55) | None. See [F4](#f4--low--missing-justifying-comment-on-ignorequeryfilters-in-submitorder). | ⚠ |

### `ITenantScoped` implementers vs. roadmap expectation

| Entity | Implements `ITenantScoped`? | `HasQueryFilter`? |
|---|---|---|
| Category | ✓ | ✓ |
| Product | ✓ | ✓ |
| Order | ✓ | ✓ |
| OrderItem | ✓ | ✓ |
| RestaurantTable | ✓ | ✓ |
| FlashDealOffer | ✓ | ✓ |
| AIDirective | ✓ | ✓ |
| RecommendationDirective | ✓ | ✓ |
| SimilarityRule | ✓ | ✓ |
| Invitation | ✗ — see [F5](#f5--low--invitation-carries-restaurantid-but-is-deliberately-not-itenantscoped--undocumented) | ✗ |
| Membership | ✗ (intentional — cross-tenant by design) | ✗ |
| Restaurant, Organization, RefreshToken, AppUser | ✗ (not tenant-scoped data) | ✗ |

### Routes and `[Authorize]` coverage

From `grep` over [backend/RestaurantApi/Controllers/](backend/RestaurantApi/Controllers/):

| Controller | Route | `[Authorize]`? |
|---|---|---|
| AdminController | `api/restaurants/{restaurantId:guid}/admin` | ✓ |
| MenuManagementController | `api/restaurants/{restaurantId:guid}/admin/menu` | ✓ |
| MenuScanController | `api/restaurants/{restaurantId:guid}/admin/menu/scan` | ✓ |
| TablesController | `api/restaurants/{restaurantId:guid}/admin/tables` | ✓ |
| OrganizationsController | `api/organizations` | ✓ |
| MenuController | `api/restaurants/{restaurantId:guid}/menu` | (intentionally anonymous — public menu) |
| CustomerController | `api` | (mix of anonymous + qr-token endpoints) |
| AuthController | `api/auth` | (anonymous by design) |

No admin route is missing `[Authorize]`. The customer surface is public by design.

### Follow-ups suggested by Segment 04 but out of scope here

- The roadmap's proposed integration test (R1/R2 with overlapping product names, authenticate as R1's admin, hit every admin endpoint with R2's id, none succeed) does not exist in [backend/RestaurantApi.Tests/](backend/RestaurantApi.Tests/). File as `[REV-04-T1]`.
- Segment 08 should re-check controller DTO binding for any direct `RestaurantId` bind — [F2](#f2--medium--stamptenant-silently-accepts-mismatched-restaurantid-on-added-entities)'s mitigation depends on it.
- Segment 03 should confirm the middleware order — `TenantResolutionMiddleware` must run after `UseAuthentication`/`UseAuthorization` so that `ctx.User?.Identity?.IsAuthenticated` is populated by the time the membership check runs.

---

*Generated as part of the [CODE_REVIEW_ROADMAP.md](CODE_REVIEW_ROADMAP.md) execution. No project code was modified during this review.*
