# Handoff: Clarifi · Restaurant Customer App

## Overview

Clarifi is a table-side ordering app for restaurant guests. Guests scan a QR code at their table, browse a menu categorised by organic "pebble" blobs, add items to a cart, receive AI-driven pairing suggestions, and submit directly to the kitchen. A flash-deal system lets the chef push time-limited offers to specific tables in real time.

The design ships in **two colour directions** — each with a standard and an accessible (large-text, senior-friendly) variant:

| Direction | Description | Accent | Secondary |
|---|---|---|---|
| **Warm** | Nume Restaurant — warm cream ground | Terracotta `#FF4A1C` | Acid lime `#D9F54A` |
| **Dark** | Supporter Sports Pub — charcoal ground | Hot pink `#E8255A` | Warm gold `#FFD166` |

---

## About the Design Files

The files in this bundle are **HTML design prototypes** — high-fidelity references showing intended look, layout, copy, and interaction behaviour. They are **not** production code. The task is to **recreate these designs in your target codebase** (React Native, SwiftUI, native Android, or web React), using its established patterns, navigation libraries, and component system. Lift exact colours, spacing, typography, and motion specs from this document and the reference files — do not copy the HTML/JSX directly.

## Fidelity

**High-fidelity.** These are pixel-complete mockups with final colours, typography, spacing, and all interaction states. Recreate the UI pixel-precisely using the codebase's existing libraries and patterns.

---

## Design Tokens

### Warm Direction (`T`)

```
paper:    #F4EEE2   // warm cream background
paperHi:  #FBF7EC   // slightly lighter cream (cards, recs panel)
card:     #FFFFFF
ink:      #15140F   // near-black with warm tint
ink70:    rgba(21,20,15, 0.62)
ink40:    rgba(21,20,15, 0.34)
ink12:    rgba(21,20,15, 0.10)
ink06:    rgba(21,20,15, 0.05)
accent:   #FF4A1C   // terracotta red
accentDk: #C0340F
lime:     #D9F54A   // acid lime — secondary/pop colour
blue:     #2B6BFF
```

### Dark Direction (`TD`)

```
paper:    #1B1814   // deep charcoal
paperHi:  #221F1A
card:     #2C2923
ink:      #F5F0E8
ink70:    rgba(245,240,232, 0.65)
ink40:    rgba(245,240,232, 0.38)
ink12:    rgba(245,240,232, 0.10)
ink06:    rgba(245,240,232, 0.05)
accent:   #E8255A   // hot pink
accentDk: #B81A47
lime:     #FFD166   // warm gold — secondary/pop colour
blue:     #6B9FFF
```

### Typography

| Role | Family | Weight | Size (mobile) | Notes |
|---|---|---|---|---|
| Body / UI | Inter Tight | 400–700 | 13–19px | Primary sans |
| Display / Hero | Inter Tight | 500–700 | 36–68px | `letter-spacing: -0.04em`, `line-height: 0.9–0.95` |
| Mono / Labels | JetBrains Mono | 300–600 | 9–12px | `letter-spacing: 0.12–0.20em`, `text-transform: uppercase` |

**Accessible variants:** body min 16px, headings min 36px, touch targets min 56px.

### Spacing & Geometry

```
Status bar clearance:  58px top padding (iOS dynamic island)
Screen padding:        20–24px horizontal
Card border-radius:    18–24px
Pill border-radius:    999px (full)
Gap between cards:     10–14px
Bottom CTA padding:    28–32px from bottom edge
```

### Grain Texture

All screens carry a subtle SVG fractalNoise grain overlay:
- Warm: `opacity: 0.35`, `mix-blend-mode: multiply`
- Dark: `opacity: 0.22`, `mix-blend-mode: screen`

Replicate with a noise texture image or a subtle SVG filter on a full-screen overlay `pointerEvents: none`.

### Background Blooms

Radial gradient blurs (`filter: blur(40–60px)`, `opacity: 0.2–0.55`) positioned at screen corners. Always `pointerEvents: none`, `z-index: 0`. Never placed over interactive elements. Colors follow accent/lime per theme.

