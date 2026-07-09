# Handoff: Happy Hour Offer Popup

## Overview
A time-boxed "Happy Hour" discount offer for a restaurant table-ordering phone app. On the menu screen, a modal popup presents a single discounted item (a Negroni at −30%) valid for a fixed window (2:00–3:00 PM) with a live "ends in" countdown. If the guest dismisses it, a persistent **return banner** appears at the top of the menu so the offer can be reopened any time during the window. The whole screen supports **light and dark mode** via a toggle in the header.

## About the Design Files
The file in this bundle (`Happy Hour Popup.dc.html`) is a **design reference created in HTML** — a working prototype showing the intended look and behavior. It is **not production code to copy directly**. The task is to **recreate this design in the target codebase's existing environment** (React, SwiftUI, native Android, etc.), using that codebase's established components, theming, and patterns. If no environment exists yet, choose the most appropriate framework for the project and implement it there.

The prototype is built as a self-contained HTML component with inline styles and a small state class. CSS custom properties drive theming. Treat the values below as the source of truth.

## Fidelity
**High-fidelity (hifi).** Final colors, typography, spacing, and interactions are specified. Recreate the UI pixel-accurately using the codebase's own libraries. The device frame (rounded 446×884 rectangle) is only a preview shell — implement against the real app's screen, not the bezel.

---

## Screens / Views

There is one screen — the **Menu screen** — with three overlay states: (A) popup open, (B) popup dismissed → return banner visible, (C) "added" confirmation toast. Both A/B render in light or dark theme.

### Menu screen (backdrop)
- **Purpose**: The guest at Table 3 browses drink categories. This is the existing screen; the offer is layered on top.
- **Layout**: Full-screen column, `padding: 54px 22px 0` (top inset accounts for status bar). Vertical flow:
  1. **Return banner** (only when popup dismissed — see below), `margin-bottom: 16px`.
  2. **Header row** — space-between: left = "Call the waiter" pill; right = icon group (bookmark + theme toggle), `gap: 11px`.
  3. **Table row** (`margin-top: 26px`) — space-between: back-arrow circle (left) + "Table 3" label (right).
  4. **Heading** (`margin-top: 26px`): "What would you like to eat?" — 46px/800, `letter-spacing: -1.5px`, `line-height: 1.03`. "to eat?" is accent pink `#ec3a63`.
  5. **Subtitle** (`margin-top: 14px`): "Tap a category to see the menu" — 17px/500, dim text.
  6. **Category card** (`margin-top: 22px`): rounded 26px container, `padding: 26px 22px`, 2-col grid `gap: 18px 26px`. Each cell = circular thumbnail (`aspect-ratio: 1`, `radial-gradient(circle at 38% 30%, #8a5a63, #3a2025)`), name (17px/700), "N dishes" (14px, dim). Cells: "Gin — 2 dishes", "Rum — 4 dishes".

**Header components (exact):**
- **Call the waiter pill**: bg `#e51d52`, white text 15px/700, `padding: 11px 17px 11px 14px`, `border-radius: 30px`, bell icon (stroke white). Static in both themes.
- **Bookmark button**: 46×46 circle, bg `#f1c84b`, bookmark icon stroke `#1b1916`. Static in both themes.
- **Theme toggle button**: 46×46 circle, bg `var(--circle)`, cursor pointer. Shows a **sun** icon in dark mode (tap → light) and a **moon** icon in light mode (tap → dark). Icon stroke `var(--icon)`.
- **Back-arrow button**: 50×50 circle, bg `var(--circle)`, arrow-left icon stroke `var(--icon)`, 2.4 stroke width.

### Return banner (state B — popup dismissed)
- **Purpose**: Non-intrusive persistent entry point to reopen the offer. Pinned at the very top of the menu flow (under the status bar, above the header) so it never overlaps scrolling menu content.
- **Layout**: full-width row, `display: flex; align-items: center; gap: 12px`, `padding: 12px 14px`, `border-radius: 16px`, `margin-bottom: 16px`. bg `var(--accent-soft)`, `1px solid var(--border)`, text `var(--text)`. No glow/animation.
- **Left icon tile**: 34×34, `border-radius: 10px`, bg `#f1c84b`, contains a **smiley-face** icon (stroke `#231405`, 2px).
- **Text block**: line 1 = "Your Happy Hour offer" (14.5px/700, `letter-spacing: -0.2px`); line 2 = "Negroni −30% · ends 3:00 PM · tap to view" (12px/500, `var(--text-dim)`). The "−30%" reflects the discount value.
- **Right**: chevron-right icon, stroke `var(--text-dim)`, 2.4 width.
- **Whole row is tappable** → reopens popup.

