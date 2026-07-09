# White-Label Branding Design

Date: 2026-07-09
Branch: feature/white-label-customization

## Purpose

Give each restaurant (tenant) the option to white-label vibeDine with its own
brand. When a business sets up its account it can optionally choose a **3-colour
scheme** (primary / secondary / background), upload a **logo**, and upload a
**presentation photo**. Those choices then re-skin the whole product: the admin
console (tenant logo in the sidebar with a small "by vibeDine" lockup, palette
drives every accent) and the customer app (a full-bleed welcome screen using the
presentation photo, the tenant logo, and a themed primary call-to-action).

Branding is optional and per-restaurant. The feature is built on the existing
theming seam — admin already renders from CSS custom properties, customer already
renders from a `T`/`TD` token object — so injecting a tenant-derived palette into
those same names re-skins the entire product with no per-component rewrite.

This work originates from an external design handoff
(`White label branding feature/design_handoff_white_label/`), which remains the
visual reference (colours, spacing, radii, copy). This spec adapts that handoff
to the repository's real architecture.

## Goals

- Persist optional branding per restaurant: `enabled` + 3 hex colours + logo URL +
  photo URL + preset key.
- Expose branding to staff via a tenant-scoped admin API (read, save, upload logo,
  upload photo) editable by non-waiter members of the restaurant.
- Expose branding to diners on the existing customer bootstrap (`join`) response.
- Derive the full ~20-token palette deterministically from the 3 chosen colours,
  with WCAG-aware contrast, using **one shared, unit-tested derivation** consumed
  by both frontends.
- Re-skin both apps at runtime when branding is enabled; fall back to the classic
  light/dark experience, unchanged, when it is not.
- Author branding in two places: a skippable onboarding step and a Settings → Brand
  tab (which can also turn branding off again).
- Add a customer Welcome screen (photo background + logo + primary CTA + a
  subtle-but-noticeable RO/EN language control) shown only when branded.

## Non-Goals

- No org-level (multi-restaurant) shared branding. Branding is strictly
  per-restaurant; an org with several restaurants brands each one independently.
- No custom fonts, custom radii, or per-component layout overrides. Only the colour
  palette, logo, and presentation photo are tenant-controlled; typography, spacing,
  and shapes stay as the product's design tokens.
- No theme *scheduling* or light/dark **variants** of a custom brand. A custom brand
  is a single authoritative palette; it does not flip to a dark variant.
- No redesign of the dashboard content or the customer menu/cart screens. They are
  already fully tokenised and re-skin automatically.
- No migration of existing tenants to branding. `enabled` defaults to `false`, so
  every existing restaurant keeps the classic experience until it opts in.
- No CDN/object-storage change for uploads. Logo/photo reuse the existing local
  `wwwroot/uploads` static-file pipeline, like avatars and menu-item photos.

## Approach Options Considered

### Persistence: scalar columns vs jsonb vs separate table (scalar columns selected)

`Restaurant` is the tenant **root** (it does not implement `ITenantScoped`; it has
no `restaurant_id`), so branding fields belong directly on the `restaurants` row.
The repo's house style is explicit, snake_case-mapped **scalar columns** (precedent:
`20260526123031_AddRestaurantLocale`), with no owned-entity/value-object pattern and
no AutoMapper anywhere in the codebase.

- **Scalar columns on `restaurants` (selected).** Seven stable fields, 1:1 with the
  restaurant, no `ITenantScoped` treatment needed (root entity). Lowest friction,
  most on-convention, trivially included wherever the restaurant row is already
  loaded (both customer bootstrap endpoints already `.Include` it).
- **`branding jsonb` column.** Consistent with existing jsonb usage
  (`MenuImportJob.ResultJson`) but less explicit and not queryable; rejected because
  the fields are stable and few.
- **Separate `restaurant_branding` table (`ITenantScoped`).** Only worth it if
  branding were optional/versioned/filtered; it would owe the full tenancy checklist
  (query filter + index + mapping + migration) for no benefit here. Rejected.

### Derivation: shared color math vs per-app helpers (shared selected)

The handoff suggests per-app helpers (`frontend-admin/src/composables/useBrandTheme.ts`).
The contrast/luminance/lighten-darken math is identical in both apps and easy to get
subtly wrong, and the same math must also power the onboarding/settings live preview.