---

## Screens / Views

### 1. Welcome

**Purpose:** Entry screen after QR scan. Establishes table context, shows chef recommendation.

**Layout:**
- Background blooms: accent top-right, lime mid-left
- Status row (top): two mono `Tag` pills — "Masa 07 · live" (with glowing dot) + date/time
- Hero text block (`padding: 58px 22px 0`): mono eyebrow → 68px display heading → 15px body → session info chips row → dark featured card
- CTA button: full-width pill, accent bg, `padding: 19px 22px`, arrow circle right

**Featured card (chef's pick):**
- Background: `T.ink` / `TD.card`
- Inner accent bloom top-right
- Food placeholder tile left (72×90px, `border-radius: 14px`)
- Mono label "ASTĂZI · CHEF'S PICK" in lime
- 19px dish name + 12px price/detail
- `→` arrow right

**Accessible variant:** Larger text (60px heading, 18px body), bigger info grid (2×2), bigger featured card with `DFoodTile`.

---

### 2. Pebbles (Category Picker)

**Purpose:** Guest picks a menu category by tapping an organic blob shape.

**Layout:**
- Header row: back button (48×48 circle) + "Masa 7" label
- Heading: 36px display + 16px subtitle
- Scroll area (`flex: 1, overflow: auto`): 3 group cards stacked with `gap: 14px`
- Each group card: accent-tinted bg + border, `border-radius: 24px`, `padding: 18px 14px 22px`
- Inside each card: 2-column grid of pebbles
- Sticky bottom bar: "Recomandarea zilei" card (see below)

**Background blooms (inside scroll container):**
- Lime bloom: `top: 10%, left: -20%`, 260×260, `blur: 60px`, `opacity: 0.32`, `z-index: 0`
- Accent bloom: `bottom: 20%, right: -15%`, 220×220, `blur: 55px`, `opacity: 0.28`, `z-index: 0`
- All cards/pebbles at `z-index: 1`

**Pebble shape:**
Each pebble is an SVG blob rendered at 158×158px (accessible) or 148×148px (standard), `viewBox="0 0 100 100"`. Six blob path variants:

```
Blob 0: "M50,5 C68,3 88,16 92,36 C97,58 84,82 64,92 C44,102 18,94 9,74 C0,54 8,26 24,14 C36,5 42,6 50,5 Z"
Blob 1: "M50,10 C70,6 90,18 94,40 C98,62 86,86 64,93 C42,100 16,90 8,68 C0,46 10,20 28,12 C38,7 44,12 50,10 Z"
Blob 2: "M50,4 C66,2 84,18 88,40 C92,64 80,88 60,95 C40,102 16,92 8,70 C0,48 10,22 28,10 C38,4 44,5 50,4 Z"
Blob 3: "M52,6 C72,4 92,20 94,42 C96,66 82,88 60,94 C38,100 12,88 6,66 C0,44 12,18 32,9 C42,4 46,7 52,6 Z"
Blob 4: "M50,8 C68,5 88,20 90,42 C92,64 78,86 58,93 C38,100 14,90 7,68 C0,46 12,20 30,11 C40,6 44,9 50,8 Z"
Blob 5: "M50,7 C72,4 92,18 96,40 C100,64 86,88 62,95 C38,102 12,92 5,68 C-2,44 10,18 30,9 C40,4 42,9 50,7 Z"
```

Each pebble SVG layers (in order):
1. `<clipPath>` using the blob path
2. `<image>` (food photo) clipped to blob
3. Linear gradient shading overlay (top transparent → bottom dark) — darkened when inactive
4. Radial specular highlight (top-left)
5. Contour stroke (outline) — **lime when active, subtle `rgba` when inactive**
6. *(Active only)* Dashed gold ring `stroke="#D9A520"` `strokeDasharray="4 3"`

**Inactive pebble state:**
- `drop-shadow(0 8px 14px rgba(21,20,15,0.22))`
- Contour stroke: `rgba(21,20,15,0.12)` / `rgba(245,240,232,0.12)` (dark), `strokeWidth: 1.0`
- Label: plain text, ink colour, 16px bold

**Active pebble state:**
- `transform: scale(1.06) translateY(-4px)` — spring easing `cubic-bezier(0.34,1.4,0.64,1)`, `duration: 450ms`
- `drop-shadow(0 16px 24px <lime>88)`
- Contour stroke: `T.lime` / `TD.lime`, `strokeWidth: 2.8`
- Dashed gold ring `#D9A520`, `strokeWidth: 1.2`, `strokeDasharray: "4 3"`, `opacity: 0.85`
- Label transforms into a **square chip**: `background: T.lime`, `border-radius: 6px`, `padding: 4px 10px`, `border: 2px solid #D9A520`, `color: #000`, `fontSize: 15px`, `fontWeight: 800`

**🎬 PEBBLE WARP TRANSITION (to implement):**

When a pebble is tapped, the transition into the menu screen should feel like the pebble "warps" the entire screen. Recommended implementation:

1. **On tap:** record the pebble's center position on screen (`getBoundingClientRect` or equivalent).
2. **Phase 1 — Pebble pulse** (0–150ms): scale the pebble to `1.15`, boost the lime drop-shadow intensity.
3. **Phase 2 — Ripple expand** (100–400ms): render a full-screen overlay (same `z-index` as the pebble, above all content). Animate a circle/blob mask from the pebble's center coordinates expanding to cover the full viewport. The mask shape should use the pebble's blob SVG path, scaled from 0 → viewport diagonal. Fill is the lime/gold accent colour.
4. **Phase 3 — Screen swap** (350ms): when the mask covers ~80% of the screen, swap the underlying view to the Menu screen.
5. **Phase 4 — Reveal** (350–600ms): reverse-animate the mask (contract from full → 0) OR dissolve out with `opacity: 0`, revealing the menu beneath.

**Alternative simpler approach (if full blob warp is too complex):**
- Use a `clip-path: circle(0px at <tapX> <tapY>)` → `clip-path: circle(150% at <tapX> <tapY>)` CSS transition on a lime-coloured overlay, `duration: 500ms`, `easing: cubic-bezier(0.4, 0, 0.2, 1)`. Swap the view at 50% progress, then fade the overlay out.

**Easing reference:** `cubic-bezier(0.34, 1.4, 0.64, 1)` (spring-like overshoot, used throughout the pebble interactions).

**Sticky "Recomandarea zilei" bar (bottom of pebble screen):**
- `position: absolute, bottom: 0` with gradient fade above it: `linear-gradient(to bottom, transparent, <paper> 28%)`
- Dark `T.ink` card (warm) / `TD.paperHi` card (dark), `border-radius: 22px`, `padding: 14px 16px`
- Food placeholder tile: 58×68px, `border-radius: 12px`
- Lime "RECOMANDAREA ZILEI" label — `font-size: 11px`, `font-weight: 700`, `letter-spacing: 0.08em`, uppercase, lime colour
- 16px dish name + 13px price/detail
- Arrow circle right: 38×38px, `border-radius: 50%`, accent bg

---

### 3. Menu List

**Purpose:** Scrollable dish list for selected category. AI recommendations at bottom.

**Layout:**
- Header: back button | category name + count | search button
- Filter chips (horizontal scroll): "Toate", "Carne", "Pește", "Veggie", "Chef" — first chip uses `T.ink` bg
- Dish cards: 2-column grid (`96px` food tile | text content), `border-radius: 20px`
- Recommendations panel: `T.paperHi` bg, top border, horizontal scroll of pairing cards (min-width 210px)
- Floating cart bar (absolute bottom): `T.ink` pill, lime item-count badge, accent "Coș →" button

**Dish card structure:**
- Food tile: 96×110px gradient placeholder
- Tag label (if present): mono uppercase, accent colour
- Dish name: 16–18px, `font-weight: 500–700`
- Description: 11.5–14px, `T.ink70`
- Price: 18–24px bold + "RON" suffix in `T.ink70`
- Add button: ink circle (standard) or accent pill (accessible)

**Accessible variant:** Cards taller, text larger (18px body, 24px price), bigger add buttons (48px height pill).

---

### 4. Cart

**Purpose:** Review and confirm order before sending to kitchen.

**Layout:**
- Header: table tag | "Draft #" tag | ✕ close button
- Hero: mono eyebrow + 42px heading "Gata de trimis."
- Cart items: 3-column grid (`60px tile | text | price+stepper`)
- AI suggestion note: lime pill badge "SUGERAT DE AI"
- Summary card: subtotal / service / total rows
- CTA: full-width accent pill "Trimite la bucătărie"

**Stepper control:** `−` / qty / `+` inline row, `background: T.paper`, `border-radius: 999px`, 22×22px tap targets (accessible: 40×40px)

---

### 5. Flash Deal

**Purpose:** Time-limited chef offer pushed to specific tables. High-energy, lime-dominant.

**Layout:**
- Lime blooms dominate (top-right large, bottom-left medium)
- Header: "Ofertă specială" badge (see below) + table/timer tag
- "FLASH DEAL" label with lightning icon
- 48px hero heading with lime highlight on "următoarele" word: `background: T.lime, border-radius: 8px, padding: 0 10px 2px`
- Deal card: 88px food tile | discount badge + name + crossed price
- Countdown card: mono `03:47` display + lime progress bar (`62%`)
- CTA: full-width lime pill "Revendică oferta" + "Nu mulțumesc" text link below

**"Ofertă specială" badge:**
- `background: T.lime` (warm) / `TD.lime` (dark)
- `color: #000`
- Solid black dot (warm) / `#000` dot (dark) — no glow
- `padding: 8px 16px`, `border-radius: 999px`, `font-size: 15px`, `font-weight: 700`

**Deal card discount ribbon:** `position: absolute, top: 10, right: -34`, `transform: rotate(35deg)`, lime bg, "HOT" label.

---

### 6. Order Sent

**Purpose:** Confirmation screen with live kitchen status timeline.

**Layout:**
- Lime bloom top-right, accent bloom bottom-left
- Header: table tag + "Confirmat ✓" badge (see below)
- Hero: 52px heading "Comanda a plecat la bucătărie."
- Timeline card: 3 steps with dot/line, timestamp, label, "ACUM" tag on active step
- Receipt card: `T.ink` bg (warm) / `TD.card` (dark), order number + total
- Bottom: 2-button row — "Chemă chelner" (outline) | "Mai comand →" (filled)

**"Confirmat ✓" badge:**
- `background: T.lime` (warm) / `TD.lime` (dark)
- `color: #000`
- `padding: 8px 14px`, `border-radius: 999px`, `font-size: 16px`, `font-weight: 700`

**Timeline dot states:**
- Done + active: `background: T.accent`, `box-shadow: 0 0 12px T.accent` (glowing)
- Done: `background: T.ink`
- Pending: transparent with `border: 1px solid T.ink40`

---

## Interactions & Behaviour

### General
- All screens scroll vertically (except Welcome and Sent which are fixed-height)
- Floating bottom bars use `position: absolute, bottom: 0` with a gradient fade above

### Pebble selection
- **Tap:** spring-scale to `1.06`, `translateY(-4px)`, `duration: 450ms`, easing `cubic-bezier(0.34,1.4,0.64,1)`
- **Outline:** switches to lime, `strokeWidth 2.8` + dashed gold ring
- **Label:** text → lime square chip with gold border, black text
- **Re-tap selected:** deselects, reverting all styles
- **Navigation:** on tap → warp transition into menu (see Pebble Warp Transition above)

### Bottom bar (pebbles)
- "Recomandarea zilei" bar is always visible at the bottom (fixed/absolute)
- Tapping it navigates to the menu screen with "Feluri principale" pre-selected

### Cart stepper
- `−` decrements qty; at qty=1 it removes the item (show confirmation or animate out)
- `+` increments qty

### Flash deal countdown
- Timer counts down in real time (MM:SS)
- Progress bar width = `(remainingSeconds / totalSeconds) * 100%`
- At 0: CTA disables, heading updates to "Oferta a expirat"

### Accessible variants
- All touch targets minimum 56px height/width
- No stagger on pebble grid (even layout)
- Navigation from pebble: tapping immediately navigates (no "select then CTA" flow — single-tap to menu)

---

## State Management

### Pebble screen
```
activeCategory: string | null     // currently selected pebble id
navigatedCategory: object | null  // triggers navigation to Menu
```

### Menu screen
```
activeFilter: string              // active filter chip ('Toate' default)
cartItems: CartItem[]             // shared cart state
```

### Cart screen
```
cartItems: CartItem[]             // { id, name, qty, price, tone, note? }
```

### Flash deal
```
secondsRemaining: number          // countdown timer
dealClaimed: boolean
```

### Order sent
```
orderStatus: 'sent' | 'preparing' | 'served'
```

---

## Food Tile Placeholders

Until real photography is integrated, use gradient placeholder tiles:

```
terra: bg #FFF4EC, gradients #FF4A1C + #FFB38A
sage:  bg #EEF3E3, gradients #5E7A4C + #B8CFA0
gold:  bg #FFF7DE, gradients #E0A528 + #FFE29A
ink:   bg #D8CFBE, gradients #2A261E + #6A6153
lime:  bg #F2FBC3, gradients #9DC41A + #D9F54A

Dark variants:
pink:  bg #3A1520, gradients #E8255A + #FF7CA0
gold:  bg #2E2510, gradients #FFD166 + #FFE9A3
sage:  bg #16271E, gradients #5E9A7A + #9ECFB4
ink:   bg #111B30, gradients #6B9FFF + #A3C0FF
terra: bg #2E1510, gradients #E85A25 + #FF9D77
lime:  bg #1D2206, gradients #C4D41A + #E6F54A
```

All tiles use two radial gradients (`60% 50% at 30% 30%` and `50% 40% at 75% 75%`) with `filter: blur(2px)`, plus a mono label at `left: 10, top: 10`.

---

## Assets

- **Food photography:** Unsplash photos used as placeholders (clipped to blob shapes). Replace with real food photography per dish.
- **Icons:** No icon library used. All icons are Unicode characters (`←`, `→`, `✕`, `⌕`, `⚡`, `✓`) or geometric CSS shapes.
- **Fonts:** Inter Tight + JetBrains Mono — both available on Google Fonts.

---

## Files in this Bundle

| File | Contents |
|---|---|
| `Clarifi Restaurant.html` | Main entry point — design canvas showing all screens |
| `screens.jsx` | Warm direction — all 6 screens + design tokens (`T`) + shared atoms |
| `screens-dark.jsx` | Dark direction — all 6 screens + dark tokens (`TD`) |
| `screens-a11y.jsx` | Accessible warm — all 6 screens, larger text/targets |
| `screens-a11y-dark.jsx` | Accessible dark — all 6 screens, larger text/targets |
| `pebbles-screen.jsx` | Standard (non-accessible) pebble screen with staggered layout |

The accessible variants (`screens-a11y.*`) are the **primary build targets** — they are the most complete, most iterated, and most appropriate for a production app targeting a broad age range.

---

## Priority Implementation Order

1. **Design tokens** — set up colour + typography system
2. **Pebbles screen** — the most novel/complex component; validate blob shapes and active states first
3. **Pebble warp transition** — implement the clip-path or mask animation before wiring navigation
4. **Menu screen** — standard list with pairing recommendations
5. **Welcome + Cart + Order Sent** — mostly layout work once tokens are in place
6. **Flash Deal** — can be a modal/sheet over the menu, triggered by server push
