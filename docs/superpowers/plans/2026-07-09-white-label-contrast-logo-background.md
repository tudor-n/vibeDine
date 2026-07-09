# White-Label Smart Contrast & Configurable Logo Background — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make white-label text auto-pick dark/light per surface (WCAG-AA guaranteed) on both apps, and make the logo background configurable (Auto / White / Transparent / Custom) with a smart Auto that adapts to the logo's own lightness.

**Architecture:** Everything routes through the one shared pure module `shared/branding/derive.ts` (admin injects its output as CSS vars, customer as a token object). We add contrast-maximising text selection and a logo-background resolver there, persist two new branding fields end-to-end, add one admin control, and close the customer adapter's on-lime gap.

**Tech Stack:** .NET 8 / EF Core / PostgreSQL (backend); Vue 3 + TS + Vite + Pinia (both frontends); Vitest (admin tests); xUnit + Testcontainers (backend tests); hand-synced TS contracts in `shared/`.

## Global Constraints

- **Multi-tenancy:** branding is already tenant-scoped via `TenantResolutionMiddleware` + the `restaurants` row. No new endpoint; reuse the existing `BrandingController` PUT/GET. Never accept a client-supplied `restaurantId`.
- **Money/timestamps:** not touched here.
- **Contract sync (3 places):** C# DTOs ↔ `shared/types/models.ts` (edit `.ts`, regen committed `.js`/`.d.ts`) ↔ frontend usage. `RestaurantBranding` is the wire type used by both frontends.
- **Admin copy is Romanian.** Customer copy is RO/EN (no new customer strings here).
- **Hex validation regex (backend, existing):** `^#[0-9A-Fa-f]{6}$`.
- **`logoBackground` domain:** `null` (Auto) | literal `"transparent"` | 6-digit hex. **`logoIsDark` domain:** `true` | `false` | `null`.
- **Contrast target:** `CONTRAST_AA = 4.5`.
- **Commits:** user pre-authorised. End commit messages with the `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>` trailer.
- **Test commands:**
  - Shared/derive + resolver + admin: `cd frontend-admin && npx vitest run <file>` ; type-check `npm run type-check`.
  - Backend: `dotnet test backend/RestaurantApi.Tests --filter "FullyQualifiedName~Branding"` (**Docker required**).
  - Customer: no test runner — verify with `cd frontend-customer && npm run type-check && npm run lint`.

---

### Task 1: Contrast-maximising text selection in the derivation

**Files:**
- Modify: `shared/branding/derive.ts`
- Test: `frontend-admin/src/__tests__/brandDerive.spec.ts`

**Interfaces:**
- Consumes: existing `relativeLuminance`, `contrastRatio`, constants `LIGHT_INK='#15140F'`, `DARK_INK='#F5F0E8'`, `WHITE='#FFFFFF'`.
- Produces: `readableText(surface: string, darkCandidate: string, lightCandidate: string): string`; `bestOnColor(bg: string, ink: string): string` (unchanged signature, now AA-fallback-backed); `deriveBrandPalette` returns the same `BrandPalette` shape but with contrast-driven `ink`/`onAccent`/`onLime`.

- [ ] **Step 1: Write failing tests** — append to `frontend-admin/src/__tests__/brandDerive.spec.ts`:

```ts
import {
  deriveBrandPalette, contrastRatio, bestOnColor, readableText,
} from '@shared/branding/derive'

// ...existing PRESETS + tests stay...

describe('readableText (AA-guaranteed contrast)', () => {
  const AA = 4.5
  // Mid-tone surfaces where the soft brand inks alone dip below AA.
  const MIDTONES = ['#808080', '#7A6E5A', '#8A7B33', '#6E7B8A', '#A08050']

  it('always returns a colour meeting AA on any mid-tone surface', () => {
    for (const s of MIDTONES) {
      const c = readableText(s, '#15140F', '#F5F0E8')
      expect(contrastRatio(c, s)).toBeGreaterThanOrEqual(AA)
    }
  })

  it('upgrades to pure black/white when the soft candidates fall short', () => {
    // #7F7F7F: neither soft ink hits AA, so a pure endpoint must be returned.
    const c = readableText('#7F7F7F', '#15140F', '#F5F0E8')
    expect(['#000000', '#FFFFFF']).toContain(c)
  })

  it('prefers a passing soft candidate over a pure endpoint', () => {
    // On a light paper, the soft dark ink already passes — keep it (warmth).
    expect(readableText('#F5EEE3', '#15140F', '#F5F0E8')).toBe('#15140F')
  })
})

describe('deriveBrandPalette contrast on adversarial custom schemes', () => {
  const AA = 4.5
  const SCHEMES = [
    { primary: '#808080', secondary: '#7A6E5A', background: '#6E7B8A' },
    { primary: '#3E7BFA', secondary: '#F0A836', background: '#14161C' }, // azur
    { primary: '#C8B8A0', secondary: '#9A8C6E', background: '#EDE7DA' }, // muted light
  ]
  it('ink/onAccent/onLime all meet AA against their own surface', () => {
    for (const s of SCHEMES) {
      const p = deriveBrandPalette(s)
      expect(contrastRatio(p.ink, p.paper)).toBeGreaterThanOrEqual(AA)
      expect(contrastRatio(p.onAccent, p.accent)).toBeGreaterThanOrEqual(AA)
      expect(contrastRatio(p.onLime, p.lime)).toBeGreaterThanOrEqual(AA)
    }
  })
})
```

