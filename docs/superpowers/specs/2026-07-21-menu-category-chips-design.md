# Menu screen: category-shortcut chips (replace diet-filter pills)

**Date:** 2026-07-21
**Area:** `frontend-customer` only (no backend / contract / shared-types changes)
**Status:** Design — awaiting spec review

## Problem

`MenuView.vue` renders a fixed row of 5 pills — `All · Meat · Fish · Vegetarian · Chef` — on
**every** category. The pills are not real data: the backend does not tag dishes by diet, so each
non-`all` pill is a regex heuristic ([MenuView.vue:61-66](../../../frontend-customer/src/components/screens/MenuView.vue#L61-L66))
matched against a dish's `name + description`. Because the same 5 pills show regardless of category,
they are nonsensical in categories where nothing can match (e.g. `Meat`/`Fish`/`Vegetarian` on a
Dessert category).

## Decision

Remove the diet-filter pills entirely and repurpose the pill row as a **category switcher**: a
horizontal-scroll row of one chip per menu category, letting the diner hop between categories inline
instead of returning to the pebble grid to pick another.

This was chosen over (a) deriving diet pills from the items actually present, and (b) adding real
diet tags to products. Both keep a filtering concept the diner didn't ask for; the switcher turns the
row into useful navigation and deletes the heuristic guesswork.

## Behavior

- The row shows one chip per category, sourced from `categories` (`useMenu`), in the **same order**
  as the pebble grid — so a category is always in a stable position.
- The chip for the currently-viewed category (`props.categoryId`) uses the existing active pill
  styling (dark `T.ink` fill, `T.paperHi` text); the rest use the inactive card styling. This reuses
  the current chip template/CSS unchanged — only the chip contents and click behavior change.
- On mount, the active chip is **auto-scrolled into view** within the horizontal row, so the diner
  can always see their current position without hunting.
- Tapping a non-active chip switches the menu to that category **in place**. Per the keyed remount
  (below), the dish list **resets to the top** and search closes/clears — a clean "you picked a new
  category" transition.
- **Search is unchanged** — the ⌕ button and its box remain an independent control. After the diet
  filter is removed, the `items` computed becomes "this category's dishes, optionally narrowed by the
  search query."
- The header **back arrow is unchanged** — it still returns to the pebble grid, which remains the
  richer visual entry point.

## Wiring

- **MenuView** emits a new event `select-category(id: string)` when a non-active chip is tapped
  (tapping the already-active chip is a no-op).
- **OrderingView** handles it by setting `activeCategoryId.value = id` and staying on the `menu`
  screen. No other screen state changes.
- The existing `<MenuView :key="menu-${activeCategoryId}">`
  ([OrderingView.vue:414-417](../../../frontend-customer/src/view/OrderingView.vue#L414-L417))
  remounts MenuView on the category change, which is what delivers the top-reset / search-clear for
  free. No changes to the keying are needed.
- `activeCategoryId` already persists to `sessionStorage` on change
  ([OrderingView.vue:99](../../../frontend-customer/src/view/OrderingView.vue#L99)), so an inline
  switch survives a same-tab refresh with no extra work.

## Deletions

- `type FilterKey`, the `filter` ref, the `FILTERS` computed, and the `FILTER_PATTERNS` record in
  `MenuView.vue` — all removed.
- The filter branch inside the `items` computed
  ([MenuView.vue:81-84](../../../frontend-customer/src/components/screens/MenuView.vue#L81-L84)) —
  removed; only the search branch remains.
- i18n keys `menu.filter.all|meat|fish|veggie|chef` in `useLocale.ts` become unused and are removed
  in **both** RO and EN dictionaries.

## Out of scope

- No new category-navigation UI beyond the chip row (the pebble grid stays as the entry point).
- No backend, DTO, `shared/types`, or admin changes.
- No real diet/dietary tagging — explicitly not pursued.

## Testing

`frontend-customer` has no test suite (`npm test` is a stub). Verification is:

- `npm run type-check` (must pass — the removed `FilterKey`/i18n keys must leave no dangling refs).
- `npm run lint`.
- Manual smoke: open a table QR, enter a category, confirm the chip row lists all categories with the
  current one highlighted and scrolled into view; tap another chip and confirm the menu switches in
  place, resets to the top, and closes search; confirm the back arrow still returns to the pebbles;
  confirm search still filters within a category.

## Risks / notes

- The chip row currently scrolls horizontally already; verify the auto-scroll-into-view logic works
  under the phone-frame layout (real phone full-screen vs. desktop 390×844 preview frame).
- Category count is small in practice, but the row must remain horizontally scrollable for tenants
  with many categories (already the case).
