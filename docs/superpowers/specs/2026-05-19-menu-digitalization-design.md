# Menu Digitalization — Design Spec

**Date:** 2026-05-19
**Branch:** `feature/digitalizing-menu`
**Status:** Design approved, ready for implementation plan

## 1. Goal

Let an admin/owner convert a physical (paper) menu into the app's digital menu by uploading photos. A vision-LLM extracts items and their section headings; the admin reviews and edits the result in a table; one click bulk-creates the products and any new categories.

## 2. Scope

### Use case (confirmed)

- **Primary user:** restaurant owner / admin.
- **Frequency:** once or very rarely per restaurant (initial onboarding or a full menu refresh).
- **Implication:** optimize for accuracy and a thorough review step over speed or daily-use ergonomics. Everything ongoing (single-item add, price tweaks) is handled by the existing manual editor.

### In scope for v1

- Upload of 1–10 photos in a single session.
- Vision-LLM extraction of: `name`, `price`, and `category_hint` (the section heading printed on the menu) per item.
- Auto-mapping of detected `category_hint`s to existing categories by normalized name; admin can override or create new categories.
- Editable review table with inline validation matching the existing `MenuManagementController` constraints.
- Atomic bulk-create of categories + products in one transaction.
- Lightweight scan-event logging (admin id, page count, model, duration, item count) — the hook for a future paid tier, no enforcement yet.

### Explicitly deferred