- **Shared color math + per-app shape adapters (selected).** Put the pure math and
  the canonical palette derivation in `@shared` (unit-tested once). Each app keeps a
  thin adapter that maps the canonical palette to its output shape (admin: CSS-var
  map on `documentElement`; customer: `ThemeTokens` object). Eliminates admin/customer
  drift and preview/applied drift.
- **Per-app duplicated helper (literal handoff).** Two copies of the tricky math;
  rejected for drift risk. Cost of the selected option: `@shared`'s compiled
  `.js/.d.ts` mirror must be regenerated (repo convention).

### Customer welcome delivery (dedicated screen selected)

A dedicated `'welcome'` screen is added *before* the language picker in the
`OrderingView` screen machine, shown only when branded (photo present). Per product
decision it **subsumes** language selection: the RO/EN choice appears as a compact,
subtle-but-noticeable control beneath the CTA, so branded diners see one welcome
screen rather than two. Unbranded diners get today's language-first flow, unchanged.

## Schema

Add nullable branding columns to the existing `restaurants` table (no new table).

```
restaurants
  + branding_enabled   boolean  NOT NULL  DEFAULT false
  + brand_primary      text     NULL
  + brand_secondary    text     NULL
  + brand_background    text     NULL
  + logo_url           text     NULL
  + photo_url          text     NULL
  + brand_preset_key   text     NULL
```

Mapped snake_case in `AppDbContext.OnModelCreating` (Restaurant block, lines ~424–443),
following the `AddRestaurantLocale` precedent. Migration:
`dotnet ef migrations add AddRestaurantBranding --project backend/RestaurantApi`
(dev auto-migrates at startup; prod applies explicitly).

Colours are stored as `#RRGGBB` strings. `branding_enabled=false` with all-null
colours is the default and means "classic light/dark".

## API

### DTOs (contract slice)

- **C#** `BrandingDto { bool Enabled; string? Primary; string? Secondary; string? Background; string? LogoUrl; string? PhotoUrl; string? PresetKey; }`
  and request `UpdateBrandingRequest { bool Enabled; string? Primary; string? Secondary; string? Background; string? PresetKey; }`
  (colours only; logo/photo are set by their own upload endpoints). Live in
  `DTO/Responses` and `DTO/Requests` (namespace `RestaurantApi.DTOs.*`), manual
  mapping (no AutoMapper).
- **Customer-facing** `BrandingDto` is a trimmed shape carried on the bootstrap:
  `{ enabled, primary, secondary, background, logoUrl, photoUrl }` (no `presetKey`).
- **shared/types/models.ts** — new `RestaurantBranding` interface mirroring the wire
  shape (edit the `.ts`, regenerate the committed `.js/.d.ts`).

### Admin endpoints — tenant-scoped, non-waiter staff

New `BrandingController` under the `api/restaurants/{restaurantId:guid}/admin`
prefix so `TenantResolutionMiddleware` resolves the tenant, enforces membership, and
already path-restricts waiters. All actions carry `[Authorize]`; the middleware is
the real gate. No action accepts a client-supplied `restaurantId` in the body — the
route param + middleware are authoritative.

- `GET  /api/restaurants/{restaurantId}/admin/branding` → `BrandingDto`.
- `PUT  /api/restaurants/{restaurantId}/admin/branding` (body `UpdateBrandingRequest`)
  → saves `enabled` + 3 hexes + `presetKey`. **Server-side hex validation**
  (`^#[0-9A-Fa-f]{6}$`); reject `enabled:true` with a missing/invalid colour (400).
- `POST /api/restaurants/{restaurantId}/admin/branding/logo` (multipart, one file)
  → stores and returns `{ logoUrl }`.
- `POST /api/restaurants/{restaurantId}/admin/branding/photo` (multipart, one file)
  → stores and returns `{ photoUrl }`.

### Image upload — reuse the avatar pattern, extract the sniffer

Uploads mirror `ProfileController.UploadAvatar`:

- Refactor the inline magic-byte sniffer out of `ProfileController` into a shared
  `Services/ImageValidation.SniffImage(stream) → "png"|"jpeg"|"webp"|null` helper,
  and use it in `ProfileController`, the new logo/photo endpoints (and optionally
  `ItemPhotoStorage`). **Magic-byte check, not client MIME** (a cross-cutting
  invariant).
- Validate: non-empty, size cap, real signature; write to
  `wwwroot/uploads/branding/{restaurantId:N}/logo.{ext}` and `…/photo.{ext}`,
  delete-prior-then-write, return the root-relative `/uploads/branding/...` URL.
