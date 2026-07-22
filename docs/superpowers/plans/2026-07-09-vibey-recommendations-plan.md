# Plan: Vibey — in-cart AI recommendations

Executes `docs/superpowers/specs/2026-07-09-vibey-recommendations-design.md`.
Branch: `feat/introducing-vibey`. Four tasks, mostly sequential (contract flows
backend → frontend).

## Global Constraints (bind every task)

- **Cost invariant:** the ONLY model call is on the first `Da, arată-mi` tap;
  cart load / quantity change / sheet re-open / add-item make ZERO model calls.
  Teaser + post-add follow-up are deterministic client copy, never networked.
- **Multi-tenancy:** the new customer endpoint is tenant-scoped by the
  `restaurantId` ROUTE param (resolved by `TenantResolutionMiddleware`). NEVER
  read a restaurantId/orderId from the request body for scoping. Product queries
  rely on the global tenant query filter — do not bypass it.
- **Money is `decimal`** server-side (`decimal(10,2)`); the client rounds with
  `utils/money.ts`. Prices are `priceRon` numbers in the wire contract.
- **Contract is a 3-place slice:** C# DTO ↔ `shared/types/*.ts` (edit `.ts`, not
  generated `.js`/`.d.ts`) ↔ frontend api-client. Keep them identical.
- **UI language:** admin RO-only; customer needs RO **and** EN for every
  user-facing string (`useLocale.ts`). Model-produced `reason`/`summary` come in
  the request `locale`; deterministic teaser/follow-up/labels ship RO+EN.
- **SignalR / order-status literals:** unaffected — do not touch them.
- **Graceful failure:** a failed/empty AI result returns
  `{ summary:"", recommendations:[] }`; the guest sees a calm empty state, never
  an error. AI failures are logged server-side, never swallowed into a 200 that
  hides a bug elsewhere.
- Keep each task's removal vs. addition reviewable; commit per task.

---

## Task 1: Backend slice — remove old endpoint, add Vibey endpoint + contract

**Goal:** Server-side Vibey: one lazy endpoint returning `{summary,
recommendations[]}`, reusing existing candidate/rule/AI plumbing; old eager
endpoint gone; contract synced.

### 1a. Python `ai-scripts` — add `summary` to `/recommend`

File: `backend/ai-scripts/models/schemas.py` — add `summary: str = ""` to
`RecommendationResponse`.

