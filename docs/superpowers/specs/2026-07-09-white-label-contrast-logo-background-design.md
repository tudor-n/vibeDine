# White-Label Smart Contrast & Configurable Logo Background — Design

Date: 2026-07-09
Branch: feature/white-label-customization

Extends the base feature (`2026-07-09-white-label-branding-design.md`). Read that
first for the palette-derivation architecture this builds on.

## Purpose

Two refinements to the white-label feature so a tenant's chosen colours always look
intentional and legible:

1. **Smart auto-contrast text.** Every piece of text should automatically render
   dark or light based on the colour it actually sits on, guaranteeing readability
   (WCAG AA) for *any* admin-picked scheme — on both the customer and dashboard
   sides. The base feature already derives text colours with WCAG-aware math, but it
   does so against a hard luminance *threshold* rather than the real per-surface
   contrast, and the customer adapter drops several on-surface tokens. This closes
   those gaps.

2. **Configurable logo background.** Today the tenant logo is always framed on a
   hardcoded white plate (customer welcome screen, admin preview) or no plate at all
   (admin sidebar). Give the tenant control: **Auto** (a smart default that adapts to
   the logo's own lightness), **Transparent** (no plate — for knockout/white logos),
   **White**, or a **Custom** colour.

Both refinements land on the existing derivation seam so they propagate to every
consumer without per-component rewrites.

## Goals

- Text colour is chosen by **measured contrast against its own surface**, not a
  luminance threshold, with a guaranteed pure-black/white fallback so contrast is
  never below AA (4.5:1) on any surface colour a tenant can pick.
- The customer runtime applies **every** on-surface token the components consume
  (close the `onLime`/`accentSoft`/`accentBorder`/`gold` overlay gap in the customer
  adapter), so branded lime/secondary surfaces get contrast-correct text like the
  admin already does.
- Persist an optional `logoBackground` per restaurant (`null` = Auto, `"transparent"`,
  or a hex colour) plus a `logoIsDark` hint used to make Auto smart.
- Resolve the logo plate identically across admin sidebar, admin live preview, and
  customer welcome screen via **one shared pure helper**.
- Author the logo background in the existing Settings → Brand controls with an
  `Auto / Alb / Transparent / Personalizat` control (Romanian admin copy).
- Fall back gracefully: schemes and logos saved before this change keep working
  (`logoBackground = null`, `logoIsDark = null` → today's light-plate behaviour).

## Non-Goals

- No server-side image processing. The `logoIsDark` hint is computed client-side
  (canvas over the locally-selected file) at upload time and persisted; the backend
  only stores it.
- No per-token manual override of text colour. Contrast is fully automatic — no
  "force light/dark text" knob (confirmed with the user).
- No new logo render sites. Only the three existing plate sites change.
- No change to the welcome-screen scrim text (it sits on a guaranteed-dark photo
  gradient — white there is correct by construction).
- No change to the non-branded (classic light/dark) experience.

## Approach Options Considered

### Contrast engine

- **(A) Keep the threshold model, widen tests.** Leave `ink` keyed on the 0.35
  luminance cliff and `bestOnColor` binary white-vs-`#15140F`. Cheapest, but a
  mid-tone background (~0.18–0.36 luminance) can still put the soft brand ink below
  4.5:1, and the choice isn't actually contrast-driven — it just happens to pass for
  the 4 presets.
- **(B) Contrast-maximising with AA fallback — chosen.** Pick each text colour by
  the higher *measured* `contrastRatio` against its surface; if even the best soft
  brand ink is < 4.5:1, upgrade to pure `#000`/`#FFF` (mathematically ≥ 4.58:1 on any
  colour). This is what "text turns dark/white based on what it's on" actually means,
  and it holds for arbitrary custom colours, not just the presets.

### Logo Auto background

- **(A) Auto = always white + override only.** Simplest, but "Auto" would just
  relabel today's behaviour — not smart. A white/knockout logo still lands in a white
  box unless the tenant manually intervenes.
- **(B) Auto = logo-lightness-aware + override — chosen.** Persist a `logoIsDark`
  hint computed once at upload. Auto resolves a **dark logo → light plate** (frames
  it) and a **light logo → transparent** (reads on dark surfaces instead of
  vanishing). ~15 lines of canvas in the admin, zero analysis on the customer's
  critical path (it reads the persisted hint), graceful when the hint is absent.
- **(C) Full manual, no Auto.** Rejected — the user explicitly chose "Auto + optional
  override".

## Architecture

The whole change funnels through `shared/branding/derive.ts`, the single pure module
both frontends import. Admin injects its output as CSS custom properties
(`useBrandTheme.ts`); customer injects it as a token object (`useTheme.ts`).
Improving contrast in the derivation propagates to every text surface with no
component edits on the admin side and only an adapter fix on the customer side.

### Component: contrast primitives (`shared/branding/derive.ts`)

`relativeLuminance`, `contrastRatio`, `hexToRgb` already exist. Add/replace:

- `readableText(surface, darkCand, lightCand): string` — the one general helper:
  returns whichever of the two candidates contrasts better with `surface`; if that
  best is below the AA target (`CONTRAST_AA = 4.5`), upgrades to pure `#000000`/`#FFFFFF`
  (whichever side wins), guaranteeing ≥4.58:1 on any colour.
  - `ink = readableText(paper, LIGHT_INK, DARK_INK)` (soft brand inks
    `#15140F`/`#F5F0E8`).
  - `bestOnColor` is **reimplemented in terms of** `readableText` — its existing
    signature `bestOnColor(bg, ink)` becomes `readableText(bg, ink, WHITE)`, so
    `onAccent`/`onLime` keep their call sites but now gain the pure-`#000`/`#FFF`
    fallback. Keeping the name preserves the existing test call sites.
- `deriveBrandPalette` changes: `ink = readableInk(paper)`; `onAccent =
  readableInk(primary, whiteBiased)`; `onLime = readableInk(secondary, whiteBiased)`.
  `mode` still derives from the 0.35 threshold (it drives *surface* lightening
  direction and the customer noise overlay, not text legibility) — decoupling text
  choice from `mode` is the whole point.
  - Rationale that a single `ink` suffices for all neutral surfaces: in light mode
    `card`/`paperHi` are *lighter* than `paper`, in dark mode they are still dark
    (`lighten(bg, ≤0.1)` of a sub-0.35 background). `paper` is therefore the
    lowest-contrast neutral in both modes, so `readableInk(paper)` is safe on all of
    them. `accent`/`lime`/`gold` are the only surfaces that flip, and they have
    dedicated on-colours.

### Component: logo-background resolver (`shared/branding/derive.ts`)

```
resolveLogoBackground(input: {
  logoBackground: string | null   // null=auto | "transparent" | "#RRGGBB"
  logoIsDark: boolean | null
  palette: BrandPalette
}): { background: string; plate: boolean }
```

- `"transparent"` → `{ background: 'transparent', plate: false }`
- hex → `{ background: hex, plate: true }`
- Auto (`null`):
  - `logoIsDark === false` (light/knockout logo) → `{ background: 'transparent', plate: false }`
  - otherwise (dark logo, or unknown hint) → `{ background: '#FFFFFF', plate: true }`

`plate: false` means callers render the logo with no plate chrome (no padding box /
shadow); `plate: true` renders the framed plate using `background`.

### Component: logo-lightness detection (admin only)

A small util (e.g. `frontend-admin/src/utils/logoLuminance.ts`) draws the selected
logo `File` onto an offscreen canvas (via `createObjectURL`, so no CORS), samples
average relative luminance weighted by alpha (fully-transparent pixels ignored), and
returns `isDark = avgLuminance < 0.5`. Called on logo file-select in the Brand
controls; the result is stored on the draft and persisted with the next save.

## Data Model & Wire Contract

Two new nullable columns on `restaurants`:

| Column | Type | Meaning |
|---|---|---|
| `logo_background` | `text?` | `null`=Auto, literal `transparent`, or `#RRGGBB` |
| `logo_is_dark` | `boolean?` | client-computed hint; `null` when unknown |

- **Entity** `Models/Restaurant.cs`: `LogoBackground`, `LogoIsDark`.
- **Migration**: `AddRestaurantLogoBackground` (both nullable, no default → existing
  rows are Auto/unknown).
- **DTOs**: add `LogoBackground` + `LogoIsDark` to `UpdateBrandingRequest` and
  `BrandingResponse`.
- **Controller** `BrandingController`: persist both in `PUT`; return both in `GET`
  and in `ToResponse`. Validation: `LogoBackground` must be `null`, `"transparent"`,
  or match the existing `^#[0-9A-Fa-f]{6}$` hex regex, else `400` (Romanian message).
  `LogoIsDark` is a plain nullable bool.
- **Customer bootstrap**: the branding object on the join response must include
  `logoBackground` and `logoIsDark` (extend the same projection that already emits
  `logoUrl`/`photoUrl`).
- **shared** `shared/types/models.ts`: `RestaurantBranding` gains
  `logoBackground: string | null` and `logoIsDark: boolean | null`. Edit the `.ts`
  and regenerate the committed `.js`/`.d.ts` mirror.

## Frontend Wiring

### Admin

- `presets.ts` `BrandDraft`: add `logoBackground: string | null`, `logoIsDark: boolean | null`.
- `useBrandingStore`: carry both through load/save.
- `BrandControls.vue`: new "Fundal logo" control — segmented
  `Auto / Alb / Transparent / Personalizat`; when `Personalizat`, show a
  `<input type="color">`. Selecting `Alb` sets `#FFFFFF`, `Transparent` sets
  `"transparent"`, `Auto` sets `null`. On logo file-select, compute `logoIsDark` and
  patch the draft.
- `BrandPreview.vue`: mini-phone logo plate uses `resolveLogoBackground` instead of
  the hardcoded `rgba(255,255,255,0.94)`, so the preview reflects the choice live.
- `App.vue` sidebar: wrap the tenant logo with the resolver output (plate/background).

### Customer

- `useTheme.ts` `applyBrand`: overlay **all** palette on-surface tokens the token
  object exposes — add `onLime` (and `accentSoft`/`accentBorder`/`gold` if consumed)
  to `ThemeTokens` (`design/tokens.ts`) and set them from the derived palette so
  branded lime/secondary surfaces get contrast-correct text.
- `useSessionStore`: pass `logoBackground`/`logoIsDark` from the join branding into
  the welcome screen (they already live on `session.branding`).
- `WelcomeView.vue`: `.logo-plate` background/chrome comes from
  `resolveLogoBackground(...)`; when `plate === false`, render the logo with no plate
  (drop the white box, padding, and shadow).

## Testing

- **`shared` / admin `brandDerive.spec.ts`** (extend): assert `contrastRatio(ink,
  paper) ≥ 4.5`, `contrastRatio(onAccent, accent) ≥ 4.5`, `contrastRatio(onLime,
  lime) ≥ 4.5` across the 4 presets **plus** adversarial mid-tone backgrounds/accents
  (e.g. `#808080`, `#7A6E5A`, a ~0.18-luminance colour) where the old soft ink would
  have dipped below AA. Assert the pure-black/white fallback triggers on those.
- **`resolveLogoBackground` unit tests**: transparent / hex / auto-dark-logo /
  auto-light-logo / auto-unknown-hint.
- **`logoLuminance` unit test** (admin): a synthetic dark vs light image returns the
  expected `isDark` (or test the pure luminance-averaging function directly).
- **Backend** `BrandingApiTests`: `PUT` round-trips `logoBackground` + `logoIsDark`;
  invalid `logoBackground` (`"red"`, `"#FFF"`, `"#12345"`) → `400`; `GET` and the
  customer bootstrap both surface the fields.

## Rollout / Commits

Coherent full-stack chunks, committed as work proceeds (user pre-authorised commits):

1. `feat(branding): contrast-maximising text with AA fallback + logo-bg resolver` —
   `derive.ts` + derivation tests.
2. `feat(branding): persist logoBackground + logoIsDark` — entity, migration, DTOs,
   controller, customer bootstrap, shared types, backend tests.
3. `feat(branding): logo-background control + smart plate in admin` — BrandControls,
   preview, sidebar, logo-luminance util, store/draft.
4. `feat(branding): apply logo-background resolver + full token overlay in customer` —
   customer adapter gap + WelcomeView plate.
```