- Size caps: **logo ≈ 2 MB, photo ≈ 5 MB** (presentation photos are larger). Enforce
  with `[RequestSizeLimit]` per action.
- Create `uploads/branding` at startup alongside `uploads/avatars` (Program.cs ~263).

### Customer bootstrap

Add a `branding` object to **both** the live `PartyController.Join` anonymous
response (line ~119) **and** the legacy `TableSessionResponse` (CustomerController
`GetTableSession`), built inline from the already-`Include`d restaurant. When
`branding_enabled=false`, emit `branding: { enabled: false }` (or null) so the
fallback contract holds. Both endpoints stay anonymous and `qrToken`-scoped; the
diner never supplies a `restaurantId`.

## Theming engine

### Shared derivation (`shared/branding/derive.ts`)

`deriveBrandPalette({ primary, secondary, background })` → a canonical semantic
palette plus an effective mode:

```
{
  mode: 'light' | 'dark',        // from background relative luminance
  paper, paperHi, card,
  ink, ink70, ink40, ink12, ink06,
  accent, accentDk, accentSoft, accentBorder, onAccent,
  lime, onLime, gold
}
```

Derivation rules (from the handoff):

| Token | Rule |
|---|---|
| `paper` | `background` |
| `paperHi` | `background` lightened ~4% (light) / darkened ~4% (dark) |
| `card` | `#FFFFFF` if light bg; else `background` lightened ~8% |
| `ink` | warm near-black `#15140F` (light bg) / warm near-white `#F5F0E8` (dark bg), by luminance for AA |
| `ink70/40/12/06` | `rgba(ink, .62 / .34 / .12 / .05)` |
| `accent` | `primary` |
| `accentDk` | `primary` darkened ~14% |
| `accentSoft` / `accentBorder` | `rgba(primary, .10)` / `rgba(primary, .30)` |
| `onAccent` | `bestOnColor(primary)` → `#fff` or `ink` (higher WCAG contrast) |
| `lime` | `secondary` |
| `onLime` | `bestOnColor(secondary)` |
| `gold` | `secondary` (warm-shifted) |

Helpers (pure, unit-tested): `hexToRgb`, `relativeLuminance`, `contrastRatio`,
`lighten(hex, pct)`, `darken(hex, pct)`, `bestOnColor(bg, ink) → '#fff' | ink`.
`mode` is `'dark'` when the background's relative luminance is below a threshold
(so the **Azur** dark preset is treated correctly).

### Admin adapter (`frontend-admin/src/composables/useBrandTheme.ts`)

Maps the canonical palette to `--var` names and writes each onto
`document.documentElement.style` (overriding the `:root` defaults in `style.css`).
`applyBrand(palette)` sets them; `clearBrand()` removes them. Because inline vars on
`documentElement` win over `:root`/`:root[data-theme]`, the injected palette is
authoritative regardless of `data-theme`.

### Customer adapter + the primitives gotcha

`frontend-customer/src/composables/useTheme.ts` `tokens` becomes: when
`session.branding.enabled`, return a `ThemeTokens` object built from the shared
palette (adapting to the customer's token keys, keeping `sans/display/mono` and
`statusClear`); otherwise the current `theme === 'dark' ? TD : T`. `useTheme` also
exposes an effective `mode` (from the palette) for light/dark-conditional bits.

`PhoneScreen.vue` and `BigButton.vue` currently bypass `useTheme` and read `T`/`TD`
directly from a `theme` prop. Refactor both to resolve tokens from `useTheme().tokens`
(falling back to the `theme` prop only when a caller explicitly overrides), so brand
tokens reach them automatically and their light/dark-conditional details (noise
overlay opacity, arrow defaults) follow the derived `mode`.

### Locked-scheme behaviour

When `enabled`: injected vars / brand tokens win, the light/dark **toggle is hidden**
(`v-if="!isBranded"` on the toggle buttons in admin `App.vue` and the customer
screens that expose it), and `useTheme` stops flipping `data-theme` (admin) / stops
swapping `T`/`TD` (customer). The server is the source of truth; `useTheme`'s
persisted `light|dark` is used only for the non-branded fallback.

## Admin UI

### State (`frontend-admin/src/stores/useBrandingStore.ts`)