- [ ] **Step 2: Run to verify failure**

Run: `cd frontend-admin && npx vitest run src/__tests__/brandDerive.spec.ts`
Expected: FAIL — `readableText` is not exported; the adversarial `#808080` scheme fails AA with the current soft-ink logic.

- [ ] **Step 3: Implement in `shared/branding/derive.ts`**

Add near the other constants:

```ts
const PURE_BLACK = '#000000'
const CONTRAST_AA = 4.5
```

Add the helper (place it just above `bestOnColor`):

```ts
/**
 * The most readable text colour on `surface`. Returns whichever of the two
 * soft candidates has the higher contrast; if even that is below WCAG AA,
 * upgrades to pure black/white (guaranteed >= 4.58:1 on any colour), keeping
 * the ends on the same light/dark side as the winning soft candidate.
 */
export function readableText(surface: string, darkCandidate: string, lightCandidate: string): string {
  const dark = contrastRatio(surface, darkCandidate)
  const light = contrastRatio(surface, lightCandidate)
  const softWins = dark >= light ? darkCandidate : lightCandidate
  if (contrastRatio(surface, softWins) >= CONTRAST_AA) return softWins
  return dark >= light ? PURE_BLACK : WHITE
}
```

Replace `bestOnColor` so it inherits the AA fallback (keep the signature):

```ts
/** Most readable of white / the given ink on `bg`, AA-guaranteed. */
export function bestOnColor(bg: string, ink: string): string {
  return readableText(bg, ink, WHITE)
}
```

In `deriveBrandPalette`, change the three text tokens (leave `mode`/surfaces as-is):

```ts
  const ink = readableText(background, LIGHT_INK, DARK_INK)
  // ...
    onAccent: bestOnColor(primary, LIGHT_INK),
    // ...
    onLime: bestOnColor(secondary, LIGHT_INK),
```

(`ink` was `isDark ? DARK_INK : LIGHT_INK`; it is now contrast-driven. `mode`, `paper`, `paperHi`, `card`, and the `ink70..ink06` alphas still derive from `ink`/`isDark` exactly as before.)

- [ ] **Step 4: Run tests to verify pass**

Run: `cd frontend-admin && npx vitest run src/__tests__/brandDerive.spec.ts`
Expected: PASS (all existing + new cases).

- [ ] **Step 5: Type-check**

Run: `cd frontend-admin && npm run type-check`
Expected: no errors.

- [ ] **Step 6: Commit**

```bash
git add shared/branding/derive.ts frontend-admin/src/__tests__/brandDerive.spec.ts
git commit -m "feat(branding): AA-guaranteed contrast-maximising text selection"
```

---

### Task 2: Logo-background resolver

**Files:**
- Modify: `shared/branding/derive.ts`
- Test: `frontend-admin/src/__tests__/logoBackground.spec.ts` (create)

**Interfaces:**
- Produces: `resolveLogoBackground(logoBackground: string | null, logoIsDark: boolean | null): { background: string; plate: boolean }`.

- [ ] **Step 1: Write failing test** — create `frontend-admin/src/__tests__/logoBackground.spec.ts`:

```ts
import { describe, it, expect } from 'vitest'
import { resolveLogoBackground } from '@shared/branding/derive'

describe('resolveLogoBackground', () => {
  it('transparent → no plate', () => {
    expect(resolveLogoBackground('transparent', null)).toEqual({ background: 'transparent', plate: false })
  })
  it('explicit hex → plate with that colour', () => {
    expect(resolveLogoBackground('#123456', true)).toEqual({ background: '#123456', plate: true })
  })
  it('auto + dark logo → white plate', () => {
    expect(resolveLogoBackground(null, true)).toEqual({ background: '#FFFFFF', plate: true })
  })
  it('auto + light logo → transparent (reads on dark surfaces)', () => {
    expect(resolveLogoBackground(null, false)).toEqual({ background: 'transparent', plate: false })
  })
  it('auto + unknown lightness → white plate (safe default)', () => {
    expect(resolveLogoBackground(null, null)).toEqual({ background: '#FFFFFF', plate: true })
  })
})
```

- [ ] **Step 2: Run to verify failure**

Run: `cd frontend-admin && npx vitest run src/__tests__/logoBackground.spec.ts`
Expected: FAIL — `resolveLogoBackground` not exported.

- [ ] **Step 3: Implement** in `shared/branding/derive.ts` (append at end):

```ts
/**
 * Resolve the plate behind a tenant logo.
 *   logoBackground: null = Auto | "transparent" | "#RRGGBB"
 *   logoIsDark: client-computed hint about the logo's own lightness (null = unknown)
 * Auto frames a dark logo on a white plate, and lets a light/knockout logo sit
 * transparent so it reads on dark surfaces instead of vanishing in a white box.
 */
export function resolveLogoBackground(
  logoBackground: string | null,
  logoIsDark: boolean | null,
): { background: string; plate: boolean } {
  if (logoBackground === 'transparent') return { background: 'transparent', plate: false }
  if (logoBackground) return { background: logoBackground, plate: true }
  if (logoIsDark === false) return { background: 'transparent', plate: false }
  return { background: WHITE, plate: true }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `cd frontend-admin && npx vitest run src/__tests__/logoBackground.spec.ts`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add shared/branding/derive.ts frontend-admin/src/__tests__/logoBackground.spec.ts
git commit -m "feat(branding): logo-background resolver (auto/transparent/custom)"
```

