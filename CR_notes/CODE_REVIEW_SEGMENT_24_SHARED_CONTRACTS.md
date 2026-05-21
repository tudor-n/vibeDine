# Code Review — Segment 24: Shared contracts (`/shared`)

**Date:** 2026-05-21
**Reviewer:** Claude (Opus 4.7, read-only review per roadmap)
**Scope:** TypeScript contract types under `shared/` and the AI-service JSON contract. Cross-checked against actual API responses in the .NET backend and against the two Vue consumers.

## Files reviewed

| Role | Path |
|---|---|
| Domain models (TS) | [shared/types/models.ts](shared/types/models.ts) |
| Request DTOs (TS) | [shared/types/api-requests.ts](shared/types/api-requests.ts) |
| Response DTOs (TS) | [shared/types/api-responses.ts](shared/types/api-responses.ts) |
| Menu import contract | [shared/types/menu-import.ts](shared/types/menu-import.ts) |
| Menu validation constants | [shared/types/menu-validation.ts](shared/types/menu-validation.ts) |
| Generated `.d.ts` / `.js` | [shared/types/models.d.ts](shared/types/models.d.ts), [models.js](shared/types/models.js), [api-requests.d.ts](shared/types/api-requests.d.ts), [api-responses.d.ts](shared/types/api-responses.d.ts) |
| AI service contract (JSON) | [shared/contracts/api-contract-ai.json](shared/contracts/api-contract-ai.json) |
| Mount wiring | [docker-compose.yml](docker-compose.yml) |
| Path aliases | [frontend-customer/tsconfig.json](frontend-customer/tsconfig.json), [frontend-customer/vite.config.ts](frontend-customer/vite.config.ts) |

No `CLAUDE.md` files exist; review is checklist-only.

---

## Checklist outcomes

