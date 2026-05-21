# Code Review — Segment 22: Admin SPA · Menu Management & Import

**Date:** 2026-05-21
**Reviewer:** Claude (Opus 4.7, read-only review per roadmap)
**Scope:** Vue 3 admin SPA — menu CRUD view and the three-step import wizard.

## Files reviewed

| Role | Path |
|---|---|
| Menu CRUD view | [frontend-admin/src/views/MenuManagementView.vue](frontend-admin/src/views/MenuManagementView.vue) |
| Wizard shell | [frontend-admin/src/views/MenuImportView.vue](frontend-admin/src/views/MenuImportView.vue) |
| Step 1 (upload) | [frontend-admin/src/components/menu-import/StepUploadPhotos.vue](frontend-admin/src/components/menu-import/StepUploadPhotos.vue) |
| Step 2 (map) | [frontend-admin/src/components/menu-import/StepMapCategories.vue](frontend-admin/src/components/menu-import/StepMapCategories.vue) |
| Step 3 (review) | [frontend-admin/src/components/menu-import/StepReviewItems.vue](frontend-admin/src/components/menu-import/StepReviewItems.vue) |
| Step indicator | [frontend-admin/src/components/menu-import/ImportStepIndicator.vue](frontend-admin/src/components/menu-import/ImportStepIndicator.vue) |
| Menu CRUD store | [frontend-admin/src/stores/useMenuManagementStore.ts](frontend-admin/src/stores/useMenuManagementStore.ts) |
| Wizard store | [frontend-admin/src/stores/useMenuImportStore.ts](frontend-admin/src/stores/useMenuImportStore.ts) |
| Wizard API client | [frontend-admin/src/services/menuImportApi.ts](frontend-admin/src/services/menuImportApi.ts) |
| Shared validation constants | [shared/types/menu-validation.ts](shared/types/menu-validation.ts) |
| Route table | [frontend-admin/src/router/index.ts](frontend-admin/src/router/index.ts) |

No `CLAUDE.md` files exist anywhere in the repository — there is no project-level Claude guidance to enforce; review proceeded against the roadmap checklist only.

---

## Checklist outcomes