- Description / allergens / gramaj extraction (schema accepts them; v1 prompt doesn't request them).
- Hand-written or chalkboard menus.
- Variant pricing matrices beyond "one row per visible price".
- Cross-page duplicate detection (admin spots them in the review table).
- Hierarchical / nested categories.
- E2E browser tests (no E2E infra in repo yet — would be bigger than this feature).
- Paid-tier gating enforcement (logging only).

## 3. Architecture

```
Admin browser (Vue wizard at /admin/menu/import)
   │  step 1: upload photos
   │  step 2: map detected category_hints → categories (auto-match + override)
   │  step 3: review/edit items, then bulk-import
   ▼
C# RestaurantApi
   POST /api/admin/menu/scan                  (new, multipart photos[])
   │   - validates file count / size / mime
   │   - stream-forwards to Python /menu/extract
   │   - logs MenuScanEvent (admin, pages, model, duration, item count)
   │   - returns MenuScanResponse
   POST /api/admin/menu/products/bulk         (new, body: items[])
   │   - resolves/creates categories and products in one IDbContextTransaction
   │   - returns AdminProductResponse[]
   ▼
Python ai-scripts
   POST /menu/extract                          (new, multipart photos[])
   │   - asyncio.gather over images, each calling vision LLM via VisionExtractor
   │   - per-image try/except → partial-success response with failed_pages
   │   - returns MenuExtractionResponse
```

Reuses the existing C# ↔ Python AI delegation pattern from `RecommendationService` and `SimilarityService`. Extraction is one pure transform (`image bytes → JSON items`); that single boundary is where future fields plug in.

## 4. Data contracts

### Python (Pydantic — canonical)

```python
# backend/ai-scripts/models/menu_extraction.py
class ExtractedMenuItem(BaseModel):
    name: str
    price: float
    source_page: int
    source_index: int
    # Schema is forward-compatible — v1 prompt doesn't request these:
    description: Optional[str] = None
    category_hint: Optional[str] = None   # populated in v1; the section heading
    confidence: Optional[float] = None

class FailedPage(BaseModel):
    source_page: int
    reason: Literal["unreadable", "no_items_detected", "vision_api_error"]

class MenuExtractionResponse(BaseModel):
    items: List[ExtractedMenuItem]
    failed_pages: List[FailedPage] = []
    model: str
    schema_version: int = 1
```

### C# (DTO mirrors)

```csharp
public record ExtractedMenuItemResponse(
    string Name, decimal Price,
    int SourcePage, int SourceIndex,
    string? Description = null,
    string? CategoryHint = null,
    double? Confidence = null);

public record FailedPageResponse(int SourcePage, string Reason);

public record MenuScanResponse(
    IReadOnlyList<ExtractedMenuItemResponse> Items,
    IReadOnlyList<FailedPageResponse> FailedPages,
    string Model,
    int SchemaVersion);

public record BulkCreateProductsRequest(
    IReadOnlyList<BulkCreateProductItem> Items,
    IReadOnlyList<BulkCreateCategoryItem> NewCategories);

public record BulkCreateProductItem(
    string Name, decimal Price, Guid CategoryId,
    string? Description = null, bool IsAvailable = true);

public record BulkCreateCategoryItem(
    Guid TempId,        // client-generated, used to wire items → not-yet-persisted categories
    string Name,
    int? SortOrder = null);
```

`BulkCreateProductItem.CategoryId` may reference either an existing `Category.Id` or a `BulkCreateCategoryItem.TempId` (both are `Guid`). The C# endpoint resolves them in this order: (1) create every `NewCategories[]` row first and capture each `TempId → realId` mapping; (2) for every product, if its `CategoryId` matches a `TempId` from step 1, replace with the resolved real id; (3) verify the resulting `CategoryId` exists in `Categories`. Any unresolved id → 409.

### Shared TypeScript (`/shared/types/`)

Mirrors the C# shapes one-to-one. Validation constants (`NAME_MAX = 120`, `DESCRIPTION_MAX = 1000`, `PRICE_MAX = 99999999.99`) live in a new `shared/types/menu-validation.ts` and are imported by both the existing `MenuManagementView` and the new import wizard so frontend errors stay aligned with backend rules.

## 5. Vision-LLM prompt (v1)

Provider is pluggable via `AI_VISION_PROVIDER` env var (`claude` | `openai`). Recommended model: GPT-4o or Claude Sonnet (whichever the team has billing on) — "mini"/"haiku" tiers degrade noticeably on decorative menu layouts and aren't worth saving cents on a once-per-restaurant tool.

Response is enforced via the provider's structured-output / tool-use feature, then re-validated through Pydantic.

```
System: You extract structured menu data from photos of printed restaurant menus.
        The menus are typically in Romanian. Return only JSON matching the given schema.

User:   Extract every item that has a clear price from this menu photo.

        Rules:
        - Include only rows where a price is printed. Skip section headings
          ("APERITIVE", "BĂUTURI", "FEL PRINCIPAL", "DESERTURI" etc.) as items,
          decorative captions, the restaurant's name/contact info, and footnotes.
        - For `category_hint`: use the literal section heading printed above this item
          (e.g. "APERITIVE", "Fel principal"). Leave it null only for items that genuinely
          sit outside any visible heading. Do not invent a heading.
        - Keep the original Romanian spelling and diacritics (ă, â, î, ș, ț).
        - Prices in RON: convert "24,90 lei" / "24.90 RON" / "24,90" → 24.90.
        - If a price is a range or "de la X", use the lower bound.
        - If an item has multiple prices (small/large), emit one row per price,
          appending the qualifier to the name (e.g. "Salată Caesar (mică)").
        - `source_index` is the 0-based reading order within this page
          (top-to-bottom, left-to-right for two-column menus).
        - If you can't read an item clearly, skip it. Do not invent items.

        [image attached]
```

Per-page results are merged in the Python service with `source_page` set by the caller as it iterates uploaded photos. The model name and `schema_version` flow back so each scan event captures exactly what produced the import.

## 6. Frontend wizard

### Entry point

One new button in `MenuManagementView.vue`'s header, next to "Adaugă categorie":

```vue
<button class="pill-btn pill-btn-ghost" @click="router.push('/admin/menu/import')">
  <span>📷</span><span>Importă din fotografie</span>
</button>
```

Separate route, not a modal — the wizard's content (file thumbnails + 20+ row review table + thumbnail strip for cross-reference) is too large to fit comfortably as an overlay.

### File layout

```
src/views/MenuImportView.vue                # wizard shell, step orchestration, route /admin/menu/import
src/components/menu-import/
  ├─ ImportStepIndicator.vue                # 1 → 2 → 3 header
  ├─ StepUploadPhotos.vue                   # step 1
  ├─ StepMapCategories.vue                  # step 2
  └─ StepReviewItems.vue                    # step 3
src/stores/useMenuImportStore.ts            # wizard state, session-scoped, calls $reset() on success
src/services/menuImportApi.ts               # scan() + bulkCreateProducts()
```

The wizard store is separate from `useMenuManagementStore` because its lifetime is "this import session"; on success it calls `menuStore.fetchMenu()` and `$reset()`s itself.

### Store shape

```ts
interface MenuImportState {
  step: 1 | 2 | 3
  files: File[]                              // local thumbnails via URL.createObjectURL
  isScanning: boolean
  scanError: string | null
  failedPages: FailedPage[]
  detectedHints: string[]                    // unique non-null category_hint values
  categoryMapping: Record<string, MappedCategory>   // key = hint, value = where it resolves to
  unassignedItemsCategory: MappedCategory | null    // for items with null category_hint
  extractedItems: ReviewItem[]
  isImporting: boolean
  importError: string | null
}

type MappedCategory =
  | { kind: 'existing'; categoryId: string }
  | { kind: 'new'; tempId: string; name: string }

interface ReviewItem {
  clientId: string                           // uuid, local-only
  name: string
  price: number
  sourcePage: number
  resolvedCategory: MappedCategory
  skipped: boolean
  description?: string                       // future-modular
  categoryHint?: string                      // original from extractor, for the page-back-trace
}
```

### Step 1 — Upload photos

Drop zone styled like the existing `.card-paper` containers, plus a "Choose files" fallback. Client-side constraints: max 10 photos, max 5 MB each, mime types `image/jpeg`, `image/png`, `image/webp`, `image/heic`. Thumbnails render in a grid with a remove button per file. Primary button "Scanează meniul" POSTs to `/api/admin/menu/scan`; while scanning, an indeterminate progress bar replaces the button. Transitions to step 2 on success.

### Step 2 — Map categories

For each unique `category_hint` from the model, the wizard shows a row with a "Map to →" dropdown of existing categories plus a "Create new with this name" option (selected by default if no normalized name matches). Items with `category_hint = null` surface here under an explicit "Unassigned (N items)" row that must be mapped before continuing.

Auto-match algorithm: lowercase + strip diacritics on both sides, compare for equality. No fuzzy matching in v1.

"Continue" is disabled while any mapping is unresolved.

### Step 3 — Review items

Editable table ordered by `(sourcePage, sourceIndex)`, grouped under each resolved category header.

| Page | Name (editable) | Price RON (editable) | Status |
|---|---|---|---|
| 1 | `Salată Caesar` | `24.90` | Active / Skipped |

- Inline `<input>` for name (maxlength 120) and price (`step="0.01"`, `min="0.01"`).
- Validation matches the shared constants from `shared/types/menu-validation.ts`.
- "Skip" toggles `skipped`; the row dims and is excluded from the import payload.
- Page badge per row — clicking it scrolls the thumbnail strip at the bottom to that photo.
- Sticky footer: `[ Înapoi ] [ Importă N produse ]`. N = `extractedItems.filter(i => !i.skipped).length`. Button disabled while any non-skipped row is invalid or N === 0.

On click of "Importă…": `POST /api/admin/menu/products/bulk`, on success → toast "N produse importate", `menuStore.fetchMenu()`, `router.push('/admin/menu')`.

### Visual style

Reuses existing design tokens (`pill-btn`, `card-paper`, `card-white`, `eyebrow`, `display-title`, `field-input`) and `Intl.NumberFormat('ro-RO', {style:'currency', currency:'RON'})` for price display. The wizard looks like part of the existing admin app, not a new aesthetic.

## 7. Error handling

### Per-photo extraction failures

Most realistic failure mode: one photo in a batch is unreadable. Python wraps each image call in its own try/except; the response carries partial success via `failed_pages: List[FailedPage]`. The wizard's step 3 surfaces these inline above the review table: *"Pagina 2 nu a putut fi citită. [Re-încarcă pagina] [Sari peste]"*. Re-upload replaces just that photo's thumbnail and re-scans that single page; the rest of the data stays in state.

### Vision-LLM-level errors (Python ↔ provider)

- **Auth / credit / config:** logged with detail server-side, surfaced to client as a generic 502 ("Serviciul de scanare nu este disponibil. Încearcă din nou."). Never expose keys or provider identifiers.
- **Rate limit (429):** single exponential-backoff retry in Python (1s, 3s); if still 429, propagate up.
- **Provider timeout:** per-image timeout 60s, per-request timeout 6 minutes (covers a 10-photo batch with concurrency). C# `HttpClient` mirrors this.
- **Invalid JSON from model:** structured-output / tool-use should prevent this; Pydantic validation catches it as a defensive fallback. Treated as `vision_api_error` for that page.

### Empty result on a successful scan

Distinct empty state in step 3: *"Nu am detectat niciun produs cu preț. Verifică dacă fotografiile arată un meniu cu prețuri vizibile."* with a "Înapoi la încărcare" button. Doesn't crash, doesn't fall through to an empty mapping step.

### Frontend validation

Same constants as the C# `MenuManagementController` (`NAME_MAX = 120`, `DESCRIPTION_MAX = 1000`, `PRICE_MAX = 99999999.99`) imported from `shared/types/menu-validation.ts`. Per-row inline error messages; the "Importă" button is disabled while any non-skipped row is invalid or while no rows are importable.

### Bulk-create transactional safety

`POST /api/admin/menu/products/bulk` runs inside a single `IDbContextTransaction`:

1. Resolve / create new categories from the payload's `NewCategories[]`; each `Categories.Add` followed by `SaveChangesAsync` so the FK ids exist.
2. Build a map `tempId → realId` for items to reference.
3. Loop the items, applying validation via a shared `ProductValidator` helper extracted from `MenuManagementController.CreateProduct` (no forked rules).
4. On any validation failure → roll back, return `400 { errors: [{ index, field, message }] }`. Frontend highlights the offending row.
5. On success → `Commit`, return `AdminProductResponse[]`.

### Race conditions

- **Category deleted between step 2 and step 3:** before bulk-creating, the C# endpoint verifies every existing `categoryId` in the payload still exists. If one is gone → `409 { missingCategoryIds: [...] }`; frontend pops the admin back to step 2 with that category flagged.
- **New-category name now collides:** `Categories.Name` is not currently a DB unique constraint, so this is enforced in application code. Inside the transaction, before inserting a `NewCategories[]` row we check by normalized name (lowercased, diacritics stripped) against existing categories. If a match exists, we skip the insert and route that row's `TempId` to the existing category's real id (same name → same intent). No DB migration in v1 — a future hardening could add the unique index once we're confident no legacy duplicates exist.

### Cost / abuse guard

Server caps payload at `10 photos × 5 MB`; anything larger gets a clean `413 Payload Too Large` before the model is ever called.

Each successful scan emits a structured log entry via `ILogger<MenuScanController>` with `EventId(MenuScan)` and fields: `admin_id`, `file_count`, `total_bytes`, `duration_ms`, `model`, `item_count`, `failed_page_count`. v1 is logging only — no DB table, no quota enforcement. The future paid tier will either (a) introduce a `MenuScanEvent` table populated from the same emission site, or (b) consume the structured logs via the existing log sink. Either path keeps v1 untouched.

## 8. Testing strategy

The vision LLM is never hit from CI. Everything around it is tested with mocks; the model itself is verified via a small manual eval set.

### Python service

- `test_menu_extraction_parsing.py` — unit tests on the response parser: canned JSON strings (good / malformed / missing fields / extra fields) through Pydantic. No network.
- `test_menu_extraction_router.py` — integration test of `POST /menu/extract` with the vision provider client patched via `pytest-httpx` / `unittest.mock`. Covers: all-pages-succeed, one-page-fails-others-succeed, all-pages-fail, rate-limit-then-retry-succeeds, invalid-JSON-from-model. Uses tiny placeholder PNGs from `tests/fixtures/`.
- `scripts/eval_menu_extraction.py` (not a test) — run manually with an API key against `tests/fixtures/real_menus/*.jpg` (3–5 photos of real menus, committed) and prints precision/recall against hand-labeled JSON. This is the prompt-calibration tool.

### C# backend

New `backend/RestaurantApi.Tests/` project (xUnit + `Microsoft.AspNetCore.Mvc.Testing` + `Testcontainers.PostgreSql`).

- `MenuScanControllerTests` — Python service stubbed via `WireMock.Net`. Covers: happy-path forwarding, 502 when Python unreachable, 413 when payload exceeds cap, `failed_pages` passthrough.
- `BulkCreateProductsControllerTests` — against an ephemeral Postgres. Critical cases: happy path (items + new categories created in one transaction), validation failure on one item rolls back the entire transaction, `categoryId` missing → `409 missingCategoryIds`, new-category name collides with existing → routed silently to the existing row.
- `ProductValidatorTests` — pure unit tests on the validator helper shared between the new bulk endpoint and the existing single-create endpoint.

### Frontend admin

- `useMenuImportStore.spec.ts` — store transitions: can't advance past step 1 without files, step 2 must cover every hint, validation flags propagate to "can import" computed, `$reset()` after success clears everything. No HTTP.
- `StepReviewItems.spec.ts` (Vitest + `@vue/test-utils`) — renders with stub items; assert per-row inline errors, "Importă" button disabled state, skip toggle, page-badge click → thumbnail scroll.
- `menuImportApi.spec.ts` — MSW-mocked scan + bulk endpoints; happy path, `failed_pages` preserved in state, 409 → user routed back to step 2.

### E2E

Deferred. Listed here so we don't pretend it's covered.

### Manual verification checklist (for QA)

1. Photograph a real 3–5 page Romanian menu, run the wizard against the dev backend.
2. Section headings auto-map to existing categories where names match.
3. Edit a name and a price in the review table — the import uses the edits.
4. Skip a row — it isn't created.
5. Re-open `MenuManagementView` — items appear under the right categories, in the order they were on the menu.

## 9. Future extensibility

The modularity story made concrete: for each likely future direction, exactly which v1 piece changes.

### Adding extracted fields (description, allergens, gramaj, modifiers)

1. Flip the `Optional` field in `ExtractedMenuItem` (Pydantic) from passive to populated.
2. Add a sentence to the prompt requesting it.
3. Add a column (or per-row expander) to `StepReviewItems.vue`.
4. Add the field to `BulkCreateProductItem`.

No DB migration: `Products.Description` already exists. Allergens / gramaj need new columns but those are menu-management decisions, not extraction work.

### Swapping or adding LLM providers

The Python service hides the provider behind one interface:

```python
class VisionExtractor(Protocol):
    async def extract(self, image_bytes: bytes, prompt: str) -> dict: ...
```

`ClaudeExtractor` / `OpenAIExtractor` / `GeminiExtractor` implement it; `AI_VISION_PROVIDER` selects. New provider = one new class, no router changes. The `model` field in the response makes A/B comparisons reconstructable from logs.

### Paid-tier gating

`MenuScanEvent` already captures everything needed. Two pieces to add later, neither requiring v1 changes:

- A `MenuScanQuota` table keyed by admin/organization.
- A middleware on `/api/admin/menu/scan` that reads the quota and either passes through or returns `402 Payment Required`. The bulk-create endpoint stays gateless — review and import after scan is always allowed for already-extracted data.

### Smarter category detection

v1 only emits `category_hint` when a visible section heading exists. v2 prompt change: "if no heading visible, infer one from the item type". Mapping step in the wizard already handles "unmatched hint → create new" — better hints just mean fewer Unassigned rows for the admin to triage.

### Hierarchical categories ("Băuturi > Cafele > Espresso")

Out of scope. Would require a `ParentId` column and a treeview in the editor — bigger than this feature, unrelated to extraction itself.

### Re-scan an existing category

Different workflow: "I refreshed my menu, re-import only the affected category". Reuses the wizard with a "merge with existing" toggle on step 3 that diffs by normalized name and only creates missing items. The `ExtractedMenuItem` schema already declares an optional `confidence` field that v1's prompt doesn't request; v2's prompt would populate it and use it as the tiebreaker for merge conflicts. Same modularity pattern: prompt change + schema field flip from passive to populated, no DTO restructuring.

### Multi-language menus

Prompt already tolerates this. v2 would add a `name_translations: dict[str, str]` field on `ExtractedMenuItem` plus a translations column on `Products`. Same modularity pattern.

## 10. Acceptance criteria for v1

- An admin can navigate from `MenuManagementView` to `/admin/menu/import` and back.
- Admin can upload up to 10 photos (max 5 MB each) of mime type jpeg/png/webp/heic.
- After clicking "Scanează meniul", the wizard displays a list of unique `category_hint`s with auto-resolved or admin-overridden mappings.
- Admin can review every extracted item, edit name or price inline with validation matching the existing menu rules, and skip rows.
- Clicking "Importă N produse" creates all selected items (and any new categories) atomically. On success, the admin lands back in `MenuManagementView` with the new categories and products visible.
- Per-photo failures appear inline in step 3 and the admin can re-upload or skip the failing page without losing other state.
- A `MenuScanEvent` is logged for every successful scan.
- No regressions in the existing menu CRUD flow.
