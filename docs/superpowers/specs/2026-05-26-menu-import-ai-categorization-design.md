# Menu Import AI Categorization Design

Date: 2026-05-26
Branch: feature/better-pdf-reading

## Purpose

The current menu import flow (photo + PDF) extracts items and lets the user map them to categories one section at a time. Category assignment relies on `categoryHint` strings the extractor finds in the source document (visible section headings). The frontend fuzzy-matches those hints against the restaurant's existing categories and otherwise creates a new flat category per hint. There is no concept of a subcategory in the data model, no whole-menu coherence (each page's hints are interpreted on their own), and no graceful behavior when hints are missing, generic, or noisy.

This work adds an AI-driven categorization step after extraction that produces a coherent `category -> subcategory -> item` hierarchy across the whole imported menu. It introduces a real `subcategories` table so the hierarchy persists, extends the import wizard with a tree review screen, and keeps the existing extraction and progress pipeline unchanged.

## Goals

- Persist a real two-level menu hierarchy: `Category -> Subcategory -> Product`.
- After extraction and dedup, run a single LLM "organizer" call that returns the hierarchy with per-item confidence scores.
- Prefer extracted hints when they are useful, fall back to semantic grouping when hints are missing, noise, or fragmented.
- Surface the proposed hierarchy in a single tree review screen with confidence cues, drag/drop and keyboard moves, rename, merge, split, and add-subcategory actions.
- Keep the import working without regression when the organizer call fails (fall back to hint-only grouping).
- Keep the public read API backward compatible by adding subcategory fields as optional.

## Non-Goals

- Do not add standalone CRUD endpoints for subcategories outside the import flow in this sprint.
- Do not redesign the customer-facing menu or the admin menu list view. They receive subcategories from the API and render them as nested sub-headers, but their layout, drag/drop, and edit affordances are not changed.
- Do not try to map imported items into the restaurant's existing categories or subcategories. New imports always produce new categories/subcategories; the user can still hand-edit. (Existing categories matching can be added in a later sprint.)
- Do not change the C# job leasing, PDF parsing, or vision/text extractor behavior. The organizer step is purely additive after dedup.
- Do not backfill existing products with subcategory assignments. All existing products keep `subcategory_id` NULL.

## Approach Options Considered

### Option 1: Inline categorization in extractors

Extend the existing per-page vision/text extractor prompts to also emit a subcategory hint per item alongside `categoryHint`. Post-process: cluster items by `(categoryHint, subcategoryHint)` to form the tree. Smallest pipeline change and lowest extra cost, but each page sees only itself. Subcategories from page 5 will not align with page 2 ("Specialty" vs "Specialità"), and semantic fallback is hard to do per-page.

### Option 2: Post-extraction Organizer step (selected)

Keep the existing extractors unchanged. After `dedup_items()` runs, add a new categorization stage: one LLM call that takes the full deduped list and their hints and returns the whole tree with per-item confidence scores. Coherent hierarchy across pages, clean separation of concerns, naturally fits the hints-first / semantic-fallback rule, and easy to swap providers because it reuses the existing provider abstraction. Cost: one extra LLM round-trip per import (cheap for 50-200 items).

### Option 3: Local heuristics, no AI

Use `categoryHint` to group, and detect subcategories from price-tier clusters or name n-gram clusters. No extra LLM call. Rejected because it rarely produces semantically meaningful subcategories, which is the central user-visible goal.

## Schema

### New `subcategories` table

```
subcategories
  id              uuid  PK
  restaurant_id   uuid  FK -> restaurants.id      (ITenantScoped)
  category_id     uuid  FK -> categories.id       (ON DELETE CASCADE)
  name            text  NOT NULL
  sort_order      int   NULL
  image_url       text  NULL

  unique (restaurant_id, category_id, name)
```

The model implements `ITenantScoped` so existing tenancy guards apply. `image_url` mirrors `categories.image_url` for symmetry even though the import flow does not populate it yet.

### `products` table change

Add a nullable column:

```
products
  + subcategory_id  uuid  NULL  FK -> subcategories.id  (ON DELETE SET NULL)
```

`ON DELETE SET NULL` keeps products inside their category when a subcategory is removed; they become "uncategorized within category" rather than being deleted.

### Migration

Single EF Core migration:

1. Create `subcategories` table with the columns above and the composite unique index.
2. Add `subcategory_id` nullable column to `products` with the FK constraint.
3. Add `subcategory_id` to the existing per-tenant index on products (e.g., `(restaurant_id, category_id, subcategory_id)`) if profiling shows it is needed; otherwise leave the index alone for this sprint.

