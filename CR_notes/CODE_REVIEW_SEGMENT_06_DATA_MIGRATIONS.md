# Segment 06 — Data layer & migrations: review findings

Scope review against the [Segment 06 checklist](CODE_REVIEW_ROADMAP.md#segment-06--data-layer--migrations) of [CODE_REVIEW_ROADMAP.md](CODE_REVIEW_ROADMAP.md). This segment covers the EF Core model, the schema produced by migrations, and the dev-only seed.

Reviewer: Claude (Opus 4.7). Date: 2026-05-21. Branch reviewed: `main` at `fb59c7e`.

## Verdict

**6 findings.** No high-severity defects: the schema is fundamentally sound — money fields are `decimal(10,2)`, timestamps are `timestamptz`, `qr_token` is globally unique, cascades preserve historical `OrderItem` rows, and no raw SQL exists in the app code. The findings cluster around two themes:

1. **Operational drift** — the dev DB carries a migration ([F1](#f1--medium--dev-database-carries-an-orphan-migration-from-a-never-merged-branch)) and the auto-`Migrate()` path silently accepts it; no playbook exists for the manual prod step ([F6](#f6--low--no-recorded-plan-for-applying-migrations-in-non-development)).
2. **Indexes/uniqueness gaps** — two `ITenantScoped` tables lack the index that the global query filter always references ([F2](#f2--medium--order_items-and-flash_deal_offers-have-no-index-on-restaurant_id-despite-the-query-filter), [F3](#f3--medium--orders-restaurant_idorder_number-is-not-unique-and-the-assigner-is-read-modify-write)); two slug/order-number uniqueness loops have no DB-level guard, leaving a TOCTOU window for races ([F3](#f3--medium--orders-restaurant_idorder_number-is-not-unique-and-the-assigner-is-read-modify-write), [F4](#f4--low--restaurantsslug-is-checked-globally-unique-by-a-toctou-loop-with-no-backing-unique-index)).

What does check out: `[Column(TypeName = "decimal(10,2)")]` / `decimal(5,4)` on every money/score field; `timestamp with time zone` everywhere; `qr_token` UNIQUE globally; `(restaurant_id, order_number)` composite index present; `OrderItem.ProductId` and `SimilarityRule.ProductA/B` `OnDelete: Restrict` to preserve history; `OrderFinalizationService`'s cross-tenant query correctly uses `SuppressResolution()`; no `FromSqlRaw` / `ExecuteSqlRaw` anywhere; no `HasData` mixed into `OnModelCreating`; seed runs only in Development behind an idempotent `Organizations.AnyAsync()` gate.

---

## Checklist coverage

| Item | Status | Evidence |
|---|---|---|
| Each migration is reversible (non-empty `Down`) | ✓ Pass | [InitialCreate.cs:664-728](backend/RestaurantApi/Migrations/20260519152754_InitialCreate.cs#L664-L728) drops in FK-aware order; [AddOrderTracking.cs:39-56](backend/RestaurantApi/Migrations/20260519194206_AddOrderTracking.cs#L39-L56) drops index then columns. |
| Indexes on FKs and frequently filtered columns | ⚠ Partial — see [F2](#f2--medium--order_items-and-flash_deal_offers-have-no-index-on-restaurant_id-despite-the-query-filter) | `Product.CategoryId/RestaurantId`, `Order.RestaurantId/TableId`, `RefreshToken.UserId`, `RestaurantTable.QrToken` (unique) all indexed. `OrderItem.RestaurantId` and `FlashDealOffer.RestaurantId` are not. |
| `QrToken` is `UNIQUE` globally | ✓ Pass | [AppDbContext.cs:79](backend/RestaurantApi/Data/AppDbContext.cs#L79). Confirmed in DB: `IX_tables_qr_token` is unique. |
| `OrderNumber` composite index with `RestaurantId` | ⚠ Partial — see [F3](#f3--medium--orders-restaurant_idorder_number-is-not-unique-and-the-assigner-is-read-modify-write) | Index exists ([AppDbContext.cs:97](backend/RestaurantApi/Data/AppDbContext.cs#L97)) but is **not** unique; assigner has a read-modify-write race. |
| Cascade rules intentional | ⚠ Partial — see [F5](#f5--low--flashdealoffer-fks-do-not-state-ondelete-explicitly) | `OrderItem.ProductId` is `Restrict` with a justifying comment; `SimilarityRule.ProductA/B` likewise. `FlashDealOffer` FKs to `orders`/`products` have no explicit `OnDelete`. |
| `decimal` precision set explicitly for money / score | ✓ Pass | `[Column(TypeName = "decimal(10,2)")]` on `Price`, `TotalAmount`, `UnitPriceAtPurchase`, `DiscountPrice`; `decimal(5,4)` on `SimilarityRule.Score`. |
| Timestamps use `timestamptz` | ✓ Pass | Every `DateTime`/`DateTime?` column lands as `timestamp with time zone` in migrations. |
| `Restaurant.Timezone` is for display only | ✓ Pass | Stored as `text`, never used in `Where(...)` or compared to `DateTime.UtcNow`. |
| Seed runs only in Development and is idempotent | ✓ Pass | [Program.cs:194-200](backend/RestaurantApi/Program.cs#L194-L200) is gated on `IsDevelopment()`; [SeedData.cs:21](backend/RestaurantApi/Data/SeedData.cs#L21) early-returns when `Organizations.AnyAsync()`. |
| No data migration in `OnModelCreating` | ✓ Pass | Only schema (`ToTable`, `HasColumnName`, `HasIndex`, `HasOne`, `HasQueryFilter`) — no `HasData`. |
| Initial migration is a single coherent schema | ⚠ Partial — see [F1](#f1--medium--dev-database-carries-an-orphan-migration-from-a-never-merged-branch) | Two on-trunk migrations 5 h apart; a third (`20260520074544_AddMenuImportJob`) reached the shared dev DB from a side branch and never landed on `main`. |
| Plan for prod migrations recorded | ❌ Fail — see [F6](#f6--low--no-recorded-plan-for-applying-migrations-in-non-development) | [Program.cs:191-193](backend/RestaurantApi/Program.cs#L191-L193) comment names the production approach in passing but no runbook/`README`/`docs/` entry expands on it. |

---

## F1 — MEDIUM — Dev database carries an orphan migration from a never-merged branch

Migrations folder on `main` contains exactly two migrations:

```
20260519152754_InitialCreate
20260519194206_AddOrderTracking
```

The live dev database, however, reports three applied:

```sql
docker compose exec postgres psql -U dev -d restaurant -c "SELECT * FROM \"__EFMigrationsHistory\";"
--           MigrationId           | ProductVersion 
-- ---------------------------------+----------------
--  20260519152754_InitialCreate    | 8.0.0
--  20260519194206_AddOrderTracking | 8.0.0
--  20260520074544_AddMenuImportJob | 8.0.0
```

`\dt` shows the corresponding orphan table:

```
public | menu_import_jobs          | table | dev
```

`git log --all --oneline -- "**/AddMenuImportJob*"` traces it to commit `5cb09b5` on branch `feature/better-pdf-reading`, which is **not an ancestor of `main`**:

```powershell
git merge-base --is-ancestor 5cb09b5 main && echo "in main" || echo "NOT in main"
# NOT in main
```

When that branch was checked out against the shared dev compose stack, [Program.cs:194-200](backend/RestaurantApi/Program.cs#L194-L200) ran `db.Database.Migrate()` and applied the branch's migration. Switching back to `main` leaves the migration history in a state EF considers inconsistent: it has applied a migration the current model knows nothing about.

This isn't a security issue — it's an operational footgun:

- The auto-migrate flow only rolls *forward*. There's no path back to the snapshot in `main` short of manually editing `__EFMigrationsHistory` or `docker compose down -v` (which the operator may not realize they need).
- The orphan table `menu_import_jobs` carries `restaurant_id` and timestamps but is invisible to the application — it accumulates orphan rows on the side branch and silently persists when the branch is abandoned.
- `dotnet ef migrations add` from this state will generate a "fixup" migration that drops `menu_import_jobs`, which then gets committed to `main` and applied to every other dev environment, including ones that *never* ran the side branch. Each developer's first run from a clean state then runs the no-op drop.

**Suggested follow-up:** document the recovery path in the README ("if you've run a feature branch, run `docker compose down -v` before checking out main"), and consider making the dev migrator emit a warning when `__EFMigrationsHistory` contains a migration the assembly doesn't know about.

---

## F2 — MEDIUM — `order_items` and `flash_deal_offers` have no index on `restaurant_id` despite the query filter

Both entities implement `ITenantScoped`:

[backend/RestaurantApi/Models/OrderItem.cs:5](backend/RestaurantApi/Models/OrderItem.cs#L5)

```csharp
public class OrderItem : ITenantScoped { ... public Guid RestaurantId { get; set; } ...
```

[backend/RestaurantApi/Models/FlashDealOffer.cs:5](backend/RestaurantApi/Models/FlashDealOffer.cs#L5)

```csharp
public class FlashDealOffer : ITenantScoped { ... public Guid RestaurantId { get; set; } ...
```

Both have `HasQueryFilter` registered against `RestaurantId`:

[backend/RestaurantApi/Data/AppDbContext.cs:303-304](backend/RestaurantApi/Data/AppDbContext.cs#L303-L304)

```csharp
modelBuilder.Entity<OrderItem>().HasQueryFilter(x => _tenant.IsResolutionSuppressed || x.RestaurantId == _tenant.RestaurantId);
modelBuilder.Entity<FlashDealOffer>().HasQueryFilter(x => _tenant.IsResolutionSuppressed || x.RestaurantId == _tenant.RestaurantId);
```

But the `OrderItem` and `FlashDealOffer` entity blocks register only the FK indexes — no `HasIndex(x => x.RestaurantId)`:

[backend/RestaurantApi/Data/AppDbContext.cs:104-148](backend/RestaurantApi/Data/AppDbContext.cs#L104-L148)

Confirmed in the live DB:

```
flash_deal_offers : IX_flash_deal_offers_order_id, IX_flash_deal_offers_product_id  (no restaurant_id)
order_items       : IX_order_items_order_id, IX_order_items_product_id              (no restaurant_id)
```

Every read of either table — including admin metrics queries that aggregate without an `OrderId` predicate, e.g. [AdminController.cs:252-254](backend/RestaurantApi/Controllers/AdminController.cs#L252-L254):

```csharp
var totalDeals = await _dbContext.FlashDealOffers.CountAsync();
var claimedDeals = await _dbContext.FlashDealOffers.CountAsync(d=>d.Status=="claimed");
var expiredDeals = await _dbContext.FlashDealOffers.CountAsync(d=>d.Status=="expired");
```

— translates to `SELECT ... WHERE restaurant_id = $tenant`. With no index on `restaurant_id`, that's a sequential scan. The roadmap calls this out explicitly:

> Indexes exist for FKs and frequently filtered columns

By "frequently filtered" the query filter qualifies the entire table.

For `OrderItem`, joining through `Order` (which *does* have `IX_orders_restaurant_id`) covers most reads, so the perf cost today is bounded. For `FlashDealOffer`, the admin metrics endpoints scan the full table on every load.

**Suggested follow-up:** add `e.HasIndex(oi => oi.RestaurantId)` and `e.HasIndex(f => f.RestaurantId)` in `OnModelCreating`; ship the resulting `Add*Index` migration. Cheap to add; meaningful in prod once these tables grow.

---

## F3 — MEDIUM — `orders.(restaurant_id, order_number)` is not unique and the assigner is read-modify-write

The composite index exists but is non-unique. From the DB:

```
IX_orders_restaurant_id_order_number | CREATE INDEX "IX_orders_restaurant_id_order_number" ON public.orders USING btree (restaurant_id, order_number)
```

The assignment site is read-modify-write *inside a single SaveChangesAsync* but with no row lock on the source range:

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
order.OrderNumber = lastNumber + 1;
```

The trade-off in the comment is acknowledged but cuts the wrong way:

- Two diners at the same restaurant submitting simultaneously each see `MAX = N`, both write `N+1`, both commit. They get the same "Comanda #N+1" on the customer screen and on the kitchen kanban (segment 12's display).
- The roadmap's "Microscopic review checklist" requires us to "confirm uniqueness scope" — a non-unique sequence per restaurant is documented but not designed-around.
- Routes that hand "Comanda #N" back to the customer are looking at a value that can collide. If the kitchen later filters by `RestaurantId + OrderNumber` to display one order, they get two rows.

**Suggested follow-up:** either (a) promote the index to unique and wrap the SELECT-MAX/INSERT in a retry loop on `DbUpdateException` with a unique-violation, or (b) move the sequence to PostgreSQL (`CREATE SEQUENCE order_seq_<restaurant_id>` or a `(restaurant_id) → next_value` table updated with `FOR UPDATE`). Approach (b) is more invasive but eliminates the race. The comment's rationale ("a unique index would convert a collision into a 500 with no easy retry path") is solved by the retry loop in approach (a), so the cost of correctness is small.

---

## F4 — LOW — `restaurants.slug` is checked globally-unique by a TOCTOU loop with no backing unique index

`Organization.Slug` has a unique index ([AppDbContext.cs:207](backend/RestaurantApi/Data/AppDbContext.cs#L207)). `Restaurant.Slug` does not — no `HasIndex` at all on it. But the slug generator treats it as globally unique:

[backend/RestaurantApi/Controllers/OrganizationsController.cs:217-230](backend/RestaurantApi/Controllers/OrganizationsController.cs#L217-L230)

```csharp
private async Task<string> UniqueSlugAsync(string name, bool isOrg)
{
    var baseSlug = Slugify(name);
    var slug = baseSlug;
    var i = 1;
    while (isOrg
        ? await _db.Organizations.AnyAsync(o => o.Slug == slug)
        : await _db.Restaurants.AnyAsync(r => r.Slug == slug))
    {
        slug = $"{baseSlug}-{++i}";
    }
    return slug;
}
```

Two concurrent restaurant creations with the same name both see no conflict, both insert `demo-restaurant`. For `Organization.Slug` the unique index forces one of them to lose with a `DbUpdateException` (recoverable). For `Restaurant.Slug` there's no DB guard — both rows commit, and any later code that resolves a restaurant by slug now has two matches.

Severity is **low** because there's no slug-based public lookup today (`r.Slug` is returned in `RestaurantResponse` but no controller route uses it as a key — routes use `restaurantId` Guids). Risk is forward-looking: the moment something like `/r/{slug}/menu` appears, the lack of a unique index becomes load-bearing.

**Suggested follow-up:** add `e.HasIndex(r => r.Slug).IsUnique()` to mirror `Organization`. The slug loop already handles the unique-violation by retrying with `-N` suffixes; promoting the index just closes the race window.

---

## F5 — LOW — `FlashDealOffer` FKs do not state `OnDelete` explicitly

The two history-preserving FKs in the codebase carry explicit `OnDelete(DeleteBehavior.Restrict)` plus a justifying comment:

[backend/RestaurantApi/Data/AppDbContext.cs:120-126](backend/RestaurantApi/Data/AppDbContext.cs#L120-L126)

```csharp
// Restrict so deleting a Product cannot wipe historical OrderItems.
// Admins should soft-delete via IsAvailable=false instead.
e.HasOne(oi => oi.Product)
 .WithMany()
 .HasForeignKey(oi => oi.ProductId)
 .OnDelete(DeleteBehavior.Restrict);
```

[backend/RestaurantApi/Data/AppDbContext.cs:187-195](backend/RestaurantApi/Data/AppDbContext.cs#L187-L195) does the same for `SimilarityRule.ProductA/B`.

But `FlashDealOffer` — which also references `Order` and `Product` and is also a quasi-historical record — leaves `OnDelete` unspecified:

[backend/RestaurantApi/Data/AppDbContext.cs:139-147](backend/RestaurantApi/Data/AppDbContext.cs#L139-L147)

```csharp
e.HasOne(f => f.Order)
 .WithMany(o => o.FlashDeals)
 .HasForeignKey(f => f.OrderId)
 .IsRequired(false);

e.HasOne(f => f.Product)
 .WithMany()
 .HasForeignKey(f => f.ProductId)
 .IsRequired(false);
```

The migration confirms: no `onDelete:` argument is emitted ([InitialCreate.cs:443-453](backend/RestaurantApi/Migrations/20260519152754_InitialCreate.cs#L443-L453)), and `\d flash_deal_offers` in the DB shows no `ON DELETE` clause — i.e., PostgreSQL's default `NO ACTION` (semantically `Restrict`). Because the FK columns are nullable in the entity, EF Core might have emitted `SetNull`; it didn't, so deleting a `Product` referenced by a flash deal fails today.

That behavior happens to be safe — flash deals on a deleted product become orphans rather than wiped history — but it's *implicit*. A reader of `OnModelCreating` sees that `OrderItem.ProductId` was deliberately set to `Restrict` and assumes `FlashDealOffer.ProductId` got the EF default (which, for a nullable FK, can be `SetNull` in EF 8). The implicit choice is doing the right thing by accident.

**Suggested follow-up:** add `.OnDelete(DeleteBehavior.Restrict)` to both FlashDealOffer FKs and copy the OrderItem comment, so the cascade strategy is consistent and grep-able.

---

## F6 — LOW — No recorded plan for applying migrations in non-Development

[Program.cs:191-200](backend/RestaurantApi/Program.cs#L191-L200) names the intent in a comment:

```csharp
// Auto-migrate and seed only in Development. In production, run
// `dotnet ef database update` as part of the deploy pipeline so a bad
// migration never blocks the API from starting.
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
    await SeedData.InitializeAsync(scope.ServiceProvider);
}
```

The gating is correct (the segment's 🔥 item is satisfied — auto-migrate is off in prod). But the deploy mechanism named in the comment exists nowhere else in the repo:

- No `.github/workflows/*` step runs `dotnet ef database update`.
- No `README.md` / `docs/` entry describes the prod migration step.
- The roadmap's follow-up question ("How does the team handle a backward-incompatible migration against a populated prod DB?") has no documented answer.

A reader inheriting the project would not know whether to (a) check an init-container step, (b) run migrations from a CI pipeline, or (c) generate idempotent SQL via `dotnet ef migrations script --idempotent` and apply manually. All three are reasonable; none are written down.

**Suggested follow-up:** add a short `docs/operations/migrations.md` (or a section in the root README) describing the chosen approach, and reference it from the comment in `Program.cs`. The cost is one paragraph; the alternative is the next on-call discovering it from a 4 a.m. failed deploy.

---

## Verification commands run

```powershell
# Verify migrations on disk vs in DB
Glob backend/RestaurantApi/Migrations/*.cs
docker compose exec -T postgres psql -U dev -d restaurant -c "SELECT * FROM \"__EFMigrationsHistory\";"

# Schema and indexes
docker compose exec -T postgres psql -U dev -d restaurant -c "\dt"
docker compose exec -T postgres psql -U dev -d restaurant -c "\di"
docker compose exec -T postgres psql -U dev -d restaurant -c "\d menu_import_jobs"
docker compose exec -T postgres psql -U dev -d restaurant -c "SELECT indexname, indexdef FROM pg_indexes WHERE tablename IN ('restaurants','order_items','flash_deal_offers','orders');"
docker compose exec -T postgres psql -U dev -d restaurant -c "SELECT indexname, indexdef FROM pg_indexes WHERE tablename = 'AspNetUsers';"

# Raw SQL audit
Grep "FromSqlRaw|ExecuteSqlRaw|FromSqlInterpolated" backend/RestaurantApi  # → no matches

# Git history for orphan migration
git log --all --oneline -- "**/AddMenuImportJob*"
git merge-base --is-ancestor 5cb09b5 main
```

`dotnet ef migrations script --idempotent` was not run: the `backend-api` container ships only the .NET runtime, not the SDK; `dotnet ef` isn't available inside the container, and the host machine wasn't tested. The schema verification above (via `psql`) is a superset of what the SQL script would have shown.

---

## Sign-off

- 🔥 items in scope (`AutoMigrate` gated to Development): **pass** — [Program.cs:194](backend/RestaurantApi/Program.cs#L194).
- Findings filed: [F1](#f1--medium--dev-database-carries-an-orphan-migration-from-a-never-merged-branch) through [F6](#f6--low--no-recorded-plan-for-applying-migrations-in-non-development).
- Cross-segment links: [F2](#f2--medium--order_items-and-flash_deal_offers-have-no-index-on-restaurant_id-despite-the-query-filter) overlaps with Segment 04's query-filter coverage; [F3](#f3--medium--orders-restaurant_idorder_number-is-not-unique-and-the-assigner-is-read-modify-write) overlaps with Segment 09 (customer order placement) and Segment 12 (kanban display); [F5](#f5--low--flashdealoffer-fks-do-not-state-ondelete-explicitly) overlaps with Segment 10 (flash-deal lifecycle). Flag any related findings in those reviews against the matching `[REV-06]` tag.