---

### Task 3: Shared wire type — `logoBackground` + `logoIsDark`

**Files:**
- Modify: `shared/types/models.ts` (the `RestaurantBranding` interface)
- Modify (regen): `shared/types/models.js`, `shared/types/models.d.ts` (committed mirrors)

**Interfaces:**
- Produces: `RestaurantBranding.logoBackground: string | null`, `RestaurantBranding.logoIsDark: boolean | null` — consumed by both frontends and Tasks 6–8.

- [ ] **Step 1: Edit the `.ts` source** — in `shared/types/models.ts`, extend `RestaurantBranding`:

```ts
export interface RestaurantBranding {
  enabled: boolean;
  primary: string | null;
  secondary: string | null;
  background: string | null;
  logoUrl: string | null;
  photoUrl: string | null;
  presetKey?: string | null;
  logoBackground: string | null;
  logoIsDark: boolean | null;
}
```

- [ ] **Step 2: Regenerate the committed mirror** — mirror the two new fields into `shared/types/models.d.ts` (add the two lines to the `RestaurantBranding` declaration). `models.js` needs no change if it emits no runtime members for this interface (interfaces erase); open it and confirm there is no `RestaurantBranding` runtime object — if there is, leave it untouched (interfaces have no JS emit). Do not hand-edit beyond matching the `.ts`.

- [ ] **Step 3: Type-check both frontends compile against the new optional-free fields**

Run: `cd frontend-admin && npm run type-check`
Expected: FAILs where a `RestaurantBranding` object literal is constructed without the new **required** fields — that is expected; those sites are fixed in Tasks 6/8. If type-check fails ONLY in `useBrandingStore.ts`/`BrandTab.vue`/customer store (object construction), proceed; those are the wiring tasks. If it fails anywhere else, note it.

> Note: the two fields are non-optional to force every construction site to be explicit. All construction sites are updated in Tasks 6 and 8; the customer only *reads* `branding` (already `RestaurantBranding | null` from the wire), so it needs no literal.

- [ ] **Step 4: Commit**

```bash
git add shared/types/models.ts shared/types/models.d.ts shared/types/models.js
git commit -m "feat(branding): add logoBackground + logoIsDark to wire contract"
```

---

### Task 4: Backend persistence, validation & customer bootstrap

**Files:**
- Modify: `backend/RestaurantApi/Models/Restaurant.cs`
- Modify: `backend/RestaurantApi/Data/AppDbContext.cs:436-442` (add two column mappings)
- Modify: `backend/RestaurantApi/DTO/Requests/BrandingRequests.cs`
- Modify: `backend/RestaurantApi/DTO/Responses/BrandingResponse.cs`
- Modify: `backend/RestaurantApi/Controllers/BrandingController.cs` (validation + persist + `ToResponse`)
- Modify: `backend/RestaurantApi/Controllers/PartyController.cs:129-137` (anonymous branding projection on join)
- Modify: `backend/RestaurantApi/Controllers/CustomerController.cs:351-359` + the `BrandingInfo` DTO it uses
- Create: migration `backend/RestaurantApi/Migrations/*_AddRestaurantLogoBackground.cs` (via `dotnet ef`)
- Test: `backend/RestaurantApi.Tests/Branding/BrandingApiTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks (backend is contract-parallel to `shared`).
- Produces: GET/PUT `api/restaurants/{id}/admin/branding` round-trips `logoBackground` + `logoIsDark`; customer join branding object carries both (camelCase `logoBackground`, `logoIsDark`).

- [ ] **Step 1: Write failing test** — add to `backend/RestaurantApi.Tests/Branding/BrandingApiTests.cs` (follow the file's existing auth/seed helpers; mirror an existing PUT round-trip test for the request/DTO shape):

```csharp
[Fact]
public async Task Put_roundtrips_logo_background_and_is_dark()
{
    var client = await AuthedClientForOwnerAsync(); // existing helper in this file
    var body = new
    {
        enabled = true,
        primary = "#FF4A1C", secondary = "#D9F54A", background = "#F4EEE2",
        presetKey = (string?)null,
        logoBackground = "transparent",
        logoIsDark = true,
    };
    var put = await client.PutAsJsonAsync(BrandingUrl, body); // BrandingUrl = existing route const
    put.StatusCode.Should().Be(HttpStatusCode.OK);

    var got = await client.GetFromJsonAsync<JsonElement>(BrandingUrl);
    got.GetProperty("logoBackground").GetString().Should().Be("transparent");
    got.GetProperty("logoIsDark").GetBoolean().Should().BeTrue();
}

[Theory]
[InlineData("red")]
[InlineData("#FFF")]
[InlineData("#12345")]
public async Task Put_rejects_invalid_logo_background(string bad)
{
    var client = await AuthedClientForOwnerAsync();
    var body = new
    {
        enabled = true, primary = "#FF4A1C", secondary = "#D9F54A", background = "#F4EEE2",
        presetKey = (string?)null, logoBackground = bad, logoIsDark = (bool?)null,
    };
    var put = await client.PutAsJsonAsync(BrandingUrl, body);
    put.StatusCode.Should().Be(HttpStatusCode.BadRequest);
}
```

> If the existing helper/const names differ (`AuthedClientForOwnerAsync`, `BrandingUrl`), match whatever the file already uses — read the top of `BrandingApiTests.cs` first and reuse its setup verbatim.

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test backend/RestaurantApi.Tests --filter "FullyQualifiedName~BrandingApiTests"` (Docker running)
Expected: FAIL — properties missing / not persisted.

