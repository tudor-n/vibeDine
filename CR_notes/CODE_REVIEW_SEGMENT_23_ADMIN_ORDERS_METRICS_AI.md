# Code Review — Segment 23: Admin SPA · Orders, Metrics, AI Config

**Date:** 2026-05-21
**Reviewer:** Claude (Opus 4.7, read-only review per roadmap)
**Scope:** Admin SPA views and stores for the active-orders kanban, finalized-orders log, AI directive editor, recommendation rule manager, and performance metrics widget.

## Files reviewed

| Role | Path |
|---|---|
| Orders log (history) | [frontend-admin/src/views/OrdersLogView.vue](frontend-admin/src/views/OrdersLogView.vue) |
| Active orders kanban | [frontend-admin/src/views/ActiveOrdersBoardView.vue](frontend-admin/src/views/ActiveOrdersBoardView.vue) |
| AI directive editor | [frontend-admin/src/views/AIConfigView.vue](frontend-admin/src/views/AIConfigView.vue) |
| Recommendation rules manager | [frontend-admin/src/views/RulesManagementView.vue](frontend-admin/src/views/RulesManagementView.vue) |
| Daily prompt input | [frontend-admin/src/components/DailyPromptInput.vue](frontend-admin/src/components/DailyPromptInput.vue) |
| Recommendation rule input | [frontend-admin/src/components/RecommendationRuleInput.vue](frontend-admin/src/components/RecommendationRuleInput.vue) |
| Similarity rule table | [frontend-admin/src/components/SimilarityRuleTable.vue](frontend-admin/src/components/SimilarityRuleTable.vue) |
| Performance metrics widget | [frontend-admin/src/components/PerformanceMetrics.vue](frontend-admin/src/components/PerformanceMetrics.vue) |
| Dashboard (metrics consumer) | [frontend-admin/src/views/DashboardView.vue](frontend-admin/src/views/DashboardView.vue) |
| AI directives store | [frontend-admin/src/stores/useDirectivesStore.ts](frontend-admin/src/stores/useDirectivesStore.ts) |
| Manual rec rules store | [frontend-admin/src/stores/useRecommendationDirectivesStore.ts](frontend-admin/src/stores/useRecommendationDirectivesStore.ts) |
| Similarity rules store | [frontend-admin/src/stores/useSimilarityRulesStore.ts](frontend-admin/src/stores/useSimilarityRulesStore.ts) |
| Metrics store | [frontend-admin/src/stores/useMetricsStore.ts](frontend-admin/src/stores/useMetricsStore.ts) |

No `CLAUDE.md` files exist in the repo — review is checklist-only.

---

## Checklist outcomes