New Pinia store: `branding`, `isEnabled`, `load(restaurantId)`, `save(draft)`,
`uploadLogo(file)`, `uploadPhoto(file)`, `disable()`. Wired into `App.vue`'s existing
`watch(() => auth.activeRestaurantId)`: on resolve, `load()` then `applyBrand()` (or
`clearBrand()` when disabled). Nothing extra persisted to `localStorage` — server is
authoritative.

### Onboarding step (`OnboardingView.vue`)

Restructure the current single submit (which creates org + restaurant, then
redirects) into two steps in the same view:

- **Step 1** — the existing org + restaurant form. On submit, create org + restaurant
  (as today) but advance to step 2 instead of redirecting.
- **Step 2 "Personalizează-ți brandul"** — preset swatch grid + custom 3-colour
  fields + logo dropzone + photo dropzone, with a **live preview** (mini phone
  welcome + mini dashboard chip) that re-tints from *draft* state via a scoped style
  object (not global vars, so the onboarding chrome itself is not tinted).
  "Finalizează și intră în cont" saves `enabled:true` (+ uploaded assets); "Sări
  peste" leaves `enabled:false`. Both then route to `/select-organization` as today.

Copy is Romanian, matching the existing onboarding voice.

### Settings → Brand tab

`frontend-admin/src/views/Settings/BrandTab.vue` + a tile in `SettingsLayout.vue`
(the `computed<Tile[]>`) + a child route in `router/index.ts` (the dual
registration). Gated with the same `!auth.isWaiter` condition as the other
management tabs. Contents: the same controls as the onboarding step **plus a master
`Personalizare brand` on/off toggle**. Toggling off → `save({ enabled:false })`,
`clearBrand()`, re-show the theme toggle. Editing → save + re-inject optimistically.
Logo/photo upload reuses the `ProfileTab` file-input convention.

### Sidebar brand block + "by vibeDine" lockup

In `App.vue`'s desktop brand block (lines ~258–264): if `isEnabled`, render the
tenant logo (`resolveMediaUrl(logoUrl)`, height ~72, `object-fit:contain`) and drop
the big restaurant-name text; underneath it a small **"by vibeDine" lockup**. If not
enabled, render today's `<BrandLogo />` + restaurant name unchanged.

Extract the pebble into a small `frontend-admin/src/components/VibeDineLockup.vue`
(reusing `PEBBLE_PATH`, gradients, strokes from `BrandLogo.vue`) whose fill/stroke
follow the tenant scheme (`fill: var(--accent)`, outer `stroke: var(--lime)`,
`Dine` wordmark `var(--accent)`). The pebble *shape* stays constant so "powered by
vibeDine" remains recognisable. This component is shared with the customer welcome
(a customer-app copy, since the two apps do not share Vue components — only
`@shared` TS).

## Customer welcome screen

Add `'welcome'` to the `Screen` union in `OrderingView.vue`, positioned before
`'language'`. Initial screen: `branding.enabled && photoUrl ? 'welcome' : 'language'`
(the classic flow is untouched when unbranded). Both `'welcome'` and `'language'`
transition to `'pebbles'`.

`frontend-customer/src/components/screens/WelcomeView.vue`:

- Full-bleed `resolveMediaUrl(photoUrl)` background, `background-size:cover`, with the
  top-to-bottom scrim
  `linear-gradient(180deg, rgba(0,0,0,.25), rgba(0,0,0,.28) 40%, rgba(0,0,0,.86))`.
  No photo → falls back to `T.paper` (should not happen, since `'welcome'` requires a
  photo).
- Centred column: eyebrow `Bine ai venit la` (mono, uppercase, white), the logo on a
  translucent blurred plate (`rgba(255,255,255,.95)`, radius 26, `backdrop-filter:
  blur(10px)`), then a white "by vibeDine" lockup (pebble fill = primary, stroke =
  secondary, `Dine` lightened primary for contrast on the dark scrim).
- Bottom: a primary `BigButton` `Vezi meniul` (brand primary), and **beneath it a
  compact but clearly tappable RO/EN control**. Picking a language calls
  `setLocale`; the CTA advances to `'pebbles'`. The welcome screen thus subsumes the
  language picker when branded.
- `PhoneScreen` stays the frame (`flush` where the photo must bleed to the top).

`useSessionStore` gains `branding` state (populated from the bootstrap response), and
the customer `resolveResponseMediaUrls` key list gains `logoUrl`/`photoUrl` so those
paths resolve cross-origin (today it rewrites only `imageUrl`/`avatarUrl`).