- [ ] **Step 3: Entity** — in `Restaurant.cs`, next to the existing brand fields:

```csharp
public string? LogoBackground { get; set; }
public bool? LogoIsDark { get; set; }
```

- [ ] **Step 4: Column mapping** — in `AppDbContext.cs` right after line 442 (`brand_preset_key`):

```csharp
            e.Property(r => r.LogoBackground).HasColumnName("logo_background");
            e.Property(r => r.LogoIsDark).HasColumnName("logo_is_dark");
```

- [ ] **Step 5: DTOs**

`BrandingRequests.cs` (`UpdateBrandingRequest`): add

```csharp
    public string? LogoBackground { get; set; }
    public bool? LogoIsDark { get; set; }
```

`BrandingResponse.cs`: add

```csharp
    public string? LogoBackground { get; set; }
    public bool? LogoIsDark { get; set; }
```

- [ ] **Step 6: Controller** — in `BrandingController.cs`:

Add validation inside `Update`, after the existing colour validation block (applies in both enabled/disabled branches — put it just before `r.BrandingEnabled = req.Enabled;`):

```csharp
        if (!string.IsNullOrWhiteSpace(req.LogoBackground)
            && req.LogoBackground != "transparent"
            && !HexColor.IsMatch(req.LogoBackground))
            return BadRequest(new { message = "Fundal logo invalid." });
```

Persist (next to the other `r.Brand* = req.*` assignments):

```csharp
        r.LogoBackground = string.IsNullOrWhiteSpace(req.LogoBackground) ? null : req.LogoBackground;
        r.LogoIsDark = req.LogoIsDark;
```

Extend `ToResponse`:

```csharp
        LogoBackground = r.LogoBackground,
        LogoIsDark = r.LogoIsDark,
```

- [ ] **Step 7: Customer bootstrap projections**

`PartyController.cs` (anonymous object at ~line 136, after `photoUrl`):

```csharp
                logoBackground = restaurant?.LogoBackground,
                logoIsDark = restaurant?.LogoIsDark,
```

`CustomerController.cs` `BrandingInfo` initializer (after `PhotoUrl`):

```csharp
                LogoBackground = restaurant?.LogoBackground,
                LogoIsDark = restaurant?.LogoIsDark,
```

Then add the two properties to the `BrandingInfo` DTO class (find it — likely in `DTO/Responses/`; grep `class BrandingInfo`):

```csharp
    public string? LogoBackground { get; set; }
    public bool? LogoIsDark { get; set; }
```

- [ ] **Step 8: Create the migration**

Run: `dotnet ef migrations add AddRestaurantLogoBackground --project backend/RestaurantApi`
Expected: a migration adding nullable `logo_background text` and `logo_is_dark boolean` to `restaurants`. Open it and confirm both columns are `nullable: true` with no default. (Dev auto-migrates on startup; prod applies explicitly.)

- [ ] **Step 9: Run tests to verify pass**

Run: `dotnet test backend/RestaurantApi.Tests --filter "FullyQualifiedName~BrandingApiTests"`
Expected: PASS. (If other Branding test classes exist, run `~Branding` to catch regressions.)

- [ ] **Step 10: Commit**

```bash
git add backend/RestaurantApi/Models/Restaurant.cs backend/RestaurantApi/Data/AppDbContext.cs \
        backend/RestaurantApi/DTO backend/RestaurantApi/Controllers/BrandingController.cs \
        backend/RestaurantApi/Controllers/PartyController.cs backend/RestaurantApi/Controllers/CustomerController.cs \
        backend/RestaurantApi/Migrations backend/RestaurantApi.Tests/Branding/BrandingApiTests.cs
git commit -m "feat(branding): persist logoBackground + logoIsDark, surface on bootstrap"
```

---

### Task 5: Admin logo-lightness detection util

**Files:**
- Create: `frontend-admin/src/utils/logoLuminance.ts`
- Test: `frontend-admin/src/__tests__/logoLuminance.spec.ts`

**Interfaces:**
- Produces: `averageLuminance(rgba: Uint8ClampedArray): number | null` (pure, alpha-weighted; `null` if fully transparent / empty); `computeLogoIsDark(file: File): Promise<boolean | null>` (DOM canvas wrapper; `null` on failure).

- [ ] **Step 1: Write failing test** — create `frontend-admin/src/__tests__/logoLuminance.spec.ts`:

```ts
import { describe, it, expect } from 'vitest'
import { averageLuminance } from '@/utils/logoLuminance'

// RGBA quads.
function px(r: number, g: number, b: number, a = 255) { return [r, g, b, a] }

describe('averageLuminance', () => {
  it('returns a low value for a dark logo', () => {
    const data = new Uint8ClampedArray([...px(10, 10, 10), ...px(20, 20, 20)])
    expect(averageLuminance(data)!).toBeLessThan(0.3)
  })
  it('returns a high value for a light logo', () => {
    const data = new Uint8ClampedArray([...px(240, 240, 240), ...px(255, 255, 255)])
    expect(averageLuminance(data)!).toBeGreaterThan(0.7)
  })
  it('ignores fully transparent pixels (weights by alpha)', () => {
    // one opaque black + one transparent white => should read as dark.
    const data = new Uint8ClampedArray([...px(0, 0, 0, 255), ...px(255, 255, 255, 0)])
    expect(averageLuminance(data)!).toBeLessThan(0.2)
  })
  it('returns null when everything is transparent', () => {
    const data = new Uint8ClampedArray([...px(255, 255, 255, 0)])
    expect(averageLuminance(data)).toBeNull()
  })
})
```

- [ ] **Step 2: Run to verify failure**

Run: `cd frontend-admin && npx vitest run src/__tests__/logoLuminance.spec.ts`
Expected: FAIL — module not found.

- [ ] **Step 3: Implement** — create `frontend-admin/src/utils/logoLuminance.ts`:

```ts
// Estimate whether an uploaded logo is predominantly dark, to pick a legible
// Auto plate. Pure luminance math is split out so it is unit-testable without a DOM.

function channel(v: number): number {
  const s = v / 255
  return s <= 0.03928 ? s / 12.92 : Math.pow((s + 0.055) / 1.055, 2.4)
}

/** Alpha-weighted mean relative luminance (0..1), or null if nothing opaque. */
export function averageLuminance(rgba: Uint8ClampedArray): number | null {
  let sum = 0
  let weight = 0
  for (let i = 0; i < rgba.length; i += 4) {
    const a = rgba[i + 3] / 255
    if (a === 0) continue
    const lum = 0.2126 * channel(rgba[i]) + 0.7152 * channel(rgba[i + 1]) + 0.0722 * channel(rgba[i + 2])
    sum += lum * a
    weight += a
  }
  return weight === 0 ? null : sum / weight
}

/** Draw the logo to an offscreen canvas and decide if it is dark. null on failure. */
export function computeLogoIsDark(file: File): Promise<boolean | null> {
  return new Promise((resolve) => {
    const url = URL.createObjectURL(file)
    const img = new Image()
    img.onload = () => {
      try {
        const w = Math.min(img.naturalWidth || 64, 64)
        const h = Math.min(img.naturalHeight || 64, 64)
        const canvas = document.createElement('canvas')
        canvas.width = w
        canvas.height = h
        const ctx = canvas.getContext('2d')
        if (!ctx) return resolve(null)
        ctx.drawImage(img, 0, 0, w, h)
        const avg = averageLuminance(ctx.getImageData(0, 0, w, h).data)
        resolve(avg === null ? null : avg < 0.5)
      } catch {
        resolve(null)
      } finally {
        URL.revokeObjectURL(url)
      }
    }
    img.onerror = () => { URL.revokeObjectURL(url); resolve(null) }
    img.src = url
  })
}
```

- [ ] **Step 4: Run to verify pass**

Run: `cd frontend-admin && npx vitest run src/__tests__/logoLuminance.spec.ts`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add frontend-admin/src/utils/logoLuminance.ts frontend-admin/src/__tests__/logoLuminance.spec.ts
git commit -m "feat(branding): admin logo-lightness detection util"
```

---

### Task 6: Admin — draft/store/API plumbing + the "Fundal logo" control

**Files:**
- Modify: `frontend-admin/src/components/branding/presets.ts` (`BrandDraft`)
- Modify: `frontend-admin/src/stores/useBrandingStore.ts` (save/disable payload)
- Modify: `frontend-admin/src/services/api-client.ts` (`BrandingUpdatePayload` type — grep for it near `brandingApi`)
- Modify: `frontend-admin/src/views/Settings/BrandTab.vue` (draft init + save + compute `logoIsDark` on upload)
- Modify: `frontend-admin/src/components/branding/BrandControls.vue` (new control)

**Interfaces:**
- Consumes: `computeLogoIsDark` (Task 5); `RestaurantBranding.logoBackground/logoIsDark` (Task 3).
- Produces: `BrandDraft` gains `logoBackground: string | null`, `logoIsDark: boolean | null`; save sends both.

- [ ] **Step 1: Extend `BrandDraft`** in `presets.ts`:

```ts
export interface BrandDraft {
  enabled: boolean
  primary: string
  secondary: string
  background: string
  presetKey: string | null
  logoUrl: string | null
  photoUrl: string | null
  logoBackground: string | null
  logoIsDark: boolean | null
}
```

- [ ] **Step 2: Extend the store** in `useBrandingStore.ts` — widen the `save` param and `disable`:

```ts
  async function save(draft: {
    enabled: boolean
    primary: string | null
    secondary: string | null
    background: string | null
    presetKey: string | null
    logoBackground: string | null
    logoIsDark: boolean | null
  }) {
    branding.value = await brandingApi.update(draft)
    return branding.value
  }
```

In `disable()`, add to the object it passes to `save`:

```ts
      logoBackground: b?.logoBackground ?? null,
      logoIsDark: b?.logoIsDark ?? null,
```

- [ ] **Step 3: Extend the API payload type** in `api-client.ts` — find `BrandingUpdatePayload` (used by `brandingApi.update`) and add:

```ts
  logoBackground: string | null;
  logoIsDark: boolean | null;