| # | Item | Result |
|---|---|---|
| 1 | `ActiveOrdersBoard` polls or subscribes via SignalR — confirm which, and document acceptable staleness | **POLLS (5s)** — see Finding 1 |
| 2 | Status transitions on the board reflect target state (no double "are you sure") | **PASS** — drag-and-drop, no confirm prompt; `onDrop` rolls back on API error ([source](frontend-admin/src/views/ActiveOrdersBoardView.vue#L98-L123)) |
| 3 | AI directive submission: user can preview the parsed rules before they're saved | **FAIL** — see Finding 2 |
| 4 | Directive deletion has a confirmation modal | **PARTIAL** — see Finding 3 |
| 5 | Date-range filters on metrics use the restaurant's timezone, not the browser's | **FAIL** — see Finding 4 |
| 6 | Empty states (no orders, no rules) are designed, not blank pages | **PASS** — every list has a styled empty card ([OrdersLogView.vue#L176-L183](frontend-admin/src/views/OrdersLogView.vue#L176-L183), [RulesManagementView.vue#L176-L181](frontend-admin/src/views/RulesManagementView.vue#L176-L181), [SimilarityRuleTable.vue#L28-L39](frontend-admin/src/components/SimilarityRuleTable.vue#L28-L39)) |
| 7 | Error states have actionable next steps ("retry", "check internet"), not raw error codes | **PARTIAL** — see Finding 5 |
| 8 | QR code generation on tables view uses `VITE_CUSTOMER_URL` env, not hardcoded `localhost:3000` | **PASS** — but has a localhost fallback ([TablesView.vue:14-15](frontend-admin/src/views/TablesView.vue#L14-L15)) — acceptable for dev |
| 9 | Follow-up: AI config page when AI service is down | **OPEN** — see Finding 6 |

Additional cross-cutting issues observed while reading the files appear as Findings 7–11.

---

## Findings

### Finding 1 — Active orders board polls every 5 s; no SignalR, no visibility-aware throttling (MEDIUM)

[ActiveOrdersBoardView.vue:127](frontend-admin/src/views/ActiveOrdersBoardView.vue#L125-L133) wires a hard-coded 5-second poll:

```ts
onMounted(() => {
  fetchActive()
  pollTimer = window.setInterval(fetchActive, 5000)
  tickTimer = window.setInterval(() => (now.value = Date.now()), 1000)
})
```

Three consequences:

1. **Stale state during concurrent edits.** A drag-drop on tablet A and another on tablet B can fight: each tablet sees its own optimistic state for up to 5 s before the next poll reconciles to the server's last-write-wins outcome. Cards can visibly snap back to the original column after the user is sure they moved it.
2. **Burns requests on a hidden tab.** There is no `document.visibilitychange` handler. A manager who leaves the board open on a wall monitor — or just an admin with the tab in the background — keeps hitting `/admin/orders/active` every 5 s. With 50 concurrent admin sessions across customers, that's 10 req/s of background load doing no useful work.
3. **`secondsLeft` countdown depends on `now` which only ticks while the tab is visible** — but the poll itself runs regardless. The countdown isn't a correctness issue (it just freezes), it's the burned bandwidth that matters.

Same critique applies to [useMetricsStore.ts:31-44](frontend-admin/src/stores/useMetricsStore.ts#L31-L44) which polls metrics every 30 s with no visibility check; the metrics endpoint is on the always-mounted Dashboard, so it polls forever per logged-in admin.

**Recommendation:** (a) gate polling on `document.visibilityState === 'visible'`, (b) treat `OrderStatusUpdated` from `StaffHub` (segment 12) as the primary signal and polling as a fallback for missed events, (c) drop poll cadence to 15 s once events are wired.

---

### Finding 2 — AI directive submission has no preview-before-save (MEDIUM)

The roadmap explicitly requires: *"AI directive submission: user can preview the parsed rules before they're saved."*

Today both directive submission paths go straight from prompt → API → store insert with no intermediate review:

- [AIConfigView.vue:15-22](frontend-admin/src/views/AIConfigView.vue#L15-L22): `handleNewPrompt` calls `directivesStore.postDirective(prompt)`, then `toast.success`.
- [RulesManagementView.vue:49-57](frontend-admin/src/views/RulesManagementView.vue#L49-L57): identical pattern for manual rec rules.

Only AFTER the directive lands in the database does the user see the AI-parsed rules (`parseDirective(d)` in [RulesManagementView.vue:36-43](frontend-admin/src/views/RulesManagementView.vue#L36-L43)). If the parse is wrong the user has to pause/delete and try again — each retry being another paid LLM call.

**Recommendation:** add a "Preview reguli" step that runs the AI parse against the prompt and renders the would-be `ParsedRecommendationDirective` for confirmation before the user commits. Persist only on the second click. The frontend code is already shaped for it (the parser output is already a structured object — see [RulesManagementView.vue:223-258](frontend-admin/src/views/RulesManagementView.vue#L223-L258)).

---

### Finding 3 — Directive deletion uses native `window.confirm`, not a styled modal (LOW)

[RulesManagementView.vue:69](frontend-admin/src/views/RulesManagementView.vue#L69) and [line 79](frontend-admin/src/views/RulesManagementView.vue#L79):

```ts
if (!window.confirm('Ești sigur că vrei să ștergi această regulă scrisă?')) return
```

Functionally correct, but the rest of the admin app uses Teleport modals (see [MenuManagementView.vue#L499-L564](frontend-admin/src/views/MenuManagementView.vue#L499-L564)). Mixing native confirm dialogs into a heavily styled UI is jarring and looks unfinished. Also: `window.confirm` is blocking and cannot be intercepted by Cypress/Playwright tests without extra plumbing.

The roadmap item literally says "modal", not "confirm". Treat as polish.

---

### Finding 4 — Times are rendered with browser locale/timezone, not restaurant timezone (MEDIUM)

The `Restaurant` model carries a `timezone` field ([api-client.ts:78](frontend-admin/src/services/api-client.ts#L78)). None of the three places that render order timestamps respect it:

- [OrdersLogView.vue:53-58](frontend-admin/src/views/OrdersLogView.vue#L53-L58): `d.toLocaleDateString('ro-RO', …)` and `d.toLocaleTimeString('ro-RO', …)` use the **browser's** local time. An admin reviewing the day's orders from London sees orders timestamped two hours behind the restaurant's reality.
- [DashboardView.vue:18-21](frontend-admin/src/views/DashboardView.vue#L18-L21): `clockLabel` derives the "current hour" from `new Date()` with no timezone — same issue.
- [ActiveOrdersBoardView.vue:69-73](frontend-admin/src/views/ActiveOrdersBoardView.vue#L69-L73): `secondsLeft` uses `now.value - readyAt`, both `Date`-derived → numerically correct since both are UTC ms, but if `readyAt` is null in the server response the countdown silently never shows.

There are no date-range filters on metrics — `apiClient.getMetrics()` ([api-client.ts:258-259](frontend-admin/src/services/api-client.ts#L258-L259)) hits `/admin/metrics` with no query string. So the checklist item about "Date-range filters on metrics use the restaurant's timezone, not the browser's" is moot in the negative: there are no filters to get wrong yet. When date ranges are added, they will need to include `timezone` in the query so the backend bucket-cuts in the restaurant's clock.

**Recommendation:** thread `restaurant.timezone` through the `useAuthStore` (it's available on `RestaurantSummary`), build an `Intl.DateTimeFormat` with `{ timeZone: restaurant.timezone }`, and use it everywhere date/time is rendered.

---

### Finding 5 — Error states show messages but no retry affordance on AI/Rules pages (LOW)

The roadmap requires: *"Error states have actionable next steps ('retry', 'check internet', 'contact support'), not raw error codes."*

Present:
- [OrdersLogView.vue#L136-L144](frontend-admin/src/views/OrdersLogView.vue#L136-L144) has a "Reîncarcă" button.
- [ActiveOrdersBoardView.vue#L143-L146](frontend-admin/src/views/ActiveOrdersBoardView.vue#L143-L146) has a "Reîncarcă" button.

Missing:
- [AIConfigView.vue](frontend-admin/src/views/AIConfigView.vue): no error UI at all (see Finding 7 below — the store swallows errors).
- [RulesManagementView.vue#L135-L144](frontend-admin/src/views/RulesManagementView.vue#L135-L144) renders the store error string but no retry button — user must reload the route to retry.
- [DashboardView.vue#L51-L61](frontend-admin/src/views/DashboardView.vue#L51-L61) shows `metricsStore.error` but no retry; metrics will re-poll automatically every 30 s though, so this is mostly self-healing.

Low priority; flag as polish.

---

### Finding 6 — AI service outages produce a generic toast with no degraded-mode UI (MEDIUM)

The roadmap follow-up asks: *"When the AI service is down, what does the AI config page do? Does it show stale rules and a banner, or refuse to load?"*

Walking the failure path:

1. User types a directive in [AIConfigView.vue](frontend-admin/src/views/AIConfigView.vue) and hits "Generează reguli".
2. `directivesStore.postDirective` calls `apiClient.postDirective(prompt)` ([useDirectivesStore.ts:26-40](frontend-admin/src/stores/useDirectivesStore.ts#L26-L40)).
3. The backend forwards to the AI service via `MenuExtractionClient` (segment 10). If the AI is unreachable the backend returns 500 or 503.
4. The store catches, logs, re-throws.
5. The view catches the throw and calls `toast.error('A apărut o eroare la salvarea directivei.')` ([AIConfigView.vue:18-21](frontend-admin/src/views/AIConfigView.vue#L18-L21)).

So the user is told only "an error occurred" — no signal that the AI is offline, no indication their prompt wasn't saved, no advice on what to do. Worse, the **existing** active directive ([AIConfigView.vue#L79-L88](frontend-admin/src/views/AIConfigView.vue#L79-L88)) still renders as "în direct" so the admin may believe the system is healthy.

**Recommendation:** the toast/banner should distinguish 5xx from 4xx and surface "AI offline — recomandările se opresc temporar la nivel global" with a backend-supplied retry-after if available. The active directive card should add a muted "AI indisponibil acum" badge when fetch is currently failing.

---

### Finding 7 — `useDirectivesStore.fetchDirectives` and `toggleActive` swallow errors silently (MEDIUM)

[useDirectivesStore.ts:15-24](frontend-admin/src/stores/useDirectivesStore.ts#L15-L24):

```ts
async function fetchDirectives() {
    try{
        isLoading.value = true;
        directives.value = await apiClient.getDirectives();
    }catch(error){
        console.error('Eroare la aducerea directivelor:', error);
    }finally{
        isLoading.value = false;
    }
}
```

`error` is logged but no `error.value` field is exposed. The view ([AIConfigView.vue](frontend-admin/src/views/AIConfigView.vue)) has no error UI — if `getDirectives` returns 401/500/timeout, the page just renders "Nicio directivă AI nu este activă" which is indistinguishable from "the list is genuinely empty". The admin will not know they need to log in again or that the backend is down.

`toggleActive` ([line 42-50](frontend-admin/src/stores/useDirectivesStore.ts#L42-L50)) has the same shape and does not re-throw — the consumer's `try/catch` for `toast.error` would never trigger.

Compare with the sibling [useRecommendationDirectivesStore.ts:42-51](frontend-admin/src/stores/useRecommendationDirectivesStore.ts#L42-L51) which sets `error.value` AND re-throws — that's the right pattern; copy it onto `useDirectivesStore`.

---

### Finding 8 — `STATUS_LABELS` lookup against an unknown server status produces "undefined" CSS (LOW)

[OrdersLogView.vue:33-40](frontend-admin/src/views/OrdersLogView.vue#L33-L40) defines `STATUS_LABELS` as a `Record<string, {…}>` keyed by six known statuses. The template binds the style directly to lookup results ([line 215-224](frontend-admin/src/views/OrdersLogView.vue#L215-L224)):

```html
<span
  class="status-chip"
  :style="{
    background: STATUS_LABELS[order.status]?.bg,
    color: STATUS_LABELS[order.status]?.color,
    borderColor: STATUS_LABELS[order.status]?.border,
  }"
>
```

If the API ever returns a status the frontend doesn't know about (e.g. backend adds `pending_payment` before frontend ships), the bindings resolve to `undefined`, Vue renders no CSS for those properties, and the chip falls back to its default `.status-chip` look. Cosmetic only; the label text falls back via `?? order.status`. Worth a `console.warn` in dev to detect drift between segments 07 (domain enum) and the frontend.

Also: `ActiveStatus` is typed in [ActiveOrdersBoardView.vue:6](frontend-admin/src/views/ActiveOrdersBoardView.vue#L6) as a union `'submitted' | 'preparing' | 'ready'` — narrower than the backend enum. If a `cancelled` order ever shows up in `getActiveOrders` it would not be assignable to `byColumn` — `buckets[o.status]` would be `undefined` and `?.push` would silently drop it. The backend should never return `cancelled` here, but defensively the type should be `string` plus a runtime filter.

---

### Finding 9 — `PerformanceMetrics` is typed `any`; shared `DailyMetrics` type is ignored (LOW)

[PerformanceMetrics.vue:5-8](frontend-admin/src/components/PerformanceMetrics.vue#L5-L8) accepts `metrics: any`. [useMetricsStore.ts:7](frontend-admin/src/stores/useMetricsStore.ts#L7) types it as `ref<any>(null)`. The shared type `DailyMetrics` exists and is even imported in `api-client.ts:5` and used as `authedFetcher<DailyMetrics>(...)`, so the value coming back is well typed at the boundary — then thrown away into `any` in the store. Any rename on the backend (`flashDealsShown` → `dealsShown`) will compile and silently render 0 forever via `metrics?.flashDealsShown || 0` ([line 26](frontend-admin/src/components/PerformanceMetrics.vue#L26)).

`claimRate` ([line 10-15](frontend-admin/src/components/PerformanceMetrics.vue#L10-L15)) returns `string` (from `.toFixed(1)`) in the success path and `number` (0) in the early-return path — the template renders both fine but the inconsistency is a type-error trap if anyone tries to compare it numerically.

---

### Finding 10 — `parseDirective` JSON.parse runs three times per rule render (LOW)

[RulesManagementView.vue:221-271](frontend-admin/src/views/RulesManagementView.vue#L221-L271) calls `parseDirective(d)` on every reactive evaluation: the wrapping `v-if`, the inner `v-for` source, and the unresolved check. With a parsed-rules string blob each render runs `JSON.parse` three times per directive. The data is small so this is not user-visible, but it allocates per render and can be avoided by hoisting to a `computed` keyed by directive id, or by parsing once when the directives are fetched and storing the result on the entity.

---

### Finding 11 — Similarity rules section is dead-code-fenced behind `v-if="false"` (LOW)

[RulesManagementView.vue:287-301](frontend-admin/src/views/RulesManagementView.vue#L287-L301):

```html
<!-- Auto-generated similarity rules section — hidden until implemented; flip v-if to true to restore -->
<section v-if="false">
```

The view still imports the component and store, calls `rulesStore.fetchRules()` on mount, computes `ruleCount` against the hidden rules, and shows the rule count in the status pill. So the network request fires and the count includes invisible rules. Either (a) finish wiring and unhide, or (b) remove the import / mount-time fetch and rebase `ruleCount` purely on `recRulesStore.activeDirectives.length`. Carrying dead UI that still does work is a smell.

---

## Summary

**Top three risks to fix first**
1. **F1** — wall-clock polling on hidden tabs, multiplied by every admin who leaves the board open, is a quietly expensive default. Add a `visibilitychange` gate before SignalR rewiring.
2. **F2** — no preview before AI directive save costs an LLM call per attempt and produces silent bad parses in production rules.
3. **F4** — timezone-naive timestamps will mislead remote admins; the `restaurant.timezone` field exists and is unused.

**Top three strengths**
- `ActiveOrdersBoardView` does optimistic drag-drop with explicit rollback and clears both `status` and `readyAt` on failure ([source](frontend-admin/src/views/ActiveOrdersBoardView.vue#L108-L123)) — and trusts the server `readyAt` over local `Date.now()` to avoid countdown drift.
- `useRecommendationDirectivesStore` is the right shape: stores `error.value`, re-throws so views can `toast.error`, and never blocks rendering on a stale list (`directives.unshift(created)`). Use as the template for fixing `useDirectivesStore`.
- The `STATUS_LABELS` map and the kanban column color treatments are the cleanest theming pattern in the admin SPA — easy to add a new status without touching CSS.

**Recommended fix order**
1. F7 (mirror `useRecommendationDirectivesStore` pattern onto `useDirectivesStore`, unblocks F6).
2. F6 (degraded-mode banner when AI fetch fails).
3. F2 (preview-before-save for AI directives).
4. F1 (visibility-gated polling).
5. F4 (timezone-aware date rendering).
6. F11 (decide on similarity section: ship or remove).
7. F3, F5, F8, F9, F10 — backlog.

No code modified during this review.