### Offer popup (state A)
- **Purpose**: Present the single discounted item and let the guest add it or defer.
- **Scrim**: covers the screen, bg `var(--scrim)`, `backdrop-filter: blur(2px)`, fades in (`hhScrim` 0.35s ease).
- **Card**: centered vertically (`top: 50%; transform: translateY(-50%)`), `left/right: 18px`. bg `var(--surface)`, `border-radius: 28px`, `padding: 26px 24px 22px`, `1px solid var(--border)`, `box-shadow: 0 30px 70px rgba(0,0,0,.4)`. Enters with `hhPop` (0.45s `cubic-bezier(.2,.8,.3,1)`): fade + `translateY(20px)→0` + `scale(.96)→1`.
- **Close button**: absolute top-right (`top/right: 18px`), 32×32 circle, bg `var(--circle)`, ✕ icon stroke `var(--text-dim)` 2.6 width. → dismiss.
- **Eyebrow row** (`gap: 9px`):
  - **HAPPY HOUR chip**: bg `#f1c84b`, text `#231405` 12px/800, `letter-spacing: 1.2px`, `padding: 7px 12px 7px 9px`, `border-radius: 30px`, leading **smiley** icon (stroke `#231405`).
  - **Time**: clock icon + "2:00 – 3:00 PM", 13px/600, `var(--text-dim)`.
- **Hero row** (`margin-top: 20px`, `gap: 18px`):
  - **Drink image**: 104×104 circle, `2px solid #f1c84b`. In the prototype this is an empty image placeholder (guest/product photo dropped in). Use a real product image in implementation.
  - **Text**: overline "FROM THE GIN BAR" (12px/700, `letter-spacing: 1.2px`, uppercase, color `#e08a2e`); title "Negroni" (26px/800, `letter-spacing: -0.6px`, `var(--text)`); description "Classic gin, Campari & sweet vermouth, orange twist." (13.5px/1.35, `var(--text-dim)`).
- **Price + countdown row** (`margin-top: 22px`, `padding-top: 18px`, `border-top: 1px solid var(--border)`, space-between):
  - Left (baseline, `gap: 10px`): new price (29px/800, `letter-spacing: -1px`, `var(--text)`); old price (16px/600, `line-through`, `var(--text-dim)`); **−30% tag** (bg `#e51d52`, white 12px/800, `padding: 3px 8px`, `border-radius: 20px`).
  - Right (right-aligned): "ENDS IN" label (10.5px/700, `letter-spacing: 0.5px`, uppercase, `var(--text-dim)`); countdown MM:SS (19px/800, `font-variant-numeric: tabular-nums`, color `#e08a2e`).
- **Primary CTA** (`margin-top: 18px`): full width, bg `#e51d52` (hover `#f02659`), white 16.5px/800, `padding: 16px`, `border-radius: 16px`, cart icon + "Add for $9.80". → shows toast.
- **Secondary**: full-width text button "Maybe later" (14px/600, `var(--text-dim)`, transparent). → dismiss.

### Confirmation toast (state C)
- Appears after "Add for…". Absolute, `left/right: 18px; bottom: 26px`. bg `#1f6b3a`, text `#eafff1` 15px/700, `border-radius: 16px`, `padding: 15px 18px`, check icon (stroke `#8ff0b4`). Copy: "Negroni added at happy-hour price". Auto-dismisses after ~2.6s. Enters with `hhPop` 0.35s. Same in both themes (fixed success color).

---

## Interactions & Behavior
- **On load**: popup open, dark theme.
- **Dismiss** (✕ or "Maybe later"): popup + scrim hide; return banner appears at top of menu.
- **Reopen** (tap return banner): popup + scrim show again; banner hides.
- **Add** (primary CTA): success toast appears for ~2.6s, then auto-hides. (In production, also add the item to the cart / order.)
- **Theme toggle** (header sun/moon): flips light ⇄ dark instantly across the whole screen. Note the toggle sits on the menu backdrop, so it's reachable when the popup is dismissed; if you want it usable while the popup is open, surface an equivalent control above the scrim.
- **Countdown**: MM:SS ticking down every 1s from the offer's remaining time; floors at 00:00. Prototype seeds it at 14:52 — in production derive it from the real offer end time (`endsAt - now`).
- **Animations**: `hhScrim` fade 0.35s ease (scrim); `hhPop` 0.45s `cubic-bezier(.2,.8,.3,1)` (popup), 0.35s (toast).