File: `backend/ai-scripts/services/recommendation_engine.py` — update the prompt
to ALSO produce a `"summary"`: one warm, concise customer-facing sentence in the
request language introducing the picks (e.g. RO: "Am găsit câteva completări care
merg de minune cu ce ai ales."). Parse it from the model JSON; return
`{"recommendations": validated, "summary": result.get("summary", "")}`. On the
existing `except`, return `{"recommendations": [], "summary": ""}`. Keep the
model (`gemini-3.1-flash-lite`) and the id-validation logic unchanged.

Add/extend a pytest in `backend/ai-scripts/tests/` mirroring existing tests
(mock the genai client) asserting `summary` is surfaced and empty-on-failure.

### 1b. .NET DTOs + shared contract

New `backend/RestaurantApi/DTO/Requests/VibeyRecommendationRequest.cs`:
```csharp
namespace RestaurantApi.DTOs.Requests;
public record VibeyRecommendationRequest(
    List<VibeyCartItem> Items,
    string? Locale
);
public record VibeyCartItem(string Id, string Name, string? Category, int Qty);
```

New `backend/RestaurantApi/DTO/Responses/VibeyRecommendationResponse.cs`:
```csharp
namespace RestaurantApi.DTOs.Responses;
public record VibeyRecommendationResponse(
    string Summary,
    List<VibeyRec> Recommendations
);
public record VibeyRec(string Id, string Name, string? Category, decimal PriceRon, string Reason);
```

`shared/types/api-requests.ts` — add:
```ts
export interface VibeyCartItem { id: string; name: string; category?: string | null; qty: number; }
export interface VibeyRecommendationRequest { items: VibeyCartItem[]; locale: string; }
```
`shared/types/api-responses.ts` — add:
```ts
export interface VibeyRec { id: string; name: string; category?: string | null; priceRon: number; reason: string; }
export interface VibeyRecommendationResponse { summary: string; recommendations: VibeyRec[]; }
```
(Edit only the `.ts`. Leave the RecommendationsResponse/pairings type in place —
Task 2 removes its last consumer; do not delete shared types here.)

### 1c. `RecommendationService` — cart-based method

Add a method that does NOT require an order row:
```csharp
public async Task<VibeyRecommendationResponse> GetVibeyRecommendationsAsync(
    IReadOnlyList<VibeyCartItem> cartItems, string language)
```
Behaviour (reuse the existing private `GetRuleDrivenRecommendationsAsync` and the
`AiService` HttpClient call verbatim where possible):
1. `cartProductIds` = parse `cartItems[].Id` to Guid (ignore unparseable).
2. Candidate products = `Products.Where(p => p.IsAvailable == true &&
   !cartProductIds.Contains(p.Id))` (tenant filter applies automatically). Also
   load their `Category.Name`.
3. Build the same Python `/recommend` request body (cart names come from
   `cartItems[].Name`; menu_items from candidates). Call `/recommend`. Read BOTH
   `recommendations` and `summary` from the response.
4. Merge rule-driven + AI recs, dedupe by product id, cap at **3**.
5. Enrich each surviving rec into `VibeyRec(id, name, category, priceRon, reason)`
   from the candidate products (drop any rec whose id isn't a known candidate).
6. On zero recs OR any exception: return `new VibeyRecommendationResponse("", [])`.
   Keep the AI-unreachable warning log. Do NOT invent a random fallback for
   Vibey (the old random 2-item fallback is for the removed endpoint).

Keep the old `GetRecommendationsAsync(orderId,...)` method ONLY if still
referenced after 1d; otherwise delete it with the endpoint.

### 1d. CustomerController — swap endpoints

- **Delete** `GetRecommendation` (the
  `[HttpGet("restaurants/{restaurantId:guid}/orders/{id:guid}/recommendations")]`
  action, CustomerController.cs ~L390–471) and its now-unused helper code.
- **Add**:
```csharp
[HttpPost("restaurants/{restaurantId:guid}/recommendations")]
public async Task<IActionResult> GetVibeyRecommendations(
    Guid restaurantId, [FromBody] VibeyRecommendationRequest request)
```
  - Guard: null/empty `request.Items` → `Ok(new VibeyRecommendationResponse("", []))`.
  - Cache key `$"vibey:{restaurantId}:{locale}:{string.Join(',', sorted item ids)}"`
    via injected `IMemoryCache`, ~2 min sliding, so identical carts don't re-hit
    the model. Cache the response object.
  - Call `_recommendationService.GetVibeyRecommendationsAsync(request.Items,
    request.Locale ?? "ro")`, return `Ok(response)`.
- `Program.cs`: add `builder.Services.AddMemoryCache();` (only if not already
  registered) and inject `IMemoryCache` into the controller ctor.

### 1e. Tests

The .NET recs endpoint has no existing integration test. Add a focused one in
`backend/RestaurantApi.Tests/` (WireMock-fake the AI `/recommend` like other AI
tests): POST to the new endpoint returns the enriched shape for a seeded cart,
and returns `{summary:"",recommendations:[]}` when the fake AI 500s. Follow
`TestWebApplicationFactory` + WireMock conventions. Run `dotnet test
backend/RestaurantApi.Tests --filter "FullyQualifiedName~Vibey"` (Docker
required) and `cd backend/ai-scripts && pytest`.

**Files:** `ai-scripts/models/schemas.py`, `ai-scripts/services/recommendation_engine.py`,
`ai-scripts/tests/*`, `RestaurantApi/DTO/Requests/VibeyRecommendationRequest.cs`,
`RestaurantApi/DTO/Responses/VibeyRecommendationResponse.cs`,
`RestaurantApi/Services/RecommendationService.cs`,
`RestaurantApi/Controllers/CustomerController.cs`, `RestaurantApi/Program.cs`,
`shared/types/api-requests.ts`, `shared/types/api-responses.ts`,
`RestaurantApi.Tests/*`.

---

## Task 2: Frontend removal of the old recommendation system

**Goal:** Delete the eager recs UI + plumbing so it can't overlap Vibey. Keep
the cart's "was recommended" tracking (Vibey reuses it).

- `frontend-customer/src/components/screens/MenuView.vue`: remove the
  `useRecommendations` import + `const { pairings, upsells } = ...`, the
  `recommendations` computed, `addRecommendation`, the `.recs` template block
  (~L252–289), and now-dead `.recs*` scoped CSS. Leave the rest of MenuView
  (menu list, filters, cart bar) intact.
- Delete `frontend-customer/src/composables/useRecommendations.ts`.
- Delete `frontend-customer/src/components/RecommendationWidget.vue` (dead; grep
  to confirm no importers first).
- `frontend-customer/src/services/api-client.ts`: remove `getRecommendations`
  and drop the now-unused `RecommendationsResponse` import if nothing else uses
  it.
- `frontend-customer/src/composables/useLocale.ts`: remove keys
  `menu.recommendedForYou` and `menu.addRecommendation` (RO + EN). Change
  `cart.aiSuggested` value to **"Sugerat de Vibey"** (RO) / **"Suggested by
  Vibey"** (EN).
- Do NOT touch `useCartStore` (recommendedItemIds/wasRecommended/
  addRecommendedItem stay — Vibey uses them). Do NOT delete `shared/types`
  Recommendation types (other code/back-compat).

**Verify:** `npm run type-check` and `npm run lint` clean; grep for
`useRecommendations` / `getRecommendations` / `RecommendationWidget` returns no
live references.

**Files:** `MenuView.vue`, `useRecommendations.ts` (del),
`RecommendationWidget.vue` (del), `api-client.ts`, `useLocale.ts`.

---

## Task 3: Vibey store, deterministic copy, contrast helper, api-client method

**Goal:** State + data layer for Vibey. No components yet.

- `frontend-customer/src/services/api-client.ts` — add:
```ts
getVibeyRecommendations: (payload: VibeyRecommendationRequest) =>
  fetcher<VibeyRecommendationResponse>(
    `/api/restaurants/${rid()}/recommendations`,
    { method: 'POST', body: JSON.stringify(payload) },
  ),
```
  Import the two types from `@shared/types/api-requests` / `api-responses`.

- `frontend-customer/src/design/contrast.ts` — `bestOn(hex: string): string`
  returning the higher-contrast foreground (light `#FFF4EC` vs dark `#15140F`)
  for a given background, using WCAG relative luminance (mirror the prototype
  `theme()` onSec logic: `lum < 0.55 ? light : dark`). Pure, tested.

- `frontend-customer/src/components/vibey/vibeyCopy.ts`:
  - `VIBEY_TEASERS`: the 5 Romanian teasers from the handoff §4.1 verbatim, plus
    a parallel 5 English. Export `pickTeaser(seed: string, locale: 'ro'|'en')`
    — deterministic hash of `seed` (cart id) → stable index (no `Math.random` so
    it's stable while the guest sits; reuse the hashing style from
    `toneFromKey`).
  - `followUpLine(name, remaining, locale)`: handoff §4.2 templates
    (RO + EN), interpolating item name; branch on `remaining > 0`.

- `frontend-customer/src/stores/useVibeyStore.ts` — Pinia setup store:
  - state: `teaser`, `recs: VibeyRec[]`, `recsLoaded`, `thinking`, `summary`,
    `sheetOpen`, `botMsg`, plus an internal `cartKey` (current cart identity).
  - `ensureTeaser(seed, locale)`: set `teaser`/`botMsg` once per `cartKey`.
  - `openSheet()`: set `sheetOpen=true`; if `recsLoaded` return (NO call); else
    `thinking=true`, `await apiClient.getVibeyRecommendations({items, locale})`
    building `items` from the cart store (`{id,name,category,qty}`; category may
    be `''`), then `recs=res.recommendations`, `summary=res.summary`,
    `botMsg=summary`, `recsLoaded=true`, `thinking=false`. Guard against errors →
    empty recs, calm state.
  - `closeSheet()`.
  - `addRec(id)`: find rec, `cartStore.addRecommendedItem(product)`, remove from
    `recs`, set `botMsg = followUpLine(...)` deterministically (no call); if
    `recs` now empty, `sheetOpen=false`.
  - `hasRecs` / `allAdded` getters; `reset()` clears everything (call on cart
    clear / order-id change).
  - **No watchers that fetch.** Fetch happens only inside `openSheet`.

- Add a vitest (mirror frontend-admin's vitest+MSW setup, or plain unit) for the
  store + copy proving: teaser deterministic & no fetch on load; exactly one
  fetch across openSheet→close→openSheet; addRec makes no fetch and sets the
  follow-up line. This is the automated guard for the cost invariant.

**Files:** `api-client.ts`, `design/contrast.ts`,
`components/vibey/vibeyCopy.ts`, `stores/useVibeyStore.ts`, test files.

---

## Task 4: Vibey components + CartView wiring

**Goal:** Recreate the handoff UI pixel-close using tokens, wired into the cart.
Reference: `White label branding feature (2)/design_handoff_vibey_recommendations/Cart AI Recommendations.dc.html`
(visual/interaction spec only — data is lazy via the store, NOT preloaded).

Theme mapping (via `useTheme().tokens` = `T`): `--acc`=`T.accent`,
`--sec`=`T.lime`, `--bg`=`T.paper`, `--hi`=`T.paperHi`, `--card`=`T.card`,
`--ink*`=`T.ink*`, `--onAcc`=`T.onAccent`, `--onSec`=`bestOn(T.lime)`.

- `components/vibey/VibeyAvatar.vue`: CSS/markup only (no image). Primary-colour
  rounded-square (radius 14, size prop default 44), two on-primary blinking eye
  dots, antenna with a **secondary-colour** tip dot. Animations `botbob` 3.4s,
  `blink` 4s, `ant` 2.4s — all gated behind `@media (prefers-reduced-motion:
  reduce)` (disable). Props: `size`, tokens.

- `components/vibey/VibeyBlock.vue` (always visible in cart): rounded card
  (`T.paperHi` bg, 1px `T.ink12`, radius 22, pad 16). Header row: `VibeyAvatar` +
  name "Vibey" (16px/700) + **primary-colour** status dot with
  `0 0 8px <primary>` glow + subtitle "Asistentul tău de la masă" (RO) /
  "Your assistant at the table" (EN). Speech bubble (`T.card`, radius 16,
  top-left 5): shows `store.thinking` → three `think`-animated dots +
  "analizează coșul…" / "reading your cart…", else `store.botMsg`. Reveal pill
  (`T.lime` bg / `bestOn` text, radius 999, min-height 56, caret chip): label
  "Da, arată-mi"/"Show me" when closed (caret `caretbob` 1.6s), "Ascunde
  ideile"/"Hide ideas" when open (caret rotate 180°). Click → `store.openSheet()`
  / `closeSheet()`. `allAdded` → dashed row: secondary ✓ chip + "Toate ideile lui
  Vibey sunt în coș" / "All of Vibey's ideas are in your cart". Reduced-motion
  gates caret/think.

- `components/vibey/VibeySheet.vue`: bottom sheet over the cart. Scrim
  (`rgba(11,11,13,.45)`, 3px blur, `scrimin` 0.22s) — click closes. Sheet
  (`T.paperHi`, top radius 28, max-height 80%, `sheetup` 0.36s
  `cubic-bezier(.22,1,.36,1)`): grab handle, header (small avatar + "Ideile lui
  Vibey"/"Vibey's ideas" + "Alese pentru ce ai în coș"/"Chosen for what's in your
  cart" + ✕), status line (thinking dots → `store.summary`), rec list. Each rec
  card (`T.card`, radius 18): 56×56 `FoodTile` (use `toneFromKey(rec.id)` +
  `mediaUrl` if a real image later), mono category tag, name, `priceRon` + " RON"
  (via `formatMoney`), **primary** "+ Adaugă"/"+ Add" button → `store.addRec`.
  Reason row below hairline: primary glyph + "Vibey:" bold + `rec.reason`. Cards
  `pop` 0.3s. Show at most 2 at a time (slice(0,2)) per handoff. Reduced-motion
  gates pop/sheetup/bob.

- `CartView.vue` wiring: import store + `VibeyBlock`/`VibeySheet`. `onMounted`
  (or watch cart id) call `store.ensureTeaser(cartIdentity, locale)`. Render
  `<VibeyBlock>` between the `.items` list and `.summary-wrap` (only when
  `cartStore.items.length > 0`). Render `<VibeySheet>` at the PhoneScreen root
  (sibling of the CTA) so it overlays. The existing "Sugerat de AI" pill already
  renders via `isRecommended(it.id)` using `cart.aiSuggested` (now "Sugerat de
  Vibey") and `T.lime` — confirm it stays. Reset Vibey store when the cart is
  cleared / order changes (hook where `resetForNewOrder`/`clearCart` fire, or
  watch `sessionStore.orderId`).

**Cost QA (must verify by reading the wired code + running the store test):**
zero `getVibeyRecommendations` calls on cart open, quantity ±, sheet re-open,
add-item; exactly one on first reveal tap.

**Verify:** `npm run type-check` + `npm run lint` clean. If a dev server / the
`verify` skill is available, drive the cart → tap reveal → confirm one network
call and the sheet renders.

**Files:** `components/vibey/VibeyAvatar.vue`, `VibeyBlock.vue`, `VibeySheet.vue`,
`components/screens/CartView.vue`, possibly `useLocale.ts` (new Vibey label
keys — RO+EN).

---

## After Task 4

Final whole-branch code review, fix Critical/Important findings, then poll
`origin` for `feature/white-label-customization`. When it lands, integrate its
tenant 3-colour token source so Vibey re-skins (Vibey already reads
`useTheme().tokens`, so integration = point those tokens at the tenant palette +
confirm `bestOn` contrast on light and dark tenant backgrounds). Then CR + push.