```

- [ ] **Step 4: `BrandTab.vue`** — `defaultDraft()` add `logoBackground: null, logoIsDark: null`; the `onMounted` load mapping add `logoBackground: b.logoBackground ?? null, logoIsDark: b.logoIsDark ?? null`; `onSave`'s `store.save({...})` add `logoBackground: draft.value.logoBackground, logoIsDark: draft.value.logoIsDark`; and compute lightness on logo upload:

```ts
import { computeLogoIsDark } from '@/utils/logoLuminance'
// ...
async function onLogo(file: File) {
  try {
    const url = await store.uploadLogo(file)
    const isDark = await computeLogoIsDark(file)
    draft.value = { ...draft.value, logoUrl: url, logoIsDark: isDark }
    toast.success('Logo încărcat.')
  } catch (e) {
    toast.error(e instanceof Error ? e.message : 'Eroare la încărcarea logo-ului.')
  }
}
```

- [ ] **Step 5: Add the control to `BrandControls.vue`** — extend the emit for the new field and add a segmented control + custom color input inside the Logo `upload-field` (below the dropzone). Script additions:

```ts
type LogoBgMode = 'auto' | 'white' | 'transparent' | 'custom'

const logoBgMode = computed<LogoBgMode>(() => {
  const v = props.modelValue.logoBackground
  if (v === null) return 'auto'
  if (v === 'transparent') return 'transparent'
  if (v.toUpperCase() === '#FFFFFF') return 'white'
  return 'custom'
})

function setLogoBgMode(mode: LogoBgMode) {
  const map: Record<LogoBgMode, string | null> = {
    auto: null, white: '#FFFFFF', transparent: 'transparent',
    custom: props.modelValue.logoBackground && props.modelValue.logoBackground.startsWith('#')
      ? props.modelValue.logoBackground : '#FFFFFF',
  }
  emitDraft({ logoBackground: map[mode] })
}

function onLogoBgColor(event: Event) {
  emitDraft({ logoBackground: (event.target as HTMLInputElement).value })
}
```

Template (add after the logo dropzone, inside its `.upload-field`):

```vue
          <div class="logo-bg-row">
            <span class="field-label">Fundal logo</span>
            <div class="logo-bg-modes" role="group" aria-label="Fundal logo">
              <button
                v-for="m in (['auto','white','transparent','custom'] as const)"
                :key="m"
                type="button"
                class="logo-bg-chip"
                :class="{ 'is-active': logoBgMode === m }"
                @click="setLogoBgMode(m)"
              >{{ { auto: 'Auto', white: 'Alb', transparent: 'Transparent', custom: 'Personalizat' }[m] }}</button>
            </div>
            <input
              v-if="logoBgMode === 'custom'"
              type="color"
              class="color-field-input"
              :value="modelValue.logoBackground?.startsWith('#') ? modelValue.logoBackground : '#FFFFFF'"
              @input="onLogoBgColor"
            />
          </div>
```

Add `computed` to the existing `import { computed, ref } from 'vue'` if not already imported (it is). Add minimal scoped styles:

```css
.logo-bg-row { margin-top: 10px; display: flex; flex-direction: column; gap: 6px; }
.logo-bg-modes { display: flex; gap: 6px; flex-wrap: wrap; }
.logo-bg-chip {
  font-size: 12px; font-weight: 600; padding: 5px 10px; border-radius: 999px;
  border: 1px solid var(--ink12); background: var(--card); color: var(--ink70); cursor: pointer;
}
.logo-bg-chip.is-active { border-color: var(--accent); color: var(--accent); }
```

Also add `logoBackground?: string | null` to the emit's `BrandDraft` (already covered since `emitDraft` spreads `Partial<BrandDraft>`).

- [ ] **Step 6: Type-check**

Run: `cd frontend-admin && npm run type-check`
Expected: no errors (all `BrandDraft`/`RestaurantBranding` construction sites now include the new fields).

- [ ] **Step 7: Run the admin test suite (guard against regressions)**

Run: `cd frontend-admin && npm test`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add frontend-admin/src/components/branding/presets.ts frontend-admin/src/stores/useBrandingStore.ts \
        frontend-admin/src/services/api-client.ts frontend-admin/src/views/Settings/BrandTab.vue \
        frontend-admin/src/components/branding/BrandControls.vue
git commit -m "feat(branding): admin logo-background control + logoIsDark on upload"
```

---

### Task 7: Admin — apply the resolver to the preview and sidebar logo

**Files:**
- Modify: `frontend-admin/src/components/branding/BrandPreview.vue` (mini-phone plate)
- Modify: `frontend-admin/src/components/branding/BrandControls.vue` (pass `logoBackground`/`logoIsDark` into `<BrandPreview>`)
- Modify: `frontend-admin/src/App.vue` (sidebar tenant logo)

**Interfaces:**
- Consumes: `resolveLogoBackground` (Task 2); draft `logoBackground`/`logoIsDark` (Task 6); `branding.branding.logoBackground/logoIsDark` from the store (Task 3/6).

- [ ] **Step 1: `BrandPreview.vue`** — accept the two props and drive the plate:

```ts
const props = defineProps<{
  colors: BrandInput
  logoUrl?: string | null
  photoUrl?: string | null
  logoBackground?: string | null
  logoIsDark?: boolean | null
}>()
import { resolveLogoBackground } from '@shared/branding/derive'
const plate = computed(() => resolveLogoBackground(props.logoBackground ?? null, props.logoIsDark ?? null))
```

Bind the plate element (replace the hardcoded background on `.preview-phone-plate`):

```vue
        <div
          class="preview-phone-plate"
          :class="{ 'is-bare': !plate.plate }"
          :style="{ background: plate.plate ? plate.background : 'transparent' }"
        >
```

In `<style scoped>`, remove the hardcoded `background: rgba(255,255,255,0.94);` from `.preview-phone-plate` (keep radius/padding) and add:

```css
.preview-phone-plate.is-bare { padding: 0; backdrop-filter: none; min-width: 0; min-height: 0; }
```

- [ ] **Step 2: `BrandControls.vue`** — pass the fields through to the preview:

```vue
    <BrandPreview
      class="brand-controls-preview"
      :colors="previewColors"
      :logo-url="modelValue.logoUrl"
      :photo-url="modelValue.photoUrl"
      :logo-background="modelValue.logoBackground"
      :logo-is-dark="modelValue.logoIsDark"
    />
```

- [ ] **Step 3: `App.vue` sidebar** — import the resolver and wrap the tenant logo. Add to script:

```ts
import { resolveLogoBackground } from '@shared/branding/derive'
const logoPlate = computed(() =>
  resolveLogoBackground(branding.branding?.logoBackground ?? null, branding.branding?.logoIsDark ?? null),
)
```

Replace the brand-block `<img>` (lines ~288-293) with a plated wrapper:

```vue
          <div
            class="brand-logo-plate"
            :style="logoPlate.plate ? { background: logoPlate.background } : undefined"
          >
            <img :src="resolveMediaUrl(branding.branding.logoUrl)" alt="" style="height: 72px; object-fit: contain" />
          </div>
          <VibeDineLockup />
```

Add scoped style (or inline) for the plate:

```css
.brand-logo-plate { display: inline-flex; border-radius: 16px; margin-bottom: 8px; }
.brand-logo-plate[style] { padding: 10px 14px; }
```