## State Management
- `open: boolean` — popup visibility (default `true`). Dismiss → `false`; reopen → `true`.
- `light: boolean` — theme (default `false` = dark). Toggle flips it.
- `added: boolean` — toast visibility; set `true` on add, auto-reset after 2600ms (clear any pending timer first).
- `secs: number` — remaining seconds for countdown; decremented each second, clamped at 0. In production, compute from the offer's end timestamp rather than a hardcoded seed.
- **Derived**: `newPrice = basePrice * (1 - discountPercent/100)`; `discountLabel = "-" + round(discountPercent) + "%"`; `countdown = MM:SS` from `secs`; `ctaLabel = "Add for " + newPrice`.
- **Config inputs** (props in the prototype): `basePrice` (default 14), `discountPercent` (default 30). In production these come from the offer/menu-item data.

## Design Tokens

### Theme variables (CSS custom properties)
| Token | Dark | Light | Used for |
|---|---|---|---|
| `--bg` | `#1b1916` | `#f7f2ea` | screen background |
| `--surface` | `#221f1c` | `#ffffff` | popup card |
| `--circle` | `#2c2a27` | `#efe8df` | neutral icon circles / close btn |
| `--accent-soft` | `#3a1d23` | `#f6e7df` | return banner + category card bg |
| `--text` | `#f5f3f0` | `#241a17` | primary text |
| `--text-dim` | `#948d87` | `#8a817a` | secondary text / dim icons |
| `--icon` | `#d8d4cf` | `#4a423d` | neutral icon strokes |
| `--border` | `rgba(255,255,255,.08)` | `rgba(0,0,0,.08)` | borders / dividers |
| `--scrim` | `rgba(7,6,5,.72)` | `rgba(28,18,12,.42)` | modal scrim |

### Fixed brand colors (both themes)
- Primary accent (pink): `#e51d52`; hover `#f02659`; heading highlight `#ec3a63`.
- Happy-hour yellow: `#f1c84b` (chip / icon tile / bookmark / image ring).
- Amber accent (overline, countdown, banner smiley): `#e08a2e`.
- Success (toast): bg `#1f6b3a`, text `#eafff1`, icon `#8ff0b4`.
- Category thumbnail gradient: `radial-gradient(circle at 38% 30%, #8a5a63, #3a2025)`.

### Typography
- Family: **Archivo** (Google Fonts), weights 400–900. Substitute the codebase's display/sans stack if Archivo isn't available.
- Scale (px / weight): 46/800 heading; 29/800 price; 26/800 popup title; 19/800 countdown; 17/500–700 subtitle & category name; 16.5/800 CTA; 15/700 waiter pill & toast; 14.5/700 banner title; 14/600 secondary btn; 13.5 description; 13/600 time; 12/800 chip & −30% tag; 12/500 banner subtext; 10.5/700 "ends in" label.

### Radius
- Buttons/CTA 16px · popup card 28px · category card 26px · pills/chips 30px · banner 16px · icon tile 10px · small tags 20px · circles 50%.

### Spacing
- Screen padding `54px 22px 0`; section rhythm `margin-top: 26px / 22px / 20px / 18px / 14px`; common gaps 9–18px; card paddings 26px.

### Shadows / effects
- Popup card `0 30px 70px rgba(0,0,0,.4)`; toast `0 16px 40px rgba(0,0,0,.35)`; scrim `backdrop-filter: blur(2px)`.

## Assets
- **Icons**: all inline SVG (stroke-based, ~24px viewBox) — bell, bookmark, sun, moon, arrow-left, chevron-right, clock, smiley-face, shopping-cart, check, ✕. Recreate with the codebase's icon set (e.g. Lucide/Feather equivalents) — the smiley is the "smile" icon.
- **Fonts**: Archivo (Google Fonts).
- **Drink image**: the prototype uses an empty circular image placeholder — supply the real product photo in implementation.
- No raster brand assets are bundled.

## Files
- `Happy Hour Popup.dc.html` — the full design reference (menu backdrop, return banner, popup, toast, theming, countdown, and state logic). Open in a browser to see all states; toggle theme with the header sun/moon; dismiss with "Maybe later" to see the return banner.