No data backfill. Existing rows get `subcategory_id = NULL`.

## Pipeline

```
  Frontend  --PDF/photos-->  C# Backend Job
                                |
                                | enqueue
                                v
                          Python Pipeline
                            (1) Parse pages
                            (2) Extract items     <- unchanged
                            (3) Dedup             <- unchanged
                            (4) Organize          <- NEW: one LLM call
                                |
                                | POST /progress (final)
                                v
                          result_json:
                            { items,
                              hierarchy,          <- NEW field
                              failedPages,
                              schemaVersion: 2 }
                                |
                                v
                          Frontend tree review,
                          then POST bulk-create-hierarchy
```

The organizer runs after the existing extraction completes for both flows: after `dedup_items()` for PDF imports, and after photo extraction finishes for photo imports. One LLM call per import, gated by the existing provider semaphore from `dc8ffbc`.

## Python Organizer Step

### File and shape

New module `backend/ai-scripts/services/menu_organizer.py`. Defines a provider-agnostic `MenuOrganizer` interface and concrete `ClaudeMenuOrganizer` / `GeminiMenuOrganizer` implementations, following the pattern in `vision_extractor.py`. Provider is selected by `AI_ORGANIZER_PROVIDER`, defaulting to the same provider as `AI_TEXT_PROVIDER` so a single env knob still works for typical deployments.

The organizer is called from both:

- `pdf_menu_extraction_service.py` after the existing dedup step.
- `menu_extraction.py` after photo extraction completes.

### Inputs

- Full deduped `list[ExtractedMenuItem]` (name, price, description, `categoryHint`, source_page).
- Restaurant locale string (e.g. `ro-RO`). C# already stores the restaurant locale; this work adds a `locale` field to the Python request DTOs (`PdfJobRequest`, photo scan request) and threads it through to the organizer call.
- Configuration knobs in `OrganizerOptions`:
  - `max_items_per_subcategory` (default 12).
  - `min_items_to_introduce_subcategory` (default 5).
  - `noise_hint_blocklist`: list of strings the organizer should treat as absent hints, default `["others", "menu", "produse", "altele"]`, comparison after diacritic-strip and lowercase.

### Output schema

Validated by Pydantic, returned through the existing provider tool-use channel:

```
OrganizerResult {
  source: "ai" | "hint-fallback"
  categories: List[OrganizedCategory]
  uncategorizedItemRefs: List[ItemRef]
  modelNotes: Optional[str]
}

OrganizedCategory {
  name: str
  sortOrder: int
  subcategories: List[OrganizedSubcategory]
  directItemRefs: List[ItemRef]
}

OrganizedSubcategory {
  name: str
  sortOrder: int
  itemRefs: List[ItemRef]
}

ItemRef {
  sourcePage: int
  sourceIndex: int
  confidence: float    # 0.0 - 1.0
}
```

Items are referenced by the existing `(sourcePage, sourceIndex)` identity. The model never restates item names or prices, which keeps the response small and prevents the model from hallucinating new items.

### Prompt rules

The system prompt enforces a hints-first / semantic-fallback policy:

1. Trust hints first. If a group of items shares a clear `categoryHint`, use it verbatim as the category name (case-normalized).
2. Discard noise hints. Hints in the blocklist or shorter than 3 characters are treated as absent.
3. Fall back to semantic grouping only for items with absent or noise hints, or when hints would create more than three categories of fewer than two items each (likely OCR noise; merge them).
4. Introduce subcategories only if a category has at least `min_items_to_introduce_subcategory` items AND the items naturally split into two or more clusters (cooking style, base ingredient, price tier, occasion). A single subcategory containing all of a category's items is meaningless; in that case emit no subcategories and put the items under `directItemRefs`. Never force-create a subcategory.
5. Confidence reflects assignment certainty: 1.0 = item name explicitly matches subcategory; 0.7-0.9 = strong semantic match; 0.4-0.6 = plausible; below 0.4 = put it in `uncategorizedItemRefs`.
6. Output category and subcategory names in the restaurant locale.

The prompt template lives next to the module so prompt iteration does not require code changes elsewhere.

### Failure behavior

If the organizer call fails (timeout, provider error, schema-invalid response, exceeded retries) the pipeline emits a `hierarchy` value constructed locally from hints:

- Every distinct non-blocklist `categoryHint`, normalized, becomes a category.
- No subcategories are introduced.
- Items with no hint go into `uncategorizedItemRefs` with `confidence = 0`.
- `source` is set to `"hint-fallback"`.

The frontend renders both `"ai"` and `"hint-fallback"` results with the same component; the only difference is a soft banner shown when `source === "hint-fallback"`. This keeps the import working end-to-end even when the AI provider is unavailable.

### Concurrency and cost

One LLM call per import. The organizer reuses the existing provider call semaphore introduced in `dc8ffbc`. No new concurrency dimension is added.

## API Contracts

### Python -> C# result payload

The final progress callback body grows one field. `schemaVersion` bumps to 2.

```
result_json = {
  items: ExtractedMenuItem[],          # unchanged
  failedPages: FailedPage[],           # unchanged
  hierarchy: {                         # NEW, optional
    source: "ai" | "hint-fallback",
    categories: OrganizedCategory[],
    uncategorizedItemRefs: ItemRef[],
    modelNotes?: string
  },
  model: string,
  schemaVersion: 2
}
```

Callers reading `schemaVersion: 1` continue to work because the new field is additive. The C# job stores `result_json` as JSONB; no schema migration on the job side.

### New bulk-create endpoint

```
POST /api/restaurants/{restaurantId}/admin/menu/bulk-create-hierarchy

Request:
{
  categories: [
    {
      tempId: "c1",
      name: "Pizza",
      sortOrder: 0,
      subcategories: [
        { tempId: "s1", name: "Classic", sortOrder: 0 }
      ]
    }
  ],
  products: [
    {
      name: "Margherita",
      price: 28,
      description: "...",
      categoryTempId: "c1",
      subcategoryTempId: "s1" | null,
      sortOrder: 0,
      isAvailable: true
    }
  ]
}

Response:
{
  createdCategories:    [{ tempId, id }],
  createdSubcategories: [{ tempId, id }],
  createdProducts:      [{ tempId?, id, name }]
}
```

All three inserts run inside one DB transaction, following the existing pattern in `MenuManagementController.cs:386-520`. Validation rules:

- `categoryTempId` must reference a category in the request.
- If `subcategoryTempId` is set, it must reference a subcategory whose parent category is the same as `categoryTempId`.
- Duplicate `tempId` values across categories or across subcategories are rejected.
- Category and subcategory names within the same parent must be unique (matches the DB constraint).

The old `bulk-create` endpoint stays in place for non-import callers; the import wizard switches to the new endpoint.

### GET menu read shape

The `GET /api/restaurants/{restaurantId}/menu` (and admin menu read) responses gain optional fields:

```
category: { id, name, sortOrder, imageUrl, subcategories?: [{ id, name, sortOrder, imageUrl? }] }
product:  { ..., subcategoryId?: string | null }
```

Old clients that ignore unknown fields continue to work. The customer menu and admin menu list view receive subcategories but the data is optional.

### Render impact on existing screens

The minimum surface to avoid regressions:

- Admin menu list view: when a category has subcategories, products are grouped under bold subcategory sub-headers within the category. Products with `subcategoryId === null` render first under the category, products in subcategories render below. No new edit affordances.
- Customer menu: same nested grouping using existing item card. Layout and styles are not changed.

No drag/drop, rename, or CRUD UI for subcategories is added outside the import wizard.

## Frontend Review UI

### Store changes

`useMenuImportStore.ts` gets:

- New state `hierarchy: { source: 'ai' | 'hint-fallback', categories: CategoryNode[], uncategorized: ItemRef[] }`.
- New actions: `moveItem(itemRef, target)`, `renameCategory(id, name)`, `renameSubcategory(id, name)`, `mergeSubcategories(idA, idB)`, `splitSubcategory(id, intoNames[])`, `addSubcategory(categoryId, name)`, `removeSubcategory(id)` (items go to parent category, no data loss).
- The existing `autoMatch()` (line 264-276) becomes the hint-fallback path. It runs only when `hierarchy` is missing or `source === 'hint-fallback'`, producing the same shape as the AI path.

### New review component

`StepReviewHierarchy.vue` replaces `StepMapCategories.vue` in the wizard for the new flow. Layout sketch:

```
Header
  Title: "Review menu structure"
  Subtitle pill: "Saved by AI - review before saving" or
                 "AI categorization wasn't available - grouped by section headings only" (yellow)
Tree (single column, expand/collapse per category):
  v Pizza                              [rename] [+ subcategory]
     v Classic                                   [rename]
         . Margherita ......... 28 lei            [move]
         . Quattro Stagioni ... 32 lei  amber-dot [move]
     v Specialty                                  [rename]
         . ...
     . Direct items (no subcategory): Calzone     [move]
  v Salads
     ...
  v Uncategorized (3)                  [auto-assign]
     . Tiramisu  amber-dot              [move]
Footer
  [Back]                                          [Save]
```

UI rules:

- Confidence cue: amber dot next to items with `confidence < 0.7`. No badge above 0.7 to avoid noise.
- Move actions: drag/drop is the fast path; a per-item kebab menu offering "Move to category / subcategory / uncategorized" is the keyboard/accessibility path. Both update the store the same way.
- The hint-fallback banner appears when `source === 'hint-fallback'`.
- Empty subcategories are allowed in the UI (user may be staging a structure) and are silently dropped on save.
- Direct items under a category (subcategory null) render under their category, visually distinct from the subcategory blocks.
- "Auto-assign" on the Uncategorized group re-runs the local hint-fallback grouping for that subset only. It never calls the AI again.

### Save flow

The wizard final step calls `bulk-create-hierarchy`. The existing `StepReviewItems.vue` (item-level review of names and prices) stays as the prior step, unchanged.

### Skipped UI

- No drag/drop reorder of categories or subcategories in this sprint. Sort order is whatever the AI returned (or the user's manual ordering if they renamed or moved). A simple up/down arrow control is fine if it is one-line trivial; otherwise defer.
- No image upload for subcategories. The schema field exists for future use.

## Testing

Each unit is exercised by tests written before implementation.

### Python tests

- `tests/test_menu_organizer.py`:
  - Hints-first: clear, distinct hints survive into category names verbatim (case-normalized).
  - Noise hint discarded: items under "OTHERS" go through semantic grouping or to uncategorized.
  - No subcategory when items < `min_items_to_introduce_subcategory`.
  - Subcategory introduced when items cluster naturally.
  - Confidence scores are in expected band given the rules.
  - Hint-fallback path produces a valid hierarchy when the LLM raises.
  - Provider client is called once per import.
  - Locale propagation: organizer is asked to output in the requested locale.
- `tests/test_pdf_pipeline_integration.py` is extended to assert the final result includes a `hierarchy` field with `schemaVersion: 2`.

### C# tests

- `MenuImportEndToEndTests`:
  - Happy path now includes a `hierarchy` round-trip through `result_json`.
  - Bulk-create-hierarchy creates categories, subcategories, and products in one transaction.
  - Validation: subcategory referencing a different category's tempId is rejected.
  - Validation: duplicate tempIds across categories rejected.
  - Tenancy: a subcategory cannot reference a category in another restaurant.
- New `BulkCreateHierarchyValidationTests` for input shape and FK rules.
- Migration test runs the new EF migration on the test container DB and asserts the resulting schema.

### Frontend tests

- `useMenuImportStore.spec.ts`:
  - Store hydrates a `hierarchy` from the job result.
  - `moveItem`, `renameCategory`, `renameSubcategory`, `addSubcategory`, `removeSubcategory`, `mergeSubcategories`, `splitSubcategory` behave correctly and never lose items.
  - Hint-fallback grouping produces the expected categories from hints alone.
  - Save serializes to the new endpoint shape, with `subcategoryTempId` correctly set or null.
- `StepReviewHierarchy.spec.ts`:
  - Renders categories, subcategories, and direct items.
  - Amber dot appears for items below the confidence threshold.
  - Hint-fallback banner appears only when `source === 'hint-fallback'`.
  - Move action via menu and via drag/drop produce the same state.

## Rollout

- The migration runs in the standard deploy. It only adds a table and one nullable column, so it is safe under writes.
- The organizer is enabled by default. If it misbehaves in production, set `AI_ORGANIZER_PROVIDER=none` to force the hint-fallback path without changing code (covered by an explicit branch in `menu_organizer.py`).
- `schemaVersion` bump to 2 is read by the frontend; the frontend tolerates either 1 or 2 in case of a partial deploy.
- Telemetry: log organizer outcome (`source`, item count, category count, subcategory count, items below confidence threshold, organizer latency) at INFO. No PII; item names are not logged.

## Open Questions

None at this design stage. Implementation may surface naming or ergonomics questions on `StepReviewHierarchy.vue`; those will be resolved during the plan and code review phases.