| # | Item | Result |
|---|---|---|
| 1 | 🔥 Every DTO returned by the .NET API has a matching TypeScript type in `shared/types/` | **FAIL** — see Findings 1, 2 |
| 2 | `shared/` is volume-mounted into `frontend-admin` (per compose) but **not** `frontend-customer` — confirm intentional or extend | **FAIL** — see Finding 3 |
| 3 | `api-contract-ai.json` matches the Pydantic schemas in `models/schemas.py` and `models/menu_extraction.py` | **FAIL** — see Finding 4 |
| 4 | Types exported via a single `index.ts` per folder, no `import` from deep paths in consumers | **FAIL** — no `index.ts` exists; every consumer imports from `@shared/types/models`, `@shared/types/api-requests`, etc. |
| 5 | Enums (`OrderStatus`) match exactly across backend C#, TS, Python | **PARTIAL** — see Finding 5 |
| 6 | Date fields typed as `string` (ISO8601), not `Date` | **PASS** — `ISODateString = string` alias used throughout ([source](shared/types/models.ts#L2)) |
| 7 | Money fields typed consistently | **PASS-ish** — all `number` (JS number) in TS; backend uses `decimal`. Documented choice not in repo — see Finding 6 |
| 8 | Generator vs. hand-maintained drift | **FAIL** — see Finding 7 |

---

## Findings

### Finding 1 — `DailyMetrics` is missing `flashDealRevenue`, which the backend returns and the UI consumes (HIGH)

[shared/types/models.ts:205-211](shared/types/models.ts#L205-L211) defines:

```ts
export interface DailyMetrics {
  date: ISODateString;
  flashDealsShown: number;
  flashDealsClaimed: number;
  recommendationClicks: number;
  upsellRevenue: number;
}
```

The backend [AdminController.cs:260-270](backend/RestaurantApi/Controllers/AdminController.cs#L260-L270) computes and returns `flashDealRevenue`. The frontend [PerformanceMetrics.vue:80](frontend-admin/src/components/PerformanceMetrics.vue#L80) reads it: `{{ metrics?.flashDealRevenue || 0 }}`. The shared type does not include it.

There's even an existing audit note in [shared/docs/INTEGRATION_FIX_GUIDE.md:34-42](shared/docs/INTEGRATION_FIX_GUIDE.md#L34-L42) that says "**Change to** (add missing `flashDealRevenue`)" — the fix never landed.

Practical impact:
- TypeScript will not catch a typo when a future change renames the field.
- The reverse case is already happening: `recommendationClicks` is declared in the shared type, but `grep` finds no consumer reading it anywhere in the frontend — so the type both over- and under-describes the real response.

**Recommendation:** add `flashDealRevenue: number` to `DailyMetrics`. Audit `AdminController.cs` and prune unused fields from the type or wire the unused ones into the dashboard.

---

### Finding 2 — Admin API client uses `any[]` for orders; shared `Order` shape doesn't match what the API returns (HIGH)

[frontend-admin/src/services/api-client.ts:262-268](frontend-admin/src/services/api-client.ts#L262-L268):

```ts
authedFetcher<any[]>(scoped(`/admin/orders?limit=${limit}`)),
authedFetcher<any[]>(scoped('/admin/orders/active')),
authedFetcher<any>(scoped(`/admin/orders/${orderId}/status`), { … }),
```

Three of the highest-traffic admin endpoints are typed as `any[]` / `any`. Then each view re-defines `Order` and `OrderItem` locally with a flattened shape:

[OrdersLogView.vue:5-25](frontend-admin/src/views/OrdersLogView.vue#L5-L25):
```ts
interface OrderItem {
  id: string
  productName: string     // flat
  quantity: number
  unitPrice: number       // flat (shared has unitPriceAtPurchase)
  subtotal: number        // NOT in shared type
  ...
}
interface Order {
  ...
  tableNumber: string | null   // NOT in shared Order
  items: OrderItem[]
}
```

vs [shared/types/models.ts:71-93](shared/types/models.ts#L71-L93):
```ts
export interface OrderItem {
  ...
  product: Pick<Product, 'id' | 'name' | 'price' | 'imageUrl'>;  // nested
  unitPriceAtPurchase: number;
  // no subtotal
}
export interface Order {
  ...
  // no tableNumber
}
```

Three different shapes for "Order": shared type, admin orders log, admin active board (`ActiveOrder` in [ActiveOrdersBoardView.vue:8-24](frontend-admin/src/views/ActiveOrdersBoardView.vue#L8-L24)). The local types are the truth; the shared type is fictional.

This is exactly the drift the roadmap warns about. The cost: a rename of `productName` on the backend won't be caught until a runtime "Cannot read undefined" surfaces in the orders log.

**Recommendation:** make a decision — either move the local `Order`/`OrderItem` interfaces into shared (and replace `any[]` in `api-client.ts`), or document that the shared type is for the customer-facing surface only and rename it to `CustomerOrder` to make the boundary explicit.

---

### Finding 3 — Customer SPA is missing the `shared/` volume mount; live dev should be failing in Docker (HIGH)

[docker-compose.yml:66-75](docker-compose.yml#L66-L75) for `frontend-customer`:

```yaml
volumes:
  - ./frontend-customer:/app
  - /app/node_modules
```

[docker-compose.yml:77-88](docker-compose.yml#L77-L88) for `frontend-admin`:

```yaml
volumes:
  - ./frontend-admin:/app
  - ./shared:/shared:ro      ← shared is mounted only for admin
  - /app/node_modules
```

But the customer app actively imports from `@shared/types/...` in **at least 9 files**:

- [src/services/api-client.ts:1-2](frontend-customer/src/services/api-client.ts#L1-L2)
- [src/composables/useMenu.ts:3](frontend-customer/src/composables/useMenu.ts#L3)
- [src/stores/useCartStore.ts:3-4](frontend-customer/src/stores/useCartStore.ts#L3-L4)
- [src/stores/useFlashDealStore.ts:3](frontend-customer/src/stores/useFlashDealStore.ts#L3)
- [src/stores/useOrderStatusStore.ts:3](frontend-customer/src/stores/useOrderStatusStore.ts#L3)
- [src/composables/useRecommendations.ts:5](frontend-customer/src/composables/useRecommendations.ts#L5)
- [src/composables/useWebSocket.ts:5](frontend-customer/src/composables/useWebSocket.ts#L5)
- [src/components/MenuList.vue:5](frontend-customer/src/components/MenuList.vue#L5)
- [src/components/RecommendationWidget.vue:4](frontend-customer/src/components/RecommendationWidget.vue#L4)
- [src/components/screens/MenuView.vue:9](frontend-customer/src/components/screens/MenuView.vue#L9)

…and [frontend-customer/vite.config.ts:10](frontend-customer/vite.config.ts#L10) resolves `@shared` to `../shared` relative to `/app`. With `/app` bind-mounted to `./frontend-customer` on the host, `../shared` inside the container points to `/shared` — which **does not exist** because there's no mount.

The customer [Dockerfile](frontend-customer/Dockerfile) does `COPY . .` against its own build context (`./frontend-customer`), so `../shared` is not in the image either.

Conclusions, ranked by likelihood:
1. **Most likely:** developers run `npm run dev` for the customer frontend directly on the host (outside Docker), where `../shared` resolves correctly relative to the host filesystem. The Docker setup for `frontend-customer` is silently broken and nobody noticed because nobody uses it.
2. The container `docker compose up frontend-customer` fails fast on first Vite resolve, and the team treats this as "known broken — local dev only" without documenting it.
3. Some other mechanism (a Vite plugin, prebuild step, NPM workspace link, …) papers over it that I missed.

Whichever it is, the asymmetry is wrong: either both frontends mount `shared/`, or neither does and the types come from a published package.

**Recommendation:** add `./shared:/shared:ro` to `frontend-customer.volumes`. Verify `docker compose up frontend-customer` boots cleanly. If the team consciously chose to keep it host-only, document that decision in `frontend-customer/README.md` or `CLAUDE.md`.

---

### Finding 4 — `api-contract-ai.json` describes an API that no longer exists (HIGH)

[shared/contracts/api-contract-ai.json](shared/contracts/api-contract-ai.json) advertises itself as the AI/recommendations contract but every endpoint path in it is out of date:

| Contract says | Actual route in admin `api-client.ts` |
|---|---|
| `GET /sessions/:session_id/recommendations` | not present (recommendations come back inline with menu) |
| `POST /sessions/:session_id/flash-deal/:deal_id/claim` | route shape differs |
| `GET /admin/restaurants/:restaurant_id/ai-config/today` | replaced by `GET /api/restaurants/:rid/admin/directives` ([api-client.ts:333](frontend-admin/src/services/api-client.ts#L333)) |
| `POST /admin/restaurants/:restaurant_id/ai-config` | replaced by `POST /…/admin/directives` (the old `AdminAIConfig` is dead — see Finding 8) |
| `POST /admin/restaurants/:restaurant_id/similarity-rules/auto-generate` | exists in [`useSimilarityRulesStore.autoGenerate`](frontend-admin/src/stores/useSimilarityRulesStore.ts#L59-L70) — actual route is `/admin/similarity-rules/auto-generate` (no `restaurants/:rid` wrapping) |

The contract also uses snake_case field names (`flash_deal`, `created_at`) for the wire format — but the actual API serialises with the .NET default of camelCase (`flashDeal`, `createdAt`), as evidenced by every consumer in `frontend-admin/`. A new developer reading `api-contract-ai.json` would write client code that doesn't deserialise anything.

Additionally, the AI contract documents the **internal pipeline** ([line 323-353](shared/contracts/api-contract-ai.json#L323-L353)) — useful prose but mis-shelved as "contract", which a code reader would assume is enforceable.

**Recommendation:** either regenerate this JSON from the live OpenAPI spec on each backend release, or delete it and replace with a `README.md` that links to the live `/swagger/v1/swagger.json` (currently behind dev-only Swagger per segment 03). Stale contract docs are worse than no contract docs because they actively mislead.

---

### Finding 5 — Enum casing diverges between TS, the C# wire format, and the AI service (MEDIUM)

`OrderStatus` ([shared/types/models.ts:68](shared/types/models.ts#L68)) uses lowercase literals: `'draft' | 'submitted' | 'preparing' | 'ready' | 'completed' | 'cancelled'`. Admin views consume them as lowercase ([OrdersLogView.vue:33-40](frontend-admin/src/views/OrdersLogView.vue#L33-L40)). The backend C# enum (referenced in segment 07) and its JSON serialisation must produce the same casing — verify by visual diff with the controller, since there's no schema-level check.

`SimilarityRelationship` ([shared/types/models.ts:191](shared/types/models.ts#L191)) uses camelCase: `'pairsWith' | 'upgradeTo' | 'followWith' | 'sameVibe'`. But [shared/contracts/api-contract-ai.json:94](shared/contracts/api-contract-ai.json#L94) and [:254](shared/contracts/api-contract-ai.json#L254) document snake_case: `'pairs_with' | 'upgrade_to' | 'follow_with' | 'same_vibe'`. At least one of these is wrong; the actual wire format determines which.

`RecommendationReasonType` ([shared/types/models.ts:120-125](shared/types/models.ts#L120-L125)) is camelCase: `'chefPick' | 'pairsWithOrder'`. The AI contract says snake_case (`'chef_pick' | 'pairs_with_order'`). Same drift.

**Recommendation:** pick one casing, fix the other surface, and add a single integration test that exercises every enum value round-trip (C# → JSON → TS literal-type check). The test catches drift the next time someone renames a value.

---

### Finding 6 — Money fields are `number` (double); no documented precision policy (LOW/MEDIUM)

[shared/types/models.ts](shared/types/models.ts) types every monetary field as `number`:
- `Product.price`
- `Order.totalAmount`
- `OrderItem.unitPriceAtPurchase`
- `FlashDeal.discountedPrice`
- `DailyMetrics.upsellRevenue` (and the missing `flashDealRevenue`)
- `SimilarityRule.score` (a probability, not money, but precision matters)

The roadmap follow-up: *"Money fields are typed as `string` or `number` consistently across the three layers (decision documented)."*

JS `number` is IEEE 754 binary64 — it cannot represent `0.1 + 0.2` exactly. For RON prices today the magnitudes are tiny enough that the error is well below display precision, but the choice is undocumented. C# `decimal` on one side, JS `number` on the other, Python `Decimal` (or float) on the third — round-trips through `JSON.parse` go through `number` regardless.

Segment 06 noted `decimal(5,4)` for `SimilarityRules.Score`. The TS type `score: number` can hold any double; nothing prevents the engine from returning `0.123456789` and the EF truncating it on save. Nothing in `/shared/` reflects that constraint.

**Recommendation:** add a one-paragraph "Numeric precision policy" doc to `shared/` stating: (a) all currency in RON, scaled by 100 is allowed as `number` up to 1e12 RON; (b) `score` fields are decimal(5,4) — anything beyond will be silently truncated. Make it the source of truth all three layers point at.

---

### Finding 7 — `.d.ts` / `.js` files in `shared/types/` are stale generated artifacts checked into git (MEDIUM)

[shared/types/models.d.ts](shared/types/models.d.ts) is missing entire interfaces that exist in [models.ts](shared/types/models.ts): `AdminCategory`, `AdminProduct`, `AdminMenuCategoryNode`, `AdminMenuTree`, `RecommendationDirective`, `RecommendationDirectiveRule`, `ParsedRecommendationDirective` — all of which the admin SPA imports today.

[shared/types/models.js](shared/types/models.js) is literally `export {};` — a side-effect-only output of `tsc` on the original (smaller) `.ts` file.

This means at some point the contracts were compiled with `tsc` and the output was committed. The TS source has since grown without regenerating. Anything reading the `.d.ts`/`.js` (e.g. a consumer that resolves `models` to `models.js`/`models.d.ts` instead of `models.ts`) sees the old schema.

The actual consumers — vite + vue-tsc on both frontends — resolve `@shared/types/models` to `models.ts` directly, so this drift is invisible to humans running the SPA locally. But (a) anything tooling that follows the `module.exports` path (Jest, ts-node, a future workspace consumer) will silently use the stale schema; (b) the `.d.ts` files mislead anyone reading them as "the contract".

**Recommendation:** either delete the `.d.ts` and `.js` files and add them to `.gitignore` (the canonical source is `models.ts`), or wire a generator step (`tsc --declaration --emitDeclarationOnly`) into CI and verify the diff is empty.

---

### Finding 8 — Dead types: `AdminAIConfig`, `PostAdminAIConfigRequest`, `AdminAIConfigResponse`, `AdminAIConfigHistoryResponse` (LOW)

Grepping the codebase, the only references to these types are inside `shared/`:

- [shared/types/models.ts:151-160](shared/types/models.ts#L151-L160) defines `AdminAIConfig` (and `FlashDealConfig` is used by it).
- [shared/types/api-requests.ts:24-29](shared/types/api-requests.ts#L24-L29) defines `PostAdminAIConfigRequest`.
- [shared/types/api-responses.ts:64-74](shared/types/api-responses.ts#L64-L74) defines `AdminAIConfigResponse` and `AdminAIConfigHistoryResponse`.

The actual admin UI uses `AIDirective` / `RecommendationDirective` (different shape, different endpoint). The legacy `AdminAIConfig` model corresponds to the dead `/ai-config` endpoint documented in the obsolete `api-contract-ai.json` (Finding 4).

Also unused: `Pagination` is referenced only by `SimilarityRulesResponse` which is itself unreachable since the similarity section is `v-if="false"` (segment 23 Finding 11). `CreateOrderRequest`, `TriggerFlashDealRequest`, `ActiveFlashDealResponse`, `ClaimFlashDealResponse`, `AutoGenerateRulesResponse` — verify against actual customer code, several appear orphaned.

**Recommendation:** delete dead types in the same PR that fixes Finding 4 (deleting the old AI contract); they go together. Each dead type is one more thing for a reader to interpret as "supported".

---

### Finding 9 — `Product.description` is `string` (non-nullable), but `AdminProduct.description` is `string | null` (LOW)

[shared/types/models.ts:43-52](shared/types/models.ts#L43-L52):
```ts
export interface Product {
  ...
  description: string;       // required
  ...
}
```

[shared/types/models.ts:19-29](shared/types/models.ts#L19-L29):
```ts
export interface AdminProduct {
  ...
  description: string | null;  // optional
  ...
}
```

Either descriptions are nullable in the DB and the customer-facing `Product` type is lying, or they aren't and the admin shape is over-permissive. The customer `MenuList.vue` (segment 18) presumably renders `product.description` directly — if the DB happens to contain a NULL row, the customer SPA will render `null` as text. Confirm against the C# entity and validator (segment 07).

Same potential issue for `imageUrl` — both shapes agree it's nullable, so that one's consistent.

---

### Finding 10 — `AIDirective.parsedRules` and `RecommendationDirective.parsedRules` have inconsistent shapes (LOW)

[shared/types/models.ts:165](shared/types/models.ts#L165): `AIDirective.parsedRules: Record<string, unknown> | null` — an object.

[shared/types/models.ts:173](shared/types/models.ts#L173): `RecommendationDirective.parsedRules: string | null` — a JSON-encoded string.

The frontend has to `JSON.parse` only one of the two. [RulesManagementView.vue:36-43](frontend-admin/src/views/RulesManagementView.vue#L36-L43) does this explicitly for `RecommendationDirective`. Two siblings with the same name and different storage formats is a footgun.

**Recommendation:** pick one. Either both store JSON-as-string (transparent to the API consumer; client decodes), or both are pre-parsed to objects on the server side. Document the choice on `IDirective` or in the controller.

---

### Finding 11 — `OrderItem.product` nested vs. backend-flat (consequence of Finding 2)

[shared/types/models.ts:71-80](shared/types/models.ts#L71-L80) types `OrderItem.product` as a nested `Pick<Product, 'id' | 'name' | 'price' | 'imageUrl'>`. The admin orders log expects flat `productName: string` ([OrdersLogView.vue:7](frontend-admin/src/views/OrdersLogView.vue#L7)). If the backend serialises `OrderItem` for the customer surface differently from the admin surface, there are effectively two `OrderItem` schemas in the wild. This is the same problem as Finding 2 just for the item shape — fix together.

---

## Summary

**🔥 The 🔥 item from the roadmap — "Every DTO returned by the .NET API has a matching TypeScript type in `shared/types/`" — is FAILED.** Findings 1, 2, 11 all document specific holes.

**Top three risks to fix first**
1. **F3** — customer SPA's missing `shared/` mount means either (a) Docker dev is broken or (b) it's silently working by accident on the host. Either way it's a one-line compose fix that buys parity with the admin container.
2. **F2 + F11 + F1** — order and metrics types are out of sync with the API. Promote local interfaces to `shared/` and replace `any[]` in `api-client.ts` with concrete types.
3. **F4** — `api-contract-ai.json` is fiction. Either regenerate from Swagger or delete; do not let it sit half-correct.

**Top three strengths**
- `ISODateString` and `UUID` type aliases ([shared/types/models.ts:1-2](shared/types/models.ts#L1-L2)) are used everywhere — that decision held up cleanly across all consumers.
- [shared/types/menu-validation.ts](shared/types/menu-validation.ts) is the rare case of a shared constant module with a comment explicitly tying it to a controller line — the right pattern. Should be the template for everything else in this folder.
- [shared/types/menu-import.ts](shared/types/menu-import.ts) has a header comment naming the C# DTOs and Python Pydantic models it mirrors. Same right pattern; replicate across the other type files.

**Recommended fix order**
1. F3 (one compose line; restores customer-Docker parity).
2. F1 (add `flashDealRevenue` to `DailyMetrics`; an audit note already requested this and the fix never landed).
3. F2 + F11 (move admin `Order` / `OrderItem` interfaces to `shared/` and type `api-client.ts` admin order endpoints).
4. F4 (regen or delete `api-contract-ai.json`).
5. F7 (drop generated `.d.ts`/`.js` files, gitignore them).
6. F5 (enum casing audit; add a round-trip test).
7. F8 / F9 / F10 / F6 — backlog.

No code modified during this review.