| # | Item | Result |
|---|---|---|
| 1 | Wizard state survives accidental tab close / browser refresh, **or** the user is warned via `beforeunload` | **FAIL** — see Finding 1 |
| 2 | Step-back is non-destructive (don't lose review edits when going back to step 2) | **FAIL** — see Finding 2 |
| 3 | File upload shows progress; failed upload can be retried without restarting the wizard | **PARTIAL** — see Finding 3 |
| 4 | Validation errors shown inline next to the row, not in a global toast | **PASS** — `StepReviewItems` renders `field-error` next to each input ([source](frontend-admin/src/components/menu-import/StepReviewItems.vue#L86-L112)) |
| 5 | Bulk save: API per-row status is rendered | **FAIL** — see Finding 4 |
| 6 | Optimistic UI updates roll back on failure | **PASS** — `reorderProducts` saves `previous`, mutates locally, restores on throw ([source](frontend-admin/src/stores/useMenuManagementStore.ts#L88-L111)). Other mutations are non-optimistic (await server then patch), which is also fine. |
| 7 | Image previews are revoked (`URL.revokeObjectURL`) on unmount | **FAIL** — see Finding 5 |
| 8 | Long lists virtualize or paginate | **FAIL** — see Finding 6 |
| 9 | Sort order edits commit without racing concurrent edits | **PARTIAL** — see Finding 7 |
| 10 | Follow-up: concurrent admin editing | **OPEN** — see Finding 8 |

Additional cross-cutting issues found while reading the files are listed below as Findings 9–11.

---

## Findings

### Finding 1 — Wizard state is wiped on refresh / navigation without warning (HIGH)

The wizard store ([useMenuImportStore.ts](frontend-admin/src/stores/useMenuImportStore.ts)) holds files, scan results, category mappings and review edits in plain Pinia refs with no persistence plugin. `MenuImportView.vue` calls `importStore.$reset()` from `onBeforeUnmount` ([line 31-33](frontend-admin/src/views/MenuImportView.vue#L31-L33)), so any in-app navigation away from `/menu/import` (sidebar click, browser back button, accidental router push) discards every selected file, every parsed line item and every manual edit.

There is no `beforeunload` listener (confirmed: `grep -r "beforeunload" frontend-admin/src` returns nothing) and no `onBeforeRouteLeave` guard. A user three minutes into a 50-item review who clicks the wrong sidebar link must re-upload the photos, re-scan (an AI call that costs money), re-map categories and re-edit every name/price.

**Recommendation:** either (a) persist `files`/`extractedItems`/`reviewItems`/`categoryMapping` to `sessionStorage` keyed by an import session id, or (b) attach a `beforeunload` listener plus an `onBeforeRouteLeave` guard while `step > 1 || files.length > 0`, prompting the user before clearing.

---

### Finding 2 — Going back to step 2 and forward again wipes Step 3 edits (HIGH)

`StepMapCategories.vue` exposes a "Înapoi" button that flips the step ([line 112-114](frontend-admin/src/components/menu-import/StepMapCategories.vue#L112-L114)). Re-advancing calls `importStore.advanceToReview()`, which **rebuilds `reviewItems` from `extractedItems` from scratch**:

```ts
function advanceToReview() {
    reviewItems.value = extractedItems.value
      .slice()
      .sort(...)
      .map((it) => ({ clientId: makeId(), name: it.name, price: it.price, ... skipped: false }))
    step.value = 3
}
```
([source](frontend-admin/src/stores/useMenuImportStore.ts#L142-L156))

So the flow "step 3 → fix 10 names → step 2 → tweak mapping → step 3" silently throws away the 10 fixes. The user will not notice until they import, by which time the original (worse) text is in the database.

Even just "step 3 → step 2 → step 3" (without any mapping change) discards the edits, which is the more dangerous case because nothing about the user's action signalled "I want to redo my work".

**Recommendation:** only rebuild `reviewItems` if `reviewItems.length === 0` or if the mapping changed since the last `advanceToReview()` call. Alternatively, keep `reviewItems` immutable once created and merge `resolvedCategory` updates onto it on re-entry.

---

### Finding 3 — Upload has no progress; per-file retry impossible (MEDIUM)

`menuImportApi.scan()` POSTs the multipart form via `fetch()` without an upload-progress event ([source](frontend-admin/src/services/menuImportApi.ts#L39-L49)); the UI shows only the text "Scanez X fotografii…" ([StepUploadPhotos.vue line 117](frontend-admin/src/components/menu-import/StepUploadPhotos.vue#L117)). For a 5 MB × 10 file batch on a slow connection that is a multi-minute black box.

`fetch()` cannot emit upload progress — `XMLHttpRequest.upload.onprogress` is still the only reliable way in the browser. Acceptable to defer, but flag as a UX gap.

Retry semantics: failed scans set `scanError`; the user clicks the button again and the same files are re-uploaded — there is no way to drop a single bad file from a partially-failed batch without manually removing it via the thumbnail × button. Acceptable for now.

---

### Finding 4 — Bulk-create error response (`missingCategoryIds`) parsed but never rendered (MEDIUM)

`menuImportApi.bulkCreate` decodes `missingCategoryIds` from a 409 body into `err.missingCategoryIds` ([source](frontend-admin/src/services/menuImportApi.ts#L15-L21)), and there is a test covering this path ([menuImportApi.spec.ts:81-92](frontend-admin/src/__tests__/menuImportApi.spec.ts#L81-L92)). But the only consumer of the error is `StepReviewItems.onImport` which catches and ignores it ([source](frontend-admin/src/components/menu-import/StepReviewItems.vue#L36-L42)), letting the generic `importStore.importError` string render. The structured `missingCategoryIds` is dropped on the floor.

Practical impact: when an admin deletes a category in a different tab while the wizard is open, the bulk import fails with "bulk-create failed: HTTP 409" and the user has no actionable guidance — they cannot tell which row(s) referenced the now-missing category. The roadmap explicitly calls this out: "Bulk save: the API result's per-row status is rendered (segment 08)."

**Recommendation:** in step 3, when `importError` is set and the error is a `MenuImportApiError` with `missingCategoryIds`, highlight the offending rows (group them by category) and offer a one-click remap to an existing category.

---

### Finding 5 — Object URLs for image previews leak; never revoked (MEDIUM)

`StepUploadPhotos.vue` defines:

```ts
function thumbUrl(file: File): string {
  return URL.createObjectURL(file)
}
```
([source](frontend-admin/src/components/menu-import/StepUploadPhotos.vue#L56-L58))

…and the template binds `:src="thumbUrl(file)"` ([line 94](frontend-admin/src/components/menu-import/StepUploadPhotos.vue#L94)). Because `thumbUrl` is not memoised, Vue invokes it on every reactive re-render of the thumb list. Each call creates a fresh blob URL that pins the underlying `File` in memory; none of them are ever passed to `URL.revokeObjectURL`. After a few add/remove cycles on five 5 MB files the tab is holding dozens of orphan blobs.

Search confirms `URL.revokeObjectURL` does not appear anywhere under `frontend-admin/src`.

**Recommendation:** compute the URLs once (e.g. a `Map<string, string>` keyed by file name+size, or a computed array kept in sync with `store.files`) and revoke them in `removeFile` and `onBeforeUnmount`. The roadmap calls this out verbatim.

---

### Finding 6 — No virtualisation / pagination on category & product lists (MEDIUM)

`MenuManagementView.vue` renders categories and their products with two nested `v-for` blocks ([line 348-495](frontend-admin/src/views/MenuManagementView.vue#L348-L495)) and no `IntersectionObserver`, no `<RecycleScroller>`, no pagination. The store fetches the entire menu in one shot via `apiClient.getAdminMenu()` ([source](frontend-admin/src/stores/useMenuManagementStore.ts#L28-L40)).

For the explicit "500-item menu" target in the roadmap, that's 500 article elements with drag handlers, an `Intl.NumberFormat` price call per row, and a thumbnail per row — well into "scrolling janks" territory on a low-end Chromebook. Not a correctness bug, but a known scalability ceiling that should be filed as follow-up before a customer with a large menu onboards.

---

### Finding 7 — `reorderProducts` is last-write-wins with no concurrency check (LOW/MEDIUM)

`reorderProducts` ([source](frontend-admin/src/stores/useMenuManagementStore.ts#L88-L111)) computes the new order, sends `productIds` to the backend, and rolls back the local copy on error. There is no `If-Match`/ETag/version field on the request, so if two admins drag at the same time the second `PUT` overwrites the first silently. The roadmap's follow-up question ("When two admins edit the same product simultaneously, what's the conflict resolution?") is unanswered in this layer — the answer is *last-write-wins*, which is acceptable but should be documented.

---

### Finding 8 — `softDeleteProduct` skips the inner loop instead of breaking out cleanly (LOW)

`useMenuManagementStore.softDeleteProduct` walks every category looking for the product and uses `break` to exit ([source](frontend-admin/src/stores/useMenuManagementStore.ts#L113-L123)). The logic is correct, but a comment claims "Backend just flips IsAvailable=false; reflect locally." — verify this is still true against the controller (segment 08) since the comment can lie after refactors. Low priority; flagged only because the rest of the store uses helpers (`moveOrUpdateProduct`) for the same job.

---

### Finding 9 — Inline `:style="background-image: url('${imageUrl}')"` allows CSS injection (MEDIUM)

`MenuManagementView.vue` builds the category and product thumbnails by string-concatenating user-controlled `imageUrl` into a CSS declaration:

```html
:style="cat.imageUrl ? `background-image: url('${cat.imageUrl}');` : ''"
```
([line 358](frontend-admin/src/views/MenuManagementView.vue#L358) and the same pattern on [line 442](frontend-admin/src/views/MenuManagementView.vue#L442))

Vue binds `:style` strings to the element's `style` attribute *literally* — there is no CSS escaping. An `imageUrl` like `https://a/'); color: red; foo:('` injects arbitrary CSS into the page. The realistic attack here is limited (the field is admin-controlled and the URL goes through `type="url"` client-side, but the API may not strictly validate format), but defense in depth matters since the `imageUrl` could also be inserted via the bulk-import API path.

**Recommendation:** swap the inline `<div style="background-image: url(...)">` for an `<img :src="cat.imageUrl">` inside the thumb wrapper — Vue escapes attribute bindings, and the browser only parses the value as a URL. Same for the product row.

---

### Finding 10 — `menuImportApi` bypasses the `authedFetcher` 401-refresh path (MEDIUM)

`api-client.ts` defines `authedFetcher` which retries once on 401 after `auth.tryRefresh()` ([source](frontend-admin/src/services/api-client.ts#L121-L140)). The menu-import service does **not** use it — it calls raw `fetch()` and reads `auth.accessToken` once for the `Authorization` header ([source](frontend-admin/src/services/menuImportApi.ts#L29-L37), used at [line 43](frontend-admin/src/services/menuImportApi.ts#L43) and [line 53-57](frontend-admin/src/services/menuImportApi.ts#L53-L57)).

Consequences:
1. If the access token has just expired when the user clicks "Scanează meniul" or "Importă", the request fails with 401 with no retry; the user sees a generic error and has to re-trigger the action, which is impossible for a multi-minute scan they just paid for.
2. Two code paths for auth header handling means future fixes (e.g., adding a `X-CSRF-Token`, a correlation id, or a global 429-retry) need to be replicated.

**Recommendation:** introduce an `authedFetch` (returning the `Response`, not parsed JSON) in `api-client.ts` that `menuImportApi.ts` can layer over. Or move the two endpoints into `apiClient` and consume them via `authedFetcher` with a custom body branch for `FormData`.

---

### Finding 11 — Category sort uses `Intl.NumberFormat` recreated per render; minor perf (LOW)

`MenuManagementView.vue` computes `priceFormatter` via `computed(() => new Intl.NumberFormat(...))` ([line 259-261](frontend-admin/src/views/MenuManagementView.vue#L259-L261)). Because the computation has no reactive dependencies it is constructed exactly once — acceptable. Not a bug; noting it because at first glance it looked like a per-render allocation.

---

## Summary

**🔥 High-stability items from the roadmap:** none for this segment (no items marked 🔥 in segment 22), but the closest analog is the wizard-state-loss combo of Findings 1 and 2 — together they make the multi-step import experience fragile in ways that will silently corrupt menus in production.

**Top three risks to fix first**
1. **F2** — silent loss of step-3 edits on backward navigation. Bad data lands in the DB; user never sees a warning.
2. **F1** — full wizard state wiped on refresh / accidental navigation. Wastes AI spend and user time.
3. **F4** — `missingCategoryIds` parsed but never surfaced. Inactionable error after a partial-failure import.

**Top three strengths**
- `useMenuImportStore` is unusually well-factored: a clear three-step state machine, validation centralised in `validateRow`, the bulk payload assembled in one place via `buildBulkPayload`, and the `MenuImportApiError` type carries enough structure to make per-row UI feasible (the missing piece is the consumer, see F4).
- `reorderProducts` correctly snapshots the previous order and rolls back on failure — the only optimistic-update path in the store, and it is implemented right.
- Validation constants live in [shared/types/menu-validation.ts](shared/types/menu-validation.ts) with a comment tying them to the C# controller, which is the right place to keep client/server in sync for this segment.

**Recommended fix order**
1. F2 (one-line guard in `advanceToReview`).
2. F4 (render `err.missingCategoryIds` in step 3 + offer remap).
3. F5 (revoke object URLs).
4. F1 (sessionStorage persistence or beforeunload guard).
5. F9 (swap inline-style URL for `<img>`).
6. F10 (route menu-import through `authedFetcher`).
7. F3, F6, F7, F8, F11 — backlog.

No code was modified during this review; this report is the only artifact.
