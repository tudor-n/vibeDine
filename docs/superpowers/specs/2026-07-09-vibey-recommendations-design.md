# Design: Vibey — in-cart AI recommendations

**Status:** approved (autonomous execution per user directive)
**Source handoff:** `White label branding feature (2)/design_handoff_vibey_recommendations/`

Replaces the old eager recommendation feature with **Vibey**, a table-side
assistant that lives inside the customer cart. Vibey shows a deterministic
teaser line; only when the guest taps **"Da, arată-mi"** does it fire a single
AI request that returns 2–3 complementary items with a one-sentence *reason*
each plus a summary line. White-label aware: it consumes the tenant's
primary/secondary/background palette (no new brand colours).

## Cost invariant (the #1 rule)

- **Zero** model calls on: cart load, quantity change, sheet re-open, add-item.
- **Exactly one** model call on the first `Da, arată-mi` tap; cached for the
  cart-version so re-opening is free.
- Teaser + post-add follow-up lines are **deterministic client-side copy** — no
  network.

## Architecture

Three-service slice, following existing patterns.

### Backend

**Python `ai-scripts` (`/recommend`)** — extend to also return a `summary`
(one warm Romanian/English sentence introducing the picks) alongside the
existing `recommendations[]` (each `{product_id, reason, confidence}`). Prompt
asks for 2–3 picks + reasons + summary in `language`. Failure → `{"summary":"",
"recommendations": []}` (graceful, never 500 into the guest).

**.NET `RestaurantApi`** —
- **Remove** the old eager endpoint `GET .../orders/{id}/recommendations`
  (returned `pairings/upsells/flashDeal`; the flashDeal path is redundant with
  the `FlashDealAvailable` SignalR event the client already handles).
- **Add** `POST api/restaurants/{restaurantId:guid}/recommendations`
  (anonymous customer endpoint, tenant-scoped via the `restaurantId` route like
  every other customer endpoint — **never** trust a client-supplied
  restaurantId in the body).
  - Request body: `{ items: [{ id, name, category, qty }], locale }`.
  - Server selects candidates (in-stock, not already in cart) reusing the
    existing rule-driven directive logic + Python call, enriches each returned
    rec with `name`, `category`, `priceRon` from the DB, and returns
    `{ summary, recommendations: [{ id, name, category, priceRon, reason }] }`,
    **max 3**.
  - Graceful empty `{ summary:"", recommendations:[] }` on any failure.
  - Short in-memory cache (`IMemoryCache`, ~2 min) keyed by
    `(restaurantId, sorted item ids, locale)` so identical carts don't re-hit
    the model.
- Keep `RecommendationService`'s candidate selection + rule-driven directive
  matching (reused server-side); add a cart-based method that does not require
  an order row. Keep `RecommendationDirective`, admin directive endpoints, and
  the `wasAiRecommended` cart-sync field untouched.

### Contract (invariant 2 — full-stack slice)

New wire types synced in three places:
- C# DTOs: `DTO/Requests/VibeyRecommendationRequest.cs`,
  `DTO/Responses/VibeyRecommendationResponse.cs`.
- `shared/types`: `VibeyRecommendationRequest` / `VibeyRecommendationResponse`
  in `api-requests.ts` / `api-responses.ts` (+ the `Rec` item shape). Edit the
  `.ts`, not the committed generated `.js`/`.d.ts`.
- `frontend-customer` api-client method `getVibeyRecommendations`.

### Frontend (`frontend-customer`)

- **Remove old UI/plumbing:** the recs strip in `MenuView.vue`,
  `composables/useRecommendations.ts`, dead `components/RecommendationWidget.vue`,
  `apiClient.getRecommendations`, and i18n keys `menu.recommendedForYou` /
  `menu.addRecommendation`. Keep `cartStore.recommendedItemIds` /
  `wasRecommended` / `addRecommendedItem` (reused for the Vibey badge + backend
  `wasAiRecommended` sync); repurpose `cart.aiSuggested` → "Sugerat de Vibey".
- **`stores/useVibeyStore.ts`** (Pinia setup store): `teaser`, `recs`,
  `recsLoaded`, `thinking`, `summary`, `sheetOpen`, `botMsg`; actions
  `openSheet()` (lazy fetch guarded by `recsLoaded`), `closeSheet()`,
  `addRec(id)` (adds to cart via `cartStore.addRecommendedItem` + sets a
  deterministic follow-up line, no call), `reset()`. Teaser chosen once per
  cart identity; store resets when the cart is cleared / order id changes.
- **`vibey/vibeyCopy.ts`**: `VIBEY_TEASERS` (5 RO + EN), post-add follow-up
  templates, all deterministic. `pickTeaser(seed)` stable per cart id.
- **Components** (CSS-only avatar, no assets):
  `components/vibey/VibeyAvatar.vue`, `VibeyBlock.vue`, `VibeySheet.vue`.
- **Wire into `CartView.vue`** between the cart items and the summary card;
  "Sugerat de Vibey" badge (secondary colour) on items added via Vibey.

### Theme mapping (white-label ready)

Vibey consumes `useTheme().tokens` (`T`/`TD`) — no hard-coded brand colours:

| Design var | Token |
|---|---|
| `--acc` (primary) | `T.accent` |
| `--sec` (secondary / antenna leaf / badge) | `T.lime` |
| `--bg`/`--hi`/`--card` | `T.paper` / `T.paperHi` / `T.card` |
| `--ink*` | `T.ink*` |
| `--onAcc` | `T.onAccent` |
| `--onSec` | computed at runtime via `bestOn(hex)` contrast helper |

Add `design/contrast.ts` with `bestOn(hex)` (relative-luminance pick of
light/dark foreground, mirroring the prototype `theme()`), used for the
on-secondary colour so it stays correct for any tenant secondary. Because Vibey
reads tokens through `useTheme`, when `feature/white-label-customization` makes
those tokens tenant-driven, Vibey re-skins automatically — the only follow-up is
wiring the tenant colours into the token source (tracked as the white-label
integration step).

## Interactions

Cart load → teaser, no network. Tap → sheet slides up, thinking dots, one
`POST`, dots→summary, cards `pop` in. Re-open → cached, no call. `+ Adaugă` →
item to cart w/ badge, leaves list, deterministic follow-up, no call. All added
→ dashed "Toate ideile lui Vibey sunt în coș". Reduced-motion gates
bob/blink/ant/caret/think animations.

## Task breakdown

1. **Backend slice** — Python summary; .NET remove old endpoint + add POST
   `/recommendations` + cache + `RecommendationService` cart method; C# DTOs;
   `shared/types`. (No .NET integration tests exist for recs; add a focused test
   for the new endpoint's tenant scoping + graceful-empty if feasible.)
2. **Frontend removal** — strip old recs from `MenuView`, api-client, dead file,
   i18n; repurpose `cart.aiSuggested`.
3. **Vibey store + copy + api-client method** — `useVibeyStore`, `vibeyCopy`,
   `contrast.ts`, `getVibeyRecommendations`.
4. **Vibey components + CartView wiring** — Avatar/Block/Sheet, badge, reduced
   motion, cost QA.

## Out of scope

Payments, admin-side Vibey config, changing the AI provider/model, the
white-label token *source* (arrives on `feature/white-label-customization`;
integrated after core Vibey lands).
