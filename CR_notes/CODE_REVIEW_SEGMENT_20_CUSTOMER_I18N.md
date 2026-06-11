# Segment 20 — Customer SPA: i18n (EN/RO) — review findings

Scope review against the [Segment 20 checklist](CODE_REVIEW_ROADMAP.md#segment-20--customer-spa--i18n-enro) of [CODE_REVIEW_ROADMAP.md](CODE_REVIEW_ROADMAP.md). Core file: [composables/useLocale.ts](frontend-customer/src/composables/useLocale.ts). Consumers — every `.vue` file under [components/screens/](frontend-customer/src/components/screens/) plus [view/OrderingView.vue](frontend-customer/src/view/OrderingView.vue). Stale duplicates: [components/Cart.vue](frontend-customer/src/components/Cart.vue), [components/MenuList.vue](frontend-customer/src/components/MenuList.vue), [components/FlashDealBanner.vue](frontend-customer/src/components/FlashDealBanner.vue), [components/RecommendationWidget.vue](frontend-customer/src/components/RecommendationWidget.vue).

Reviewer: Claude (Opus 4.7). Date: 2026-05-21. Branch reviewed: `main` at `fb59c7e`.

## Verdict

**6 findings, two HIGH.** The i18n foundation is well-shaped and the most-cited risks all pass at first read: both dictionaries declare exactly 94 keys with zero set-difference (programmatic diff: `RO: 94, EN: 94, RO-only: [], EN-only: []`), Romanian diacritics use the modern comma-below form throughout (`s/t cedilla: 0, s/t comma: 23`) with proper `ă`/`â`/`î` distribution, and `useLocale.ts` reads `localStorage` at module-load time ([line 230](frontend-customer/src/composables/useLocale.ts#L230)) so the locale is resolved synchronously before the first paint. The `t()` helper at [line 253-257](frontend-customer/src/composables/useLocale.ts#L253-L257) has the right shape — `{count}` / `{minutes}` / `{query}` placeholders are substituted by a tiny `format()` helper, and unresolved placeholders are left as `{name}` rather than coerced to `"undefined"`.

The live screens — `LanguageSelectView`, `PebblesView`, `MenuView`, `CartView`, `FlashDealView`, `OrderSentView` — route every user-visible string through `t()`. A scripted scan of the live templates' element text for un-bracketed letters returns no hits (the grep for `>[Letter]...<` outside `{{...}}` interpolations is empty). The `session.invalidTitle` / `session.missingMessage` / `session.invalidMessage` overlay path in `OrderingView` is keyed and surfaces in both languages, satisfying the segment-18 cross-reference. Locale state is a single shared `ref` ([line 230](frontend-customer/src/composables/useLocale.ts#L230)) — every consumer of `useLocale()` reads the same ref instance, so `setLocale('en')` propagates reactively across the SPA in one tick.

The findings cluster around two themes:

1. **The "no hardcoded text" win is undermined by four orphaned duplicate components.** [F1](#f1--high--four-orphan-components-still-contain-hardcoded-romanian-text-cartvue--menulistvue--flashdealbannervue--recommendationwidgetvue) — `Cart.vue`, `MenuList.vue`, `FlashDealBanner.vue`, and `RecommendationWidget.vue` were not deleted when their `screens/`-prefixed replacements landed. The dead files contain hardcoded strings like `"Comanda a fost trimisă!"`, `"Bucătarii noștri o pregătesc chiar acum."`, `"Adăugat!"`, `"Trimite Comanda"`. `grep "from '@/components/(Cart|MenuList|FlashDealBanner|RecommendationWidget)'"` returns zero imports — they're unreachable from `OrderingView`, but they are reachable for a contributor browsing for examples and they ship in the source bundle until a tree-shake removes them.
2. **No locale-aware number / currency / date formatting.** [F2](#f2--high--no-intlnumberformat-prices-render-as-raw-js-numbers-249-ron-not-2490-ron) — every price in `CartView`, the cart-summary total, every recommendation price, and the `OrderSentView` total are rendered as `{{ raw_js_number }}` followed by `{{ t('common.ron') }}`. A price of `24.9` ships as `"24.9 RON"`. Romanian customers expect `"24,90 RON"`; English customers expect `"24.90 RON"`. Neither happens. The `common.ron` translation key is the *only* currency-localisation knob — and it's the same literal `'RON'` in both languages. [F5](#f5--medium--ordersentview-builds-timestamps-with-raw-gethoursgetminutes-no-intldatetimeformat) — the order-status timeline timestamps via raw `${d.getHours()}:${d.getMinutes()}` ([OrderSentView.vue:55](frontend-customer/src/components/screens/OrderSentView.vue#L55)) regardless of locale.

The remaining items are smaller: [F3](#f3--medium--missingkey-fallback-renders-the-literal-keypath-instead-of-the-roadmaps-english-fallback) — the `t()` helper falls through to the literal `key` ("flash.foo.bar") as last resort ([line 255](frontend-customer/src/composables/useLocale.ts#L255)). The roadmap says fallback should be English; here it's "Romanian then key.path". Today the EN/RO sets are identical so the literal-key path never fires, but the design is wrong for the next contributor who forgets a key. [F4](#f4--medium--pluralisation-misses-romanians-fewmany-distinction-and-the-singular-case-of-dishescount) — Romanian has three plural forms (1, 2-19, 20+ with "de" prefix); the existing `cart.selectedOne`/`cart.selectedMany` binary split covers EN cleanly but produces *"20 preparate"* in RO where idiomatic Romanian is *"20 de preparate"*. Separately, `pebbles.dishesCount` and `menu.dishesCount` always render the plural form (*"{count} preparate"*), so a category with one dish reads *"1 preparate"* — wrong in Romanian, fine in English. [F6](#f6--low--menunoresultsfor-en-inherits-the-romanianstyle-low9-quote--for-the-en-translation) — `menu.noResultsFor` uses `„{query}".` in *both* RO and EN ([line 53, 159](frontend-customer/src/composables/useLocale.ts#L53-L159)) — the low-9 quote `„` is correct for Romanian but unusual for English which conventionally uses `"…"` paired.

What does check out, beyond the verdict opener: every live screen calls `t()` on every user-visible string — the scripted hardcoded-text scan against `frontend-customer/src/components/screens/**.vue` is empty. The `dict` is one module with two object literals, so adding a key requires both entries in the same diff — the file structure makes parity-drift visible at PR time. `format()` ([line 244-250](frontend-customer/src/composables/useLocale.ts#L244-L250)) leaves un-bound placeholders as `{name}` rather than serialising `undefined` — a small but defensive choice. The `lang.ro` / `lang.en` keys hold *"Română"* / *"English"* identically across both dicts ([line 13-14, 119-120](frontend-customer/src/composables/useLocale.ts#L13-L120)) — language names render in their own script regardless of the active locale, which is the convention. `localStorage` writes are wrapped in `try/catch` ([line 232-238](frontend-customer/src/composables/useLocale.ts#L232-L238)) for private-mode browsers. No `v-html` is used to render any translation string (grep `v-html` across `frontend-customer/src` returns zero hits) — XSS surface is structurally absent. SSR fallback at [line 224](frontend-customer/src/composables/useLocale.ts#L224) (`if (typeof localStorage === 'undefined')`) is the right shape even though this app isn't SSR'd today.

---

## Checklist coverage

| Item | Status | Evidence |
|---|---|---|
| Every user-visible string comes from the locale dictionary — no hardcoded English in templates | ⚠ Partial — see [F1](#f1--high--four-orphan-components-still-contain-hardcoded-romanian-text-cartvue--menulistvue--flashdealbannervue--recommendationwidgetvue) | Live screens ✓ (scripted scan empty). Dead duplicates `Cart.vue`, `MenuList.vue`, `FlashDealBanner.vue`, `RecommendationWidget.vue` contain hardcoded RO text but are unreachable. |
| Both EN and RO dictionaries have **identical key sets**. Diff them | ✓ Pass | Scripted diff: `RO: 94, EN: 94, RO-only: [], EN-only: []`. Zero drift. |
| Romanian diacritics (`ă`, `â`, `î`, `ș`, `ț`) render correctly (UTF-8 source, no double-encoding) | ✓ Pass | Scripted character-class count: `s/t cedilla (deprecated): 0, s/t comma (modern): 23, i/a circumflex: 17, a breve: 47`. Uniform modern diacritics. |
| Locale persists (`localStorage` per explore report) and is read on boot before the first paint | ✓ Pass | [line 230](frontend-customer/src/composables/useLocale.ts#L230) — `const locale = ref<Locale>(readInitial())` at module top. Synchronous read happens at module-import time, before any consumer renders. |
| Number / currency / date formatting uses `Intl.*` with the active locale, not hardcoded strings | ❌ Fail — see [F2](#f2--high--no-intlnumberformat-prices-render-as-raw-js-numbers-249-ron-not-2490-ron), [F5](#f5--medium--ordersentview-builds-timestamps-with-raw-gethoursgetminutes-no-intldatetimeformat) | Grep `Intl\.\|toLocaleString\|toFixed` across `frontend-customer/src` returns zero matches. Prices render as raw `{{ value }} {{ t('common.ron') }}`; timestamps via raw `getHours()/getMinutes()`. |
| When a key is missing, fallback is English, not a literal `key.path` shown to the user | ❌ Fail — see [F3](#f3--medium--missingkey-fallback-renders-the-literal-keypath-instead-of-the-roadmaps-english-fallback) | [line 255](frontend-customer/src/composables/useLocale.ts#L255) — `table[key] ?? dict.ro[key] ?? key`. RO is the fallback (not EN), and the *last* resort is the literal `key`. |
| Pluralization rules handled where needed (Romanian has different rules from English) | ❌ Fail — see [F4](#f4--medium--pluralisation-misses-romanians-fewmany-distinction-and-the-singular-case-of-dishescount) | Binary one/many split (`cart.selectedOne` / `cart.selectedMany`, `menu.cartCountOne` / `menu.cartCountMany`); always-plural for `dishesCount`. No `Intl.PluralRules`. |
| No HTML tags inside translation strings unless rendered with `v-html` and known safe | ✓ Pass | Grep `v-html` across [src/](frontend-customer/src/) returns no matches. No translation string contains `<`. |

---

## F1 — HIGH — Four orphan components still contain hardcoded Romanian text (`Cart.vue` / `MenuList.vue` / `FlashDealBanner.vue` / `RecommendationWidget.vue`)

The customer SPA has two parallel UI hierarchies:

- **Live screens** under [components/screens/](frontend-customer/src/components/screens/) — `CartView.vue`, `MenuView.vue`, `FlashDealView.vue`, `OrderSentView.vue`, `PebblesView.vue`, `LanguageSelectView.vue`. These are i18n-aware; every user-visible string runs through `t()`.
- **Dead duplicates** under [components/](frontend-customer/src/components/) — `Cart.vue`, `MenuList.vue`, `FlashDealBanner.vue`, `RecommendationWidget.vue`. These look like the older versions, predating the move to `components/screens/`.

The live shell ([OrderingView.vue:11-16](frontend-customer/src/view/OrderingView.vue#L11-L16)) only imports from `components/screens/`. A grep for `from '@/components/(Cart|MenuList|FlashDealBanner|RecommendationWidget)'` across [src/](frontend-customer/src/) returns zero matches. The four files are unreachable in production today.

But they contain Romanian text outside `t()`:

| File | Hardcoded strings (sample) |
|---|---|
| [Cart.vue](frontend-customer/src/components/Cart.vue) | `"Comanda a fost trimisă!"`, `"Bucătarii noștri o pregătesc chiar acum."`, `"Coșul tău este gol. Adaugă câteva preparate delicioase din meniu"`, `"Total estimat:"`, `"Se trimite..."`, `"Trimite Comanda"` |
| [MenuList.vue](frontend-customer/src/components/MenuList.vue) | `"Se încarcă meniul..."`, `"Ce ai poftă să mănânci?"`, `"Vezi meniul →"`, `"Înapoi"` |
| [FlashDealBanner.vue](frontend-customer/src/components/FlashDealBanner.vue) | `"Ofertă Limitată!"`, `"Adăugat!"` |
| [RecommendationWidget.vue](frontend-customer/src/components/RecommendationWidget.vue) | `"Se actualizează..."`, `"Bazat pe ce ai adăugat deja în coș"` |

Three concrete consequences:

- **Resurrected-from-history risk.** A future contributor browsing `components/` for "how do I render a cart?" finds `Cart.vue` first (it's in the canonical path) and copy-paste-evolves it forward. The hardcoded strings come with the copy.
- **Bundle bloat.** Vite tree-shakes by static analysis. With zero imports the files *should* be eliminated, but the analysis depends on the bundle entry graph; tools like vue-router's `defineAsyncComponent` (not used today, but plausible additions) defeat the elimination. Worst case: ~10 KB of dead RO strings ship to every diner.
- **Translation drift.** `Cart.vue:98` renders `{{ item.price }} RON`; the live `CartView` (`screens/CartView.vue:127`) renders `{{ it.price * it.quantity }} {{ t('common.ron') }}`. The numbers themselves *also* differ (the dead one shows unit price, the live one shows extended price). Anyone re-using the dead one will quietly ship a different formula, in addition to skipping i18n.

The fix is one-line per file: delete. The screens/ hierarchy is the live one; the duplicates predate it. Out of scope for this review.

---

## F2 — HIGH — No `Intl.NumberFormat`; prices render as raw JS numbers (`"24.9 RON"`, not `"24,90 RON"`)

[CartView.vue:125-127](frontend-customer/src/components/screens/CartView.vue#L125-L127):

```vue
<div class="item-price">
  {{ it.price * it.quantity }}
  <span class="item-price-unit" :style="{ color: T.ink70 }">{{ t('common.ron') }}</span>
</div>
```

[CartView.vue:155, 162-164](frontend-customer/src/components/screens/CartView.vue#L155-L164):

```vue
<span>{{ t('cart.subtotal') }}</span><span>{{ cartStore.total }} {{ t('common.ron') }}</span>
...
<span class="total-value">
  {{ cartStore.total }}
  <span class="total-unit" :style="{ color: T.ink70 }">{{ t('common.ron') }}</span>
</span>
```

`grep "Intl\.\|toLocaleString\|toFixed" frontend-customer/src` returns zero matches. The price formatting story is *everywhere* the same: `{{ raw_js_number }} {{ t('common.ron') }}`.

What ships per locale (for a price of `24.90`):

| Locale | What renders | What's idiomatic |
|---|---|---|
| RO | `"24.9 RON"` | `"24,90 RON"` (comma decimal, two digits) |
| EN | `"24.9 RON"` | `"24.90 RON"` (dot decimal, two digits) |
| Either | `"24.9"` if `it.price` happens to be `24.9` — `{{ it.price * it.quantity }}` for qty `3` produces `74.69999999999999` (floating-point) | Always two-digit fraction |

The `common.ron` translation key is the only currency-localisation knob and it's the same literal `'RON'` in both dictionaries ([line 20, 126](frontend-customer/src/composables/useLocale.ts#L20-L126)). It's not even used to localise — RO-speakers in Romania say "RON" or "lei"; "RON" is the ISO code and is identical in both languages. So today, the `t()` call adds no value over a static `'RON'` literal.

The floating-point concern is the more acute one: `24.9 * 3 = 74.69999999999999`. The cart's `total` computed at [useCartStore.ts:18-20](frontend-customer/src/stores/useCartStore.ts#L18-L20) is:

```ts
const total = computed(() => {
  return items.value.reduce((sum, item) => sum + item.price * item.quantity, 0)
})
```

…which ships JavaScript's floating-point rounding errors directly to the cart summary. A customer with three of a `24.90 RON` item sees `74.69999999999999 RON`.

Fix shape (out of scope, but the rough shape is one helper):

```ts
// in useLocale.ts
const formatters: Record<Locale, Intl.NumberFormat> = {
  ro: new Intl.NumberFormat('ro-RO', { style: 'currency', currency: 'RON' }),
  en: new Intl.NumberFormat('en-RO', { style: 'currency', currency: 'RON' }),
}
function formatPrice(value: number): string {
  return formatters[locale.value].format(value)
}
```

Then every `{{ x }} {{ t('common.ron') }}` becomes `{{ formatPrice(x) }}`. The `t('common.ron')` key becomes redundant. `Intl.NumberFormat` handles the floating-point round-to-two-digits and the decimal-separator choice in one call.

---

## F3 — MEDIUM — Missing-key fallback renders the literal `key.path` instead of the roadmap's English fallback

[useLocale.ts:252-258](frontend-customer/src/composables/useLocale.ts#L252-L258):

```ts
export function useLocale() {
  const t = (key: string, params?: Record<string, string | number>): string => {
    const table = dict[locale.value] ?? dict.ro
    const str = table[key] ?? dict.ro[key] ?? key
    return format(str, params)
  }
  return { locale, setLocale, t }
}
```

The fallback chain reads as a sentence: "Look in the active locale's dict; if not found, look in the Romanian dict; if not found, return the key itself."

Two roadmap-flagged concerns:

- **Wrong fallback locale.** Roadmap: *"When a key is missing, fallback is English, not a literal `key.path` shown to the user."* This code falls back to *Romanian* (`dict.ro[key]`), not English. Today that's harmless because both dictionaries are identical, but the intent matters: this is an RO-first product, so RO-as-fallback is defensible — but the roadmap calls for EN. Either align the code with the roadmap (`dict.en[key]`) or document the deliberate inversion.
- **The literal-`key` final fallback.** When a key is missing from *both* dictionaries, `t('cart.foo.bar')` returns the string `'cart.foo.bar'` and renders it to the user. That's exactly the "key.path shown to the user" outcome the roadmap names as wrong.

Today the EN/RO sets are identical (94 keys, zero drift) so the literal-key path never fires *in production*. But:

- A future contributor adding `cart.errorTimeout` to RO and forgetting EN: the EN user sees `'Eroare de timeout'` (RO leaking into EN). The RO-first fallback masks the omission.
- A future contributor adding the consumer (`t('cart.errorTimeout')`) without adding either dict entry: every user sees `'cart.errorTimeout'` rendered as the toast text.

Both are silent. A `console.warn(`Missing translation key: ${key}`)` in the fallback path makes the omission noisy in development; in production, surfacing a less-jarring fallback (the part of the key after the last dot, capitalised — `"Error timeout"`) keeps the UI from showing dotted identifiers.

The fix is two lines inside `t()`. Out of scope here.

---

## F4 — MEDIUM — Pluralisation misses Romanian's few/many distinction, and the singular case of `dishesCount`

Two related sub-issues:

**4a — Always-plural `dishesCount`.** [PebblesView.vue:204](frontend-customer/src/components/screens/PebblesView.vue#L204), [MenuView.vue:124](frontend-customer/src/components/screens/MenuView.vue#L124):

```vue
:sublabel="t('pebbles.dishesCount', { count: pb.count })"
...
<div class="title-meta">{{ t('menu.dishesCount', { count: items.length }) }}</div>
```

Both keys are plural-only:

```ts
'pebbles.dishesCount': '{count} preparate',  // RO
'menu.dishesCount': '{count} preparate',      // RO
'pebbles.dishesCount': '{count} dishes',      // EN
'menu.dishesCount': '{count} dishes',         // EN
```

A category with one dish renders *"1 preparate"* (RO grammar requires *"1 preparat"*) and *"1 dishes"* (EN grammar requires *"1 dish"*). Both wrong. The fix is the `one/many` split that `cart.selectedOne/Many` already uses; the `dishesCount` keys missed the pattern.

**4b — Binary one/many misses Romanian's "few" form.** [CartView.vue:97](frontend-customer/src/components/screens/CartView.vue#L97):

```vue
{{ itemCount === 1 ? t('cart.selectedOne', { count: itemCount }) : t('cart.selectedMany', { count: itemCount }) }}
```

Romanian's pluralisation rules ([Unicode CLDR plural categories](https://cldr.unicode.org/index/cldr-spec/plural-rules)):

- **one** — n = 1 → *"1 preparat selectat"*
- **few** — n = 0 or 2-19 or any number whose last two digits are 1-19 → *"2 preparate selectate"*
- **other** — n = 20+ → *"20 **de** preparate selectate"* (the "de" is required for counts ≥ 20)

The current code uses `selectedOne` for `n=1` and `selectedMany` for everything else, producing *"20 preparate selectate"* which is mid-formal at best and ungrammatical at worst.

`Intl.PluralRules` solves both cases at once:

```ts
const rules: Record<Locale, Intl.PluralRules> = {
  ro: new Intl.PluralRules('ro-RO'),
  en: new Intl.PluralRules('en'),
}
function plural(key: string, n: number, params?: Record<string, any>) {
  const category = rules[locale.value].select(n)  // "one" | "few" | "other"
  return t(`${key}.${category}`, { ...params, count: n })
}
```

Then dict shape becomes (RO):

```ts
'cart.selected.one': '{count} preparat selectat',
'cart.selected.few': '{count} preparate selectate',
'cart.selected.other': '{count} de preparate selectate',
```

EN only needs `one` and `other`. The shape is universal. Out of scope, documented for future work.

---

## F5 — MEDIUM — `OrderSentView` builds timestamps with raw `getHours()/getMinutes()`; no `Intl.DateTimeFormat`

[OrderSentView.vue:54-56](frontend-customer/src/components/screens/OrderSentView.vue#L54-L56):

```ts
function fmt(d: Date) {
  return `${d.getHours().toString().padStart(2, '0')}:${d.getMinutes().toString().padStart(2, '0')}`
}
```

This is the only timestamp-rendering path in the customer SPA (grep `getHours\|getMinutes\|toLocaleTimeString` returns just this line). It produces `"19:42"` regardless of locale.

For Romania the 24-hour format is correct; for English-speaking visitors the convention is locale-dependent (American English is 12-hour, British English is 24-hour, Australia is 12-hour). `Intl.DateTimeFormat` resolves all three from the active locale:

```ts
const timeFormat = new Intl.DateTimeFormat(locale.value === 'ro' ? 'ro-RO' : 'en-GB', {
  hour: '2-digit',
  minute: '2-digit',
})
return timeFormat.format(d)
```

The line that's the actual concern — for a diner who selected English, every timeline timestamp ships as `"HH:MM"` in 24-hour format without any locale signal that the choice was deliberate. Not breaking, but the roadmap's "Number / currency / date formatting uses `Intl.*` with the active locale" item names this exact scenario.

The fix is small enough that a separate `composables/useFormatting.ts` becomes the natural home — and that helper also houses [F2](#f2--high--no-intlnumberformat-prices-render-as-raw-js-numbers-249-ron-not-2490-ron)'s currency formatter.

---

## F6 — LOW — `menu.noResultsFor` EN inherits the Romanian-style low-9 quote `„` for the EN translation

[useLocale.ts:53, 159](frontend-customer/src/composables/useLocale.ts#L53-L159):

```ts
'menu.noResultsFor': 'Niciun rezultat pentru „{query}".',   // RO
'menu.noResultsFor': 'No results for „{query}".',           // EN
```

The opening `„` (U+201E, "DOUBLE LOW-9 QUOTATION MARK") is the standard opening quote in Romanian and German typography, paired with closing `"` (U+201D). In English the conventional pair is `"` U+201C / `"` U+201D, or the typographically simpler `"…"`. The EN entry uses the RO open-quote — likely a copy/paste artifact when the EN dict was added.

Concrete render comparison:

- RO: *"Niciun rezultat pentru „pizza"."* ← correct Romanian
- EN: *"No results for „pizza"."* ← English with a low-9 quote that signals "Romanian source"

LOW because it's a typographic micro-inconsistency that no user will report. Catching it here documents the kind of polish item the segment 20 review naturally surfaces.

While noting: the closing-quote `"` (U+201D) is correct in both — only the opening got missed.

---

## What's solid

| Item | Why it's good |
|---|---|
| Single-file dict ([useLocale.ts](frontend-customer/src/composables/useLocale.ts)) | Adding a key requires both entries in the same diff — drift is visible at PR review time. Scripted diff confirms zero current drift. |
| Modern Romanian diacritics throughout | `s/t` use comma-below (`ș`, `ț`) — modern Unicode practice. Zero cedilla forms (`ş`, `ţ`) in source. |
| Module-load `localStorage` read ([line 230](frontend-customer/src/composables/useLocale.ts#L230)) | `const locale = ref<Locale>(readInitial())` — synchronous, runs before any consumer mounts. SSR fallback at [line 224](frontend-customer/src/composables/useLocale.ts#L224). |
| `try/catch` around `localStorage.setItem` ([line 232-238](frontend-customer/src/composables/useLocale.ts#L232-L238)) | Private-mode and quota-exceeded scenarios don't throw to consumers. |
| Single shared `locale` ref | Every consumer reads the same instance — `setLocale()` propagates reactively in one tick. |
| Unbound-placeholder behaviour ([line 246-249](frontend-customer/src/composables/useLocale.ts#L246-L249)) | `format()` leaves `{name}` literal if param missing — not coerced to `"undefined"`. Defensive. |
| No `v-html` on translation strings | XSS surface structurally absent. `grep v-html frontend-customer/src` returns zero hits. |
| Live screens use `t()` everywhere | Scripted scan of [components/screens/](frontend-customer/src/components/screens/) for hardcoded text outside `{{…}}` is empty. |
| Session-error overlay is i18n'd ([OrderingView.vue:92-97](frontend-customer/src/view/OrderingView.vue#L92-L97)) | The "Invalid QR code" path — the worst user state — is fully translated; `t('session.invalidTitle')`, `t('session.missingMessage')`, `t('session.invalidMessage')`. |

---

## Cross-segment follow-ups raised here

- **[Segment 18](CODE_REVIEW_ROADMAP.md#segment-18--customer-spa--shell--state):** [F1](#f1--high--four-orphan-components-still-contain-hardcoded-romanian-text-cartvue--menulistvue--flashdealbannervue--recommendationwidgetvue) — the four orphan components are also dead-code candidates the segment-18 pass would flag on a "tidy the components/ directory" sweep. Deletion is the right answer for both segment views.
- **[Segment 22](CODE_REVIEW_ROADMAP.md#segment-22--admin-spa--menu-management--import) / [Segment 23](CODE_REVIEW_ROADMAP.md#segment-23--admin-spa--orders-metrics-ai-config):** the admin SPA has its own currency / date rendering paths (`OrdersLog`, `PerformanceMetrics`, `AIConfig`). If [F2](#f2--high--no-intlnumberformat-prices-render-as-raw-js-numbers-249-ron-not-2490-ron) and [F5](#f5--medium--ordersentview-builds-timestamps-with-raw-gethoursgetminutes-no-intldatetimeformat) land here as a `composables/useFormatting.ts` helper, the admin SPA's equivalent paths can pick up the same shape via [shared/](shared/) — assuming the admin SPA's own locale story exists (it should be audited as part of those segments).
- **[Segment 24](CODE_REVIEW_ROADMAP.md#segment-24--shared-contracts-shared):** the `Locale` type and the dict shape itself could live in [shared/types/](shared/types/) for cross-SPA reuse. Not a current finding (the customer SPA is the only consumer today) but worth a note as the admin SPA grows its own locale handling.
- **[Segment 10](CODE_REVIEW_ROADMAP.md#segment-10--background--domain-services) / [Segment 11](CODE_REVIEW_ROADMAP.md#segment-11--email-subsystem):** [CODE_REVIEW_SEGMENT_11_EMAIL.md F5](CODE_REVIEW_SEGMENT_11_EMAIL.md#f5--low--subject-and-body-are-hard-coded-romanian-regardless-of-the-recipients-locale) names the email subsystem's hard-coded Romanian — that's the *server-side* twin of this segment. The customer SPA's i18n discipline is much higher than the email path; cross-pollinating the pattern (a server-side `locale` parameter on `IEmailSender.SendAsync` + template dictionaries) is the natural next step.
- **[Segment 17](CODE_REVIEW_ROADMAP.md#segment-17--ai-service-tests):** the [F4](#f4--medium--pluralisation-misses-romanians-fewmany-distinction-and-the-singular-case-of-dishescount) discussion intersects [CODE_REVIEW_SEGMENT_17_AI_TESTS.md F3](CODE_REVIEW_SEGMENT_17_AI_TESTS.md#f3--medium--no-roen-directive-parsing-test-the-roadmaps-explicit-ask-from-segment-16) — Romanian morphology matters in two places (pluralisation here, directive parsing there). Neither has tests covering the "few" form.