(When `plate` is false there is no `:style`, so the `[style]` padding rule doesn't apply — the logo sits bare on the sidebar.)

- [ ] **Step 4: Type-check**

Run: `cd frontend-admin && npm run type-check`
Expected: no errors.

- [ ] **Step 5: Manual verify (dev server)** — `cd frontend-admin && npm run dev`, open Settings → Brand: switching **Auto / Alb / Transparent / Personalizat** updates the live preview plate; uploading a light vs dark logo under **Auto** flips between transparent and white plate. Confirm the sidebar logo reflects the saved choice after Save.

- [ ] **Step 6: Commit**

```bash
git add frontend-admin/src/components/branding/BrandPreview.vue \
        frontend-admin/src/components/branding/BrandControls.vue frontend-admin/src/App.vue
git commit -m "feat(branding): apply logo-background resolver in admin preview + sidebar"
```

---

### Task 8: Customer — close the on-lime gap + apply resolver on the welcome screen

**Files:**
- Modify: `frontend-customer/src/design/tokens.ts` (`ThemeTokens` + `T`/`TD` add `onLime`)
- Modify: `frontend-customer/src/composables/useTheme.ts` (`applyBrand` overlays `onLime`)
- Modify: lime-surface text sites (list below) to use `T.onLime` instead of hardcoded `#000`/`T.ink`
- Modify: `frontend-customer/src/components/screens/WelcomeView.vue` (plate via resolver + no-plate mode)

**Interfaces:**
- Consumes: `resolveLogoBackground` (Task 2); `deriveBrandPalette().onLime` (Task 1); `session.branding.logoBackground/logoIsDark` (already on `RestaurantBranding`).

- [ ] **Step 1: Add `onLime` to the customer token type + both token sets** in `design/tokens.ts`:

```ts
export type ThemeTokens = {
  // ...existing...
  onAccent: string
  onLime: string
}
```

In `T` (light): `onLime: '#15140F',`  ·  In `TD` (dark): `onLime: '#1B1814',`
(These defaults match today's black-on-lime look for the classic scheme; they only change when branded.)

- [ ] **Step 2: Overlay `onLime` when branded** in `useTheme.ts` `applyBrand`, add to the overlaid object (after `onAccent: p.onAccent,`):

```ts
    onLime: p.onLime,
```

- [ ] **Step 3: Repoint lime-surface text to `onLime`.** In each site below, the element has `background: <lime>`; set its text `color` to the branded on-lime. Where the file aliases `const { tokens: T } = useTheme()`, use `T.onLime`; where it references the static import `T` from tokens (only `OrderingView.vue` uses `T.lime` with the reactive alias — confirm each file's `T` is the `useTheme()` alias, which the grep showed it is):

  - `components/screens/CartView.vue:140` — `color: T.ink` → `color: T.onLime`
  - `components/screens/MenuView.vue:257` (`.recs-pill`) — `color: T.ink` → `color: T.onLime`
  - `components/screens/MergedView.vue:27` — `color: T.ink` → `color: T.onLime`
  - `components/screens/OrderSentView.vue:108` — `color: '#000'` → `color: T.onLime`
  - `components/screens/FlashDealView.vue:142` — `color: '#000'` → `color: T.onLime`
  - `components/screens/FlashDealView.vue:161` — `color: '#000'` → `color: T.onLime`
  - `components/screens/FlashDealView.vue:182` — `color: T.ink` → `color: T.onLime`
  - `view/OrderingView.vue:412` — `color: '#000'` → `color: T.onLime`
  - `components/HappyHourBanner.vue:50` — `color: T.ink` → `color: T.onLime` (line 54 is lime *text on ink* — leave it)
  - `components/FlashDealIcon.vue:79` — `color: '#000'` → `color: T.onLime` (confirm its `T` is the `useTheme()` alias; if it imports static `T`, alias `onLime` won't exist — switch it to `useTheme()` first or use `deriveBrandPalette` value. Read the file's script before editing.)
  - `components/CallWaiterButton.vue:44` — `color: T.value.ink` → `color: T.value.onLime`
  - `components/PartyPanel.vue:42` — background is lime OR accent; if a paired text color exists on that element, set it to `isYou(m) ? T.onAccent : T.onLime`. Read the element first; only change if it renders text on that background.

> For any site whose `T` is the **static** import (not the `useTheme()` alias), either (a) switch that component to `const { tokens: T } = useTheme()`, or (b) leave the hardcoded value. Do not reference `T.onLime` on a static `T` — it exists on the type now, so it will type-check, but the static `T`/`TD` constants carry the unbranded value. Prefer (a) for correctness.

- [ ] **Step 4: `WelcomeView.vue` plate via resolver.** Script:

```ts
import { resolveLogoBackground } from '@shared/branding/derive'
const logoPlate = computed(() =>
  resolveLogoBackground(session.branding?.logoBackground ?? null, session.branding?.logoIsDark ?? null),
)
```

Template — bind the plate and toggle a bare class:

```vue
        <div
          v-if="logoUrl"
          class="logo-plate"
          :class="{ 'logo-plate--bare': !logoPlate.plate }"
          :style="logoPlate.plate ? { background: logoPlate.background } : undefined"
        >
          <img :src="logoUrl" :alt="session.restaurantName || ''" class="logo-img" />
        </div>
```

Style — remove the hardcoded `background: rgba(255, 255, 255, 0.95);` from `.logo-plate` (keep radius/padding/shadow), and add:

```css
.logo-plate--bare {
  background: transparent !important;
  padding: 0;
  box-shadow: none;
  backdrop-filter: none;
}
```

- [ ] **Step 5: Type-check + lint**

Run: `cd frontend-customer && npm run type-check && npm run lint`
Expected: no errors.

- [ ] **Step 6: Manual verify (dev server).** With a seeded branded restaurant (or after configuring one in admin), open `http://localhost:3000/<qrToken>`:
  - Welcome screen logo plate reflects Auto/White/Transparent/Custom.
  - Set a **dark** secondary colour in admin, reload customer: flash-deal / cart lime chips keep legible text (now white via `onLime`) instead of unreadable black.

- [ ] **Step 7: Commit**

```bash
git add frontend-customer/src/design/tokens.ts frontend-customer/src/composables/useTheme.ts \
        frontend-customer/src/components frontend-customer/src/view/OrderingView.vue
git commit -m "feat(branding): auto-contrast lime text + configurable logo plate on customer"
```

---

## Self-Review

**Spec coverage:**
- Contrast-maximising text + AA fallback → Task 1. ✓
- Close customer on-surface token gap (`onLime`) → Task 8 Steps 1-3. ✓ (Grep confirmed only `lime`/`onLime` is consumed as a branded text surface on the customer side; `accentSoft`/`accentBorder`/`gold` are not, so they are correctly out of scope.)
- Persist `logoBackground` + `logoIsDark`, bootstrap exposure → Task 4. ✓ (both PartyController + CustomerController projections + `BrandingInfo` DTO).
- Shared resolver → Task 2; applied in admin (Task 7) + customer (Task 8). ✓
- Author control `Auto/Alb/Transparent/Personalizat` → Task 6. ✓
- Graceful fallback for pre-existing data (`null`/`null` → white plate) → resolver default (Task 2) + nullable columns (Task 4). ✓
- `logoIsDark` computed client-side at upload → Task 5 (util) + Task 6 Step 4 (wired). ✓
- Welcome scrim text unchanged → Task 8 leaves lines 96/57-58 alone. ✓

**Placeholder scan:** No TBD/TODO. Two guarded ambiguities are explicit read-first instructions (BrandingApiTests helper names in Task 4 Step 1; `BrandingInfo` DTO location in Task 4 Step 7; static-vs-alias `T` in Task 8 Step 3) — each says exactly what to check and what to do in either branch.

**Type consistency:** `resolveLogoBackground(logoBackground, logoIsDark)` positional signature is identical in Tasks 2, 7, 8. `readableText(surface, dark, light)` identical in Task 1. `computeLogoIsDark(file): Promise<boolean|null>` / `averageLuminance(rgba): number|null` identical in Tasks 5, 6. `BrandDraft` new fields (`logoBackground: string|null`, `logoIsDark: boolean|null`) match `RestaurantBranding` (Task 3) and the store/API payload (Task 6). Backend `LogoBackground`/`LogoIsDark` (C# `string?`/`bool?`) map to camelCase `logoBackground`/`logoIsDark` on the wire (Task 4).