## Contract sync checklist (full-stack slice)

A branding change touches all of:

- C# entity + migration + `AppDbContext` mapping.
- C# DTOs (`BrandingDto`, `UpdateBrandingRequest`) + `BrandingController` + upload
  endpoints + `ImageValidation`.
- Customer bootstrap: `PartyController.Join` + `TableSessionResponse`.
- `shared/types/models.ts` `RestaurantBranding` (+ regenerate compiled mirror).
- `shared/branding/derive.ts` (+ compiled mirror) — shared math.
- Admin: `useBrandingStore`, `useBrandTheme`, `App.vue` (sidebar + hide toggle +
  bootstrap wiring), `OnboardingView`, `SettingsLayout` + `BrandTab` + route,
  `VibeDineLockup`.
- Customer: local `JoinTableResponse`/`TableSession` interfaces, `useSessionStore`,
  `useTheme`, `PhoneScreen`/`BigButton` refactor, `WelcomeView`, `OrderingView`
  screen machine, `mediaUrl` key list, `VibeDineLockup` copy.

## Testing

- **Backend** (`dotnet test backend/RestaurantApi.Tests`, requires Docker /
  Testcontainers):
  - Branding `PUT`/`GET` round-trip.
  - **Tenant isolation**: a member of restaurant A cannot read or write restaurant
    B's branding (403/404 via middleware) — the #1 invariant.
  - Hex validation rejects bad colours; `enabled:true` with a missing colour is 400.
  - Upload rejects a non-image by **magic bytes** (renamed `.png` that is not PNG),
    enforces the size cap, and a waiter is forbidden.
  - Customer bootstrap includes `branding` when enabled and the fallback shape when
    disabled.
- **Shared derivation** (unit tests): `contrastRatio`, `lighten/darken`,
  `bestOnColor`, and AA thresholds — assert `onAccent`/`onLime`/`ink` meet WCAG AA
  against their backgrounds for **all four presets, including the Azur dark
  background**.
- **Admin** (`npm test`, vitest + MSW): `useBrandingStore` load/save/disable;
  `BrandTab` enable→apply→disable; toggle hidden when branded.
- **Customer**: no test harness exists (stub) — manual smoke only.
- **Type-check** both frontends (`npm run type-check`); customer also `npm run lint`.
  Regenerate `@shared` compiled mirrors and confirm both apps still build.

## Phased implementation

Coherent full-stack slices, each independently verifiable (matching the repo's
numbered-chunks convention):

1. **Backend foundation + contract.** Columns + migration; `BrandingDto` /
   `UpdateBrandingRequest`; `BrandingController` (GET/PUT + logo/photo upload);
   `ImageValidation` extraction; branding on both customer bootstrap responses;
   `shared/types` + mirror. Verify via Swagger + a raw customer bootstrap payload.
2. **Theming engine.** `shared/branding/derive.ts` + unit tests; admin `useBrandTheme`
   + `useBrandingStore` + apply-on-bootstrap; customer `useTheme` brand path +
   `PhoneScreen`/`BigButton` refactor + `useSessionStore` branding + `mediaUrl` keys.
   Verify: set branding directly in the DB → both apps re-skin, toggle hidden.
3. **Admin authoring UI.** Settings → Brand tab (edit + enable/disable) as the primary
   surface; then the skippable onboarding step; then the sidebar tenant logo +
   `VibeDineLockup` + hide-toggle.
4. **Customer welcome.** `WelcomeView` + screen-machine integration + fallback +
   language-underneath control.
5. **QA / polish.** Contrast AA verification on derived tokens (incl. Azur);
   skip/save/disable paths; both-app smoke with the Supporter example assets
   (`design_handoff_white_label/assets/`, dev-only — not shipped).

## Cross-cutting invariants honoured

- **Multi-tenancy (#1):** admin branding endpoints are tenant-scoped via
  `TenantResolutionMiddleware` (membership + waiter path-restriction); no client
  supplies a `restaurantId`; customer endpoints trust `qrToken` only. Tenant
  isolation is explicitly tested.
- **Contract changes are full-stack slices:** C# DTOs ↔ `shared/types` ↔ both
  frontends kept in sync (checklist above).
- **File uploads:** magic-byte validation, not client MIME.
- **UI language:** admin strings Romanian; customer strings RO + EN via `useLocale`.
- **Timestamps** (if any added) UTC — none needed here.
- **AI providers / money:** not touched.
