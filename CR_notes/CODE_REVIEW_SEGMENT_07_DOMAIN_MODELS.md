# Segment 07 — Domain models & invariants: review findings

Scope review against the [Segment 07 checklist](CODE_REVIEW_ROADMAP.md#segment-07--domain-models--invariants) of [CODE_REVIEW_ROADMAP.md](CODE_REVIEW_ROADMAP.md). This segment covers the 16 `.cs` files under [backend/RestaurantApi/Models/](backend/RestaurantApi/Models/) plus the invariants the rest of the API relies on them to encode.

Reviewer: Claude (Opus 4.7). Date: 2026-05-21. Branch reviewed: `main` at `fb59c7e`.

## Verdict

**8 findings.** The model layer is in good shape on the easy items — every entity has a `Guid` primary key, every money field is `decimal(10,2)` (similarity score `decimal(5,4)`), `MembershipRole` is a proper enum, `Invitation` has the full token/status/expiry triple, and the new-product flow places items at `max + 1` so reorders don't fight insertions. What does not check out clusters around two themes:

1. **Mutation-of-tenant guard is missing.** [F1](#f1--high--savechanges-does-not-block-restaurantid-mutation-on-modified-entries) — the segment 04 🔥 invariant "forbids changing `RestaurantId` on Modified entities" has no enforcement; `StampTenant()` only runs on `Added`. Combined with public `set` accessors on every `ITenantScoped.RestaurantId`, a single buggy `entity.RestaurantId = otherTenant` save would transfer the row silently.
2. **Free-form strings carry state machines with no DB-level fence.** [F2](#f2--medium--orderstatus-paymentstatus-flashdealofferstatus-invitationstatus-are-free-form-strings-with-no-db-check-constraint), [F6](#f6--low--invitationstatus--expired-is-documented-but-never-written) — `Order.Status`, `Order.PaymentStatus`, `FlashDealOffer.Status`, `Invitation.Status` are all `text` with no `CHECK` constraint. The single allow-list (`AdminController.ActiveStatuses`) is private to one controller; the literal strings ("submitted", "preparing", "ready", "completed", "cancelled", "draft", "pending", "claimed", "expired", "accepted", "revoked") are scattered across services and controllers, and the documented set on `Invitation` doesn't match the writers.
3. **Refresh-token chain detection is absent.** [F3](#f3--medium--refreshtoken-is-missing-replacedbytoken-and-is-stored-in-plaintext) — checklist explicitly names `ReplacedByToken`; the model has only `RevokedAt`. Combined with plaintext storage, a leaked DB hands out unlimited live sessions with no forensic chain to revoke.
4. **TOCTOU on invite acceptance.** [F4](#f4--medium--invitation-acceptance-has-a-toctou-race-on-status--pending) — `AcceptInvite` reads, decides, and writes without a row lock or version column. The `Memberships` composite unique index keeps the data clean; the second concurrent accept just surfaces as an unhandled `DbUpdateException` → HTTP 500.
5. **QR token entropy sits below the checklist bar.** [F5](#f5--medium--qrtoken-is-a-guid-122-bits-checklist-asks-for-128) — `Guid.NewGuid()` produces 122 bits of effective entropy on a public, unauthenticated lookup token; checklist says ≥128.

What does check out: every entity in [backend/RestaurantApi/Models/](backend/RestaurantApi/Models/) uses `public Guid Id { get; set; } = Guid.NewGuid();` (no `int` primary keys reaching URLs); `Product.Price`, `OrderItem.UnitPriceAtPurchase`, `Order.TotalAmount`, `FlashDealOffer.DiscountPrice` all declare `[Column(TypeName = "decimal(10,2)")]`; `SimilarityRule.Score` is `decimal(5,4)`; `MembershipRole` is an enum with exactly the documented values `OrgOwner` and `RestaurantAdmin` (note: `Staff` from the checklist does not exist in code); `Invitation.Token` is `Guid` and DB-unique ([AppDbContext.cs:291](backend/RestaurantApi/Data/AppDbContext.cs#L291)); `Memberships` has a composite unique index on `(UserId, OrganizationId, RestaurantId)` ([AppDbContext.cs:241](backend/RestaurantApi/Data/AppDbContext.cs#L241)); the canonical `FlashDealOffer.Status = "expired"` transition is idempotent under a `Status != "pending"` guard ([FlashDealService.cs:275-279](backend/RestaurantApi/Services/FlashDealService.cs#L275-L279)); the kanban status endpoint validates against the `ActiveStatuses` allow-list ([AdminController.cs:368-369](backend/RestaurantApi/Controllers/AdminController.cs#L368-L369)); `ReorderProducts` is full-rewrite with a strict set-equality guard and a single `SaveChangesAsync` ([MenuManagementController.cs:534-571](backend/RestaurantApi/Controllers/MenuManagementController.cs#L534-L571)); new products land at `max + 1` so they survive concurrent inserts during a drag-reorder ([MenuManagementController.cs:242-256](backend/RestaurantApi/Controllers/MenuManagementController.cs#L242-L256)).

---

## Checklist coverage

| Item | Status | Evidence |
|---|---|---|
| Each entity has a single `Guid` primary key | ✓ Pass | Every file in [Models/](backend/RestaurantApi/Models/) declares `Guid Id { get; set; } = Guid.NewGuid();`. `AppUser` inherits `IdentityUser<Guid>` so its `Id` is `Guid` too. No `int Id` anywhere. |
| `Order.Status` / `OrderItem` flags are enums or check-constrained | ❌ Fail — see [F2](#f2--medium--orderstatus-paymentstatus-flashdealofferstatus-invitationstatus-are-free-form-strings-with-no-db-check-constraint) | `Status` and `PaymentStatus` are `text` ([InitialCreate.cs:317](backend/RestaurantApi/Migrations/20260519152754_InitialCreate.cs#L317)) with no `CHECK`. `OrderItem` flags (`WasAiRecommended`, `IsFlashDealItem`) are correctly `bool?`. |
| `FlashDealOffer.ExpiresAt` and `Status` cannot drift | ✓ Pass | The "expired" transition lives in exactly one place, behind an idempotent guard: [FlashDealService.cs:275-279](backend/RestaurantApi/Services/FlashDealService.cs#L275-L279) checks `Status != "pending"` before writing. The "claimed" and "pending" writes are also confined to `FlashDealService` ([line 79](backend/RestaurantApi/Services/FlashDealService.cs#L79), [line 237](backend/RestaurantApi/Services/FlashDealService.cs#L237)). |
| `Membership.Role` is an enum with documented set | ⚠ Partial | [MembershipRole](backend/RestaurantApi/Models/Membership.cs#L3-L10) is an enum with `OrgOwner = 0` and `RestaurantAdmin = 1`. The roadmap names a third role `Staff` that does not exist — either dead documentation or planned scope. Persisted as `int` ([AppDbContext.cs:237](backend/RestaurantApi/Data/AppDbContext.cs#L237)), so DB-level it's bounded. |
| `Invitation` has expiry + status + token; one-shot accept | ⚠ Partial — see [F4](#f4--medium--invitation-acceptance-has-a-toctou-race-on-status--pending) | Fields all present ([Invitation.cs:24-29](backend/RestaurantApi/Models/Invitation.cs#L24-L29)). Accept logic at [AuthController.cs:183-220](backend/RestaurantApi/Controllers/AuthController.cs#L183-L220) is single-statement but unprotected against concurrent calls. |
| `RefreshToken` has `RevokedAt`, `ReplacedByToken`, `ExpiresAt` | ❌ Fail — see [F3](#f3--medium--refreshtoken-is-missing-replacedbytoken-and-is-stored-in-plaintext) | `RevokedAt` and `ExpiresAt` present; `ReplacedByToken` absent in both [model](backend/RestaurantApi/Models/RefreshToken.cs) and [migration](backend/RestaurantApi/Migrations/20260519152754_InitialCreate.cs#L238-L258). Token stored plaintext via `Convert.ToBase64String(RandomNumberGenerator.GetBytes(48))` ([JwtTokenService.cs:43-48](backend/RestaurantApi/Auth/JwtTokenService.cs#L43-L48)). |
| `Product.SortOrder` / `Category.SortOrder` strategy is documented | ✓ Pass | New products: `max + 1` ([MenuManagementController.cs:242-256](backend/RestaurantApi/Controllers/MenuManagementController.cs#L242-L256)). Reorder: full-rewrite with strict set-equality validation ([MenuManagementController.cs:534-571](backend/RestaurantApi/Controllers/MenuManagementController.cs#L534-L571)). No midpoint-insertion / precision-exhaustion risk. |
| `RestaurantTable.QrToken` ≥128 bits of entropy | ❌ Fail — see [F5](#f5--medium--qrtoken-is-a-guid-122-bits-checklist-asks-for-128) | `Guid.NewGuid()` at [TablesController.cs:57](backend/RestaurantApi/Controllers/TablesController.cs#L57) and [TablesController.cs:74](backend/RestaurantApi/Controllers/TablesController.cs#L74). RFC 4122 v4 = 122 effective bits (6 reserved). CSPRNG-backed in .NET 8, but below the checklist threshold. |
| No entity's setters allow mutating `RestaurantId` post-create | ❌ Fail — see [F1](#f1--high--savechanges-does-not-block-restaurantid-mutation-on-modified-entries) | Every `ITenantScoped` implementer declares `public Guid RestaurantId { get; set; }` (mutable). The only defense in this segment's surface is `AppDbContext.StampTenant()`, which acts on `Added` only ([AppDbContext.cs:323-332](backend/RestaurantApi/Data/AppDbContext.cs#L323-L332)). |
| Money fields are `decimal`, never `double` / `float` | ✓ Pass | `Product.Price`, `OrderItem.UnitPriceAtPurchase`, `Order.TotalAmount`, `FlashDealOffer.DiscountPrice` are all `decimal` with `[Column(TypeName = "decimal(10,2)")]`. `SimilarityRule.Score` is `decimal(5,4)`. No `double` or `float` in [Models/](backend/RestaurantApi/Models/). |

---

## F1 — HIGH — `SaveChanges` does not block `RestaurantId` mutation on Modified entries

The segment 04 🔥 invariant (cross-referenced from segment 07) reads:

> `SaveChangesAsync` override stamps `RestaurantId` on **Added** entities and **forbids changing** it on **Modified** entities (a tenant transfer is a bug, not a feature).

Only the first half is implemented. From [AppDbContext.cs:311-332](backend/RestaurantApi/Data/AppDbContext.cs#L311-L332):

```csharp
public override int SaveChanges(bool acceptAllChangesOnSuccess)
{
    StampTenant();
    return base.SaveChanges(acceptAllChangesOnSuccess);
}

public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
{
    StampTenant();
    return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
}

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

`StampTenant` returns silently on `EntityState.Modified`. Combined with the model layer — every `ITenantScoped` entity exposes a public mutable `RestaurantId` setter, e.g. [OrderItem.cs:8](backend/RestaurantApi/Models/OrderItem.cs#L8), [Order.cs:9](backend/RestaurantApi/Models/Order.cs#L9), [Product.cs:9](backend/RestaurantApi/Models/Product.cs#L9), [FlashDealOffer.cs:7](backend/RestaurantApi/Models/FlashDealOffer.cs#L7) — there is no defense against a tracked entity having its `RestaurantId` reassigned and saved.

Why this matters even though no controller does it today:

- The Read-side `HasQueryFilter` registrations ([AppDbContext.cs:299-307](backend/RestaurantApi/Data/AppDbContext.cs#L299-L307)) prevent a tenant from *reading* another tenant's rows. They do not prevent a Write that mutates `RestaurantId`. The middleware loads the row from tenant A's perspective; the controller (or a service down the stack) reassigns the field; the save commits the row under tenant B; tenant A loses the row from their queries; tenant B now has it.
- This is exactly the bug-class the segment 04 🔥 line is asking the model layer to make structurally impossible.

Minimal defense (out of scope for this review, but for follow-up): in `StampTenant`, walk Modified entries and check `entry.Property(nameof(ITenantScoped.RestaurantId)).IsModified` — if true, throw `InvalidOperationException`. Or migrate `RestaurantId` to an `init`-only setter and rely on the EF Core 7+ shadow-property pattern for inserts.

---

## F2 — MEDIUM — `Order.Status`, `PaymentStatus`, `FlashDealOffer.Status`, `Invitation.Status` are free-form strings with no DB check constraint

The checklist:

> `Order.Status` and `OrderItem` flags are enums, not strings — or, if strings, validated at DB-level via check constraints.

`OrderItem` flags pass (booleans). The four `Status` strings do not.

Migrations declare each as plain text. From [InitialCreate.cs:316-318](backend/RestaurantApi/Migrations/20260519152754_InitialCreate.cs#L316-L318):

```csharp
status = table.Column<string>(type: "text", nullable: false),
payment_status = table.Column<string>(type: "text", nullable: true),
```

No `CHECK` constraint, no `HasConversion` to a constrained type, no shared constants class. Writers are scattered:

| File:Line | Field | Literal |
|---|---|---|
| [Order.cs:15](backend/RestaurantApi/Models/Order.cs#L15) | `Status` | `"draft"` |
| [Order.cs:17](backend/RestaurantApi/Models/Order.cs#L17) | `PaymentStatus` | `"unpaid"` |
| [CustomerController.cs:48](backend/RestaurantApi/Controllers/CustomerController.cs#L48) | `Status` | `"submitted"` |
| [CustomerController.cs:95-96](backend/RestaurantApi/Controllers/CustomerController.cs#L95-L96) | `Status`, `PaymentStatus` | `"draft"`, `"unpaid"` |
| [AdminController.cs:380](backend/RestaurantApi/Controllers/AdminController.cs#L380) | `Status` | `newStatus` (validated against `ActiveStatuses`) |
| [OrderFinalizationService.cs:83](backend/RestaurantApi/Services/OrderFinalizationService.cs#L83) | `Status` | `"completed"` |
| [FlashDealService.cs:79](backend/RestaurantApi/Services/FlashDealService.cs#L79) | `FlashDealOffer.Status` | `"claimed"` |
| [FlashDealService.cs:237](backend/RestaurantApi/Services/FlashDealService.cs#L237) | `FlashDealOffer.Status` | `"pending"` |
| [FlashDealService.cs:278](backend/RestaurantApi/Services/FlashDealService.cs#L278) | `FlashDealOffer.Status` | `"expired"` |
| [AuthController.cs:215](backend/RestaurantApi/Controllers/AuthController.cs#L215) | `Invitation.Status` | `"accepted"` |
| [OrganizationsController.cs:183](backend/RestaurantApi/Controllers/OrganizationsController.cs#L183) | `Invitation.Status` | `"revoked"` |

Reads compare against scattered literals too — [AdminController.cs:253-254](backend/RestaurantApi/Controllers/AdminController.cs#L253-L254) for flash deals, [AdminController.cs:282](backend/RestaurantApi/Controllers/AdminController.cs#L282) for `Order.Status == "cancelled"` (note: this status is **read** but no writer in the codebase produces it — see [F6](#f6--low--invitationstatus--expired-is-documented-but-never-written) for the parallel issue on `Invitation`).

The one allow-list lives in [AdminController.cs:29-32](backend/RestaurantApi/Controllers/AdminController.cs#L29-L32) and is private to that class:

```csharp
private static readonly HashSet<string> ActiveStatuses = new(StringComparer.OrdinalIgnoreCase)
{
    "submitted", "preparing", "ready"
};
```

It enforces the kanban write-side at [AdminController.cs:368-369](backend/RestaurantApi/Controllers/AdminController.cs#L368-L369), but nothing prevents any other code path from writing `Status = "totally-made-up"`.

A C# enum + `HasConversion<string>()` (or a `CHECK status IN (...)` constraint per migration) would close this. The current state is "any new contributor needs to grep the whole repo to learn the legal status set" — the kind of invariant the segment is asking the *model* to encode.

---

## F3 — MEDIUM — `RefreshToken` is missing `ReplacedByToken` and is stored in plaintext

Checklist:

> `RefreshToken` has `RevokedAt`, `ReplacedByToken` (for chain detection), `ExpiresAt`.

Two of three. From [RefreshToken.cs](backend/RestaurantApi/Models/RefreshToken.cs):

```csharp
public class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public AppUser? User { get; set; }
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RevokedAt { get; set; }
    public bool IsActive => RevokedAt is null && DateTime.UtcNow < ExpiresAt;
}
```

No `ReplacedByToken` (or `ReplacedByTokenId`), and the migration confirms it ([InitialCreate.cs:238-258](backend/RestaurantApi/Migrations/20260519152754_InitialCreate.cs#L238-L258) — six columns, none of them a chain pointer).

The refresh endpoint at [AuthController.cs:149-167](backend/RestaurantApi/Controllers/AuthController.cs#L149-L167) is a no-chain rotate:

```csharp
[HttpPost("refresh")]
public async Task<IActionResult> Refresh([FromBody] RefreshRequest request)
{
    var stored = await _db.RefreshTokens
        .FirstOrDefaultAsync(t => t.Token == request.RefreshToken);

    if (stored == null || !stored.IsActive)
        return Unauthorized(new { message = "Invalid or expired refresh token." });

    var user = await _users.FindByIdAsync(stored.UserId.ToString());
    if (user == null)
        return Unauthorized(new { message = "Account no longer exists." });

    // Rotate: revoke the used token, issue a fresh pair.
    stored.RevokedAt = DateTime.UtcNow;
    await _db.SaveChangesAsync();

    return Ok(await BuildAuthResponseAsync(user));
}
```

Reuse of a revoked token returns 401 (good), but does not revoke the user's other live tokens (the standard mitigation for a stolen refresh: detect the reuse, conclude theft, kill the whole family). With no `ReplacedByToken`, the server can't even tell *which* family was compromised.

Plaintext storage is the second half. [JwtTokenService.cs:43-48](backend/RestaurantApi/Auth/JwtTokenService.cs#L43-L48):

```csharp
public RefreshToken CreateRefreshToken(Guid userId) => new()
{
    UserId = userId,
    Token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)),
    ExpiresAt = DateTime.UtcNow.AddDays(_opts.RefreshTokenDays),
};
```

48 bytes of CSPRNG output is ample entropy (384 bits). But the resulting token lands in the DB as-is and is looked up by equality on the plaintext value. If the DB ever leaks (backup, replica, ad-hoc dump), every active session is a session-takeover until the natural 30-day expiry.

This is a Segment 05 🔥 finding pre-flagged in the roadmap as well — flagging again here because the *model* is where the structural change has to land.

---

## F4 — MEDIUM — Invitation acceptance has a TOCTOU race on `Status == "pending"`

The accept handler at [AuthController.cs:183-220](backend/RestaurantApi/Controllers/AuthController.cs#L183-L220):

```csharp
[Authorize]
[HttpPost("accept-invite")]
public async Task<IActionResult> AcceptInvite([FromBody] AcceptInviteRequest request)
{
    var user = await _users.GetUserAsync(User);
    if (user == null) return Unauthorized();

    var invite = await _db.Invitations.FirstOrDefaultAsync(i => i.Token == request.Token);
    if (invite == null) return NotFound(new { message = "Invitation not found." });

    if (invite.Status != "pending" || invite.ExpiresAt < DateTime.UtcNow)
        return BadRequest(new { message = "This invitation is no longer valid." });

    if (!string.Equals(invite.Email, user.Email, StringComparison.OrdinalIgnoreCase))
        return Forbid();

    var already = await _db.Memberships.AnyAsync(m =>
        m.UserId == user.Id &&
        m.OrganizationId == invite.OrganizationId &&
        m.RestaurantId == invite.RestaurantId);

    if (!already)
    {
        _db.Memberships.Add(new Membership { ... });
    }

    invite.Status = "accepted";
    await _db.SaveChangesAsync();
    ...
}
```

Read-decide-write with no row lock and no `Version` / `ConcurrencyToken`. Two concurrent POSTs against the same token both observe `Status == "pending"`, both observe `AnyAsync == false`, both try to insert a Membership. The composite unique index on `(UserId, OrganizationId, RestaurantId)` at [AppDbContext.cs:241](backend/RestaurantApi/Data/AppDbContext.cs#L241) saves the data — one insert wins, the other throws `DbUpdateException`. But that exception is uncaught: the second call returns HTTP 500 instead of the clean 400 the design implies.

Practical blast radius is small (the loser sees a 500 once; the invite ends up `Status = "accepted"`; subsequent retries hit the `Status != "pending"` guard and 400 cleanly). The reason it's worth listing under domain invariants: the checklist asks the *model* to make one-shot acceptance structural, and the model offers no concurrency control field (`xmin` / `RowVersion` / etc.). The unique index does enforce the *outcome* (no duplicate membership); it does not enforce the *protocol* (clean response on the loser).

Either a `BEGIN TRANSACTION ... SELECT ... FOR UPDATE` or an `xmin`-based concurrency token on `Invitation`, paired with catching `DbUpdateConcurrencyException` and returning 400, would fix it.

---

## F5 — MEDIUM — `QrToken` is a `Guid` (122 bits) — checklist asks for 128

[RestaurantTable.cs:10](backend/RestaurantApi/Models/RestaurantTable.cs#L10):

```csharp
public Guid? QrToken { get; set; }
```

Generated at insert and rotation by `Guid.NewGuid()`:

- [TablesController.cs:57](backend/RestaurantApi/Controllers/TablesController.cs#L57) (create)
- [TablesController.cs:74](backend/RestaurantApi/Controllers/TablesController.cs#L74) (regenerate — confirms the rotation endpoint exists; deals with admin-triggered invalidation)

`Guid.NewGuid()` produces RFC 4122 v4 GUIDs: 128 bits total, of which 6 are reserved for version (4 bits) and variant (2 bits), leaving **122 bits of effective entropy**. On .NET 8 the underlying source is the OS CSPRNG (`BCryptGenRandom` / `getrandom`), so the entropy *is* cryptographic — it's just 122 bits, not the ≥128 the checklist asks for.

This is the QR token, used as the sole lookup key for unauthenticated customer entry — see [TenantResolutionMiddleware.cs:75](backend/RestaurantApi/Tenancy/TenantResolutionMiddleware.cs#L75) and [CustomerController.cs:221](backend/RestaurantApi/Controllers/CustomerController.cs#L221). A token attacker who can hit the customer routes anonymously has ~2^122 effective search space, which is well past the "you can't brute it" line in practice; the real concern is the standard against which the checklist was written. To meet the ≥128 bar, replace with `RandomNumberGenerator.GetBytes(16)` (Base64Url-encoded) or a 32-byte version for headroom, and migrate `QrToken` to `string`. Until then, mark as a known divergence.

The seed at [SeedData.cs:283-302](backend/RestaurantApi/Data/SeedData.cs#L283-L302) uses fixed `aaaaaaaa-0000-...` GUIDs — fine for dev (matches `.env.example`-style well-known tokens) but worth a comment that these would never reach prod.

---

## F6 — LOW — `Invitation.Status = "expired"` is documented but never written

[Invitation.cs:26](backend/RestaurantApi/Models/Invitation.cs#L26) documents the status set:

```csharp
/// <summary>pending | accepted | revoked | expired</summary>
public string Status { get; set; } = "pending";
```

Grep for `Invitation` status writes finds only three transitions in the entire backend:

- `"pending"` — the model default
- `"accepted"` — [AuthController.cs:215](backend/RestaurantApi/Controllers/AuthController.cs#L215)
- `"revoked"` — [OrganizationsController.cs:183](backend/RestaurantApi/Controllers/OrganizationsController.cs#L183)

Nothing writes `"expired"`. Expiry is handled implicitly by the `invite.ExpiresAt < DateTime.UtcNow` short-circuit in `AcceptInvite` ([AuthController.cs:192](backend/RestaurantApi/Controllers/AuthController.cs#L192)), which means an expired invitation row still reads `Status = "pending"`. The admin invitations list (if one exists, or when one is added) will display these as pending instead of expired. Same effect: there is no background reaper that transitions `pending → expired` after `ExpiresAt`.

Low because it's a UX/inventory issue, not a correctness/security one (the gate at accept-time still rejects expired invites). Worth either (a) deleting the `expired` mention from the docstring, or (b) adding a tiny hosted service / Read-side projection that synthesizes the effective status.

Parallel observation: [AdminController.cs:282](backend/RestaurantApi/Controllers/AdminController.cs#L282) reads `Order.Status == "cancelled"` but **no writer** in the codebase produces that literal — the only allowed transitions in the kanban (`ActiveStatuses`) are submitted/preparing/ready, and `OrderFinalizationService` writes "completed". Either there's an unimplemented "cancel order" admin action, or this branch is dead. Worth tracing.

---

## F7 — LOW — `OrderItem.RestaurantId` and parent `Order.RestaurantId` are not structurally tied

Both entities implement `ITenantScoped`. Both `RestaurantId` columns are stamped independently by `StampTenant()` from the same `_tenant.RestaurantId` value, so in normal flow they match. But the schema does not *require* them to match:

- The FK on `OrderItem.OrderId` ([AppDbContext.cs:116-118](backend/RestaurantApi/Data/AppDbContext.cs#L116-L118)) targets `Order.Id` only — there's no composite `(OrderId, RestaurantId)` FK that pins the item to its parent's tenant.
- There's no `CHECK` constraint at the DB level pinning `OrderItem.RestaurantId = Order.RestaurantId`.

In a system without [F1](#f1--high--savechanges-does-not-block-restaurantid-mutation-on-modified-entries), this is harmless — both rows get stamped with the same tenant on insert, end of story. Stacked on top of F1, however: any mutation of `Order.RestaurantId` would orphan its `OrderItem` rows under the original tenant, or vice-versa. The query filter would then return half the order to one tenant and half to another (the items get hidden under one filter, the order under another), depending on which side of the cross-tenant transfer mutated.

Cheap structural fix: composite FK `(RestaurantId, OrderId) → Order(RestaurantId, Id)`. Same pattern would be useful for `FlashDealOffer.OrderId → Order.Id` and `OrderItem.ProductId → Product.Id`. Listed as LOW because F1 is the upstream — if F1 is fixed, this becomes a defense-in-depth nicety rather than a real exposure.

---

## F8 — LOW — `MembershipRole.Staff` named in the roadmap does not exist in code

The checklist:

> `Membership.Role` is an enum with a documented set: `OrgOwner`, `RestaurantAdmin`, `Staff`. Anything else is a bug.

Code:

```csharp
public enum MembershipRole
{
    OrgOwner = 0,
    RestaurantAdmin = 1
}
```

[Membership.cs:3-10](backend/RestaurantApi/Models/Membership.cs#L3-L10) — only two values, persisted as `int` via `HasConversion<int>()` ([AppDbContext.cs:237](backend/RestaurantApi/Data/AppDbContext.cs#L237)).

This is a documentation/scope discrepancy, not a defect — the roadmap was written ahead of the model. Either the roadmap should be tightened to "`OrgOwner`, `RestaurantAdmin`" or `Staff` needs to be added (probably as `Staff = 2`) to match a future role. Worth a one-line decision and either an edit to the roadmap or a follow-up ticket.

---

## Items deferred to later segments

- **Refresh-token hashing & chain detection ([F3](#f3--medium--refreshtoken-is-missing-replacedbytoken-and-is-stored-in-plaintext))** is restated and owned by **Segment 05 — Authentication & token lifecycle** (already reviewed; the model-side gap matches the auth-side gap, both flagged consistently).
- **`SaveChanges` modification guard ([F1](#f1--high--savechanges-does-not-block-restaurantid-mutation-on-modified-entries))** belongs to the **Segment 04 — Multi-tenancy enforcement** 🔥 list; this segment's job is to confirm the model layer hasn't given the writer any *additional* surface area, which it has not — the gap is solely in `StampTenant`'s missing Modified case.
- **`Order.Status = "cancelled"` writer absent** (noted in [F6](#f6--low--invitationstatus--expired-is-documented-but-never-written)) is a controllers-layer question for **Segment 08 — Controllers — admin surface**.

---

## Sign-off

🔥 items in scope: none in segment 07's own list; the cross-cuts from segments 04 and 05 are flagged above.

Strengths to preserve:
- The model's discipline on money (`decimal(10,2)` everywhere), keys (`Guid` everywhere), and tenant marker interface (`ITenantScoped`).
- `FlashDealOffer`'s status transition is genuinely idempotent — the kind of guard the rest of the codebase should imitate.
- `ReorderProducts`' set-equality validation + atomic `0..N` rewrite is exactly the right shape for SortOrder.
- `Invitation.Token` is unique at the DB level; `Memberships` has the composite unique key that absorbs the F4 race.

Top three to land first:
1. Add the `Modified`-entry guard in `StampTenant` ([F1](#f1--high--savechanges-does-not-block-restaurantid-mutation-on-modified-entries)). Two lines of code, closes a tenant-leak primitive.
2. Hash refresh tokens and add `ReplacedByToken` ([F3](#f3--medium--refreshtoken-is-missing-replacedbytoken-and-is-stored-in-plaintext)). Needs a migration and is the highest-leverage security delta in this branch.
3. Turn the four status strings into either C# enums with `HasConversion<string>()` or DB `CHECK` constraints ([F2](#f2--medium--orderstatus-paymentstatus-flashdealofferstatus-invitationstatus-are-free-form-strings-with-no-db-check-constraint)). Pick one — both close the gap; the enum is friendlier to future contributors.
