# Clarifi · Restaurant App — Accessible Design Handoff
**For Claude Code — implement the senior-friendly UI**

---

## Overview

Clarifi is a smart restaurant ordering app. This handoff covers the **accessible variant** of the customer-facing mobile UI — designed for older guests. It exists in two colour modes: **Warm Light** (Ospătăria Curții) and **Dark** (Supporter Sports Pub).

The screens in this package are production-ready React components written as JSX. They use inline styles and React state only — no external UI libraries required.

---

## Design Principles (Accessible Variant)

| Principle | Rule |
|---|---|
| Body text minimum | **18px** |
| Dish names / card titles | **18–22px, weight 700** |
| Page headings | **36–60px** |
| Touch target minimum | **48px** height/width |
| Primary CTA buttons | **56px** height, full-width pill |
| Secondary buttons | **48px** height |
| Quantity steppers | **40px** per side |
| Copy style | Plain full words — no abbreviations, no all-caps labels longer than 2 words |
| Decorative mono labels | Removed or enlarged to ≥14px |
| Line height (body) | **1.45–1.55** |
| Max content width | Match phone width (390px design canvas) |

---

## Colour Tokens

### Warm Light (`T` — Ospătăria Curții)
```js
const T = {
  paper:    '#F4EEE2',   // warm cream background
  paperHi:  '#FBF7EC',   // slightly brighter cream
  card:     '#FFFFFF',   // card surfaces
  ink:      '#15140F',   // primary text
  ink70:    'rgba(21,20,15,0.62)',  // secondary text
  ink40:    'rgba(21,20,15,0.34)',  // tertiary / placeholder
  ink12:    'rgba(21,20,15,0.10)',  // borders
  ink06:    'rgba(21,20,15,0.05)',  // dividers
  accent:   '#FF4A1C',   // terracotta — primary action colour
  accentDk: '#C0340F',   // darker terracotta for hover
  lime:     '#D9F54A',   // acid lime — AI / badge highlight
  blue:     '#2B6BFF',   // informational
}
```

### Dark (`TD` — Supporter Sports Pub)
```js
const TD = {
  paper:    '#1B1814',
  paperHi:  '#221F1A',
  card:     '#2C2923',
  ink:      '#F5F0E8',
  ink70:    'rgba(245,240,232,0.65)',
  ink40:    'rgba(245,240,232,0.38)',
  ink12:    'rgba(245,240,232,0.10)',
  ink06:    'rgba(245,240,232,0.05)',
  accent:   '#E8255A',   // hot pink
  accentDk: '#B81A47',
  lime:     '#FFD166',   // warm gold
  blue:     '#6B9FFF',
}
```

---

## Typography

```js
const fonts = {
  sans:    '"Inter Tight", -apple-system, system-ui, sans-serif',
  display: '"Inter Tight", -apple-system, system-ui, sans-serif',
  mono:    '"JetBrains Mono", ui-monospace, Menlo, monospace',
}
```

Google Fonts import:
```html
<link href="https://fonts.googleapis.com/css2?family=Instrument+Serif:ital@0;1&family=Inter+Tight:wght@300;400;500;600;700&family=JetBrains+Mono:wght@300;400;500&display=swap" rel="stylesheet">
```

---

## Screens

The accessible flow has **6 screens**:

| # | Screen | File component | Description |
|---|---|---|---|
| 1 | Welcome | `WelcomeScreenA11y` / `WelcomeScreenA11yDark` | Landing, restaurant info, chef's pick card, open menu CTA |
| 2 | Pebbles | `PebblesScreenA11y` / `PebblesScreenA11yDark` | Category picker — organic blob photo shapes in a 2-col grid |
| 3 | Menu | `MenuScreenA11y` / `MenuScreenA11yDark` | Dish list with filter chips, large cards, AI recommendations strip |
| 4 | Cart | `CartScreenA11y` / `CartScreenA11yDark` | Order review, quantity steppers, total, send CTA |
| 5 | Flash Deal | `FlashDealScreenA11yDark` / `FlashDealScreenA11y` | Time-limited chef offer with countdown bar |
| 6 | Order Sent | `SentScreenA11y` / `SentScreenA11yDark` | Confirmation, live timeline, receipt summary |

---

## Component Patterns

### Screen wrapper
Every screen is wrapped in a full-bleed container pinned to `390 × 844` (iPhone 15 viewport). It carries a subtle noise texture overlay (SVG feTurbulence) and top padding of **58px** to clear the iOS status bar + Dynamic Island.

```jsx
<div style={{
  width: '100%', height: '100%',
  background: T.paper, color: T.ink,
  fontFamily: fonts.sans,
  position: 'relative', overflow: 'hidden',
  paddingTop: 58,
}}>
  {/* noise overlay — see source */}
  {children}
</div>
```

### Primary button (`ABigBtn`)
Used for all primary CTAs (Open Menu, Send to Kitchen, etc.):
```jsx
<button style={{
  width: '100%',
  padding: '20px 24px',
  border: 'none',
  borderRadius: 999,
  background: T.accent,
  color: '#FFF4EC',
  fontSize: 19,
  fontWeight: 700,
  display: 'flex',
  justifyContent: 'space-between',
  alignItems: 'center',
  boxShadow: `0 10px 24px -8px ${T.accent}66`,
}}>
  <span>Label</span>
  {/* Arrow circle */}
  <span style={{
    width: 38, height: 38, borderRadius: '50%',
    background: '#FFF4EC', color: T.accent,
    display: 'grid', placeItems: 'center', fontSize: 20,
  }}>→</span>
</button>
```

### Pebble navigation (Pebbles screen)
Organic blob shapes clipped with SVG `<clipPath>`. Each pebble uses a real food photo (Unsplash) as fill, with a shading gradient and specular highlight layered on top. Tapping a pebble scales it up (`scale(1.06) translateY(-4px)`) with a spring transition and reveals a bottom CTA bar.

Key SVG technique:
```jsx
<svg viewBox="0 0 100 100" width="158" height="158">
  <defs>
    <clipPath id="clip-id"><path d={blobPath} /></clipPath>
    {/* shading + highlight gradients */}
  </defs>
  <image href={photoUrl} clipPath="url(#clip-id)"
    x="0" y="0" width="100" height="100"
    preserveAspectRatio="xMidYMid slice" />
  {/* shading layer */}
  {/* specular layer */}
  {/* stroke outline — thickens + turns accent on active */}
</svg>
```

Blob path strings (6 shapes) are in `screens-a11y.jsx` as `A_BLOB_PATHS`.

### Dish card (Menu screen)
```
┌──────────────────────────────────┐
│  [100×100 food tile]  Dish name  │  ← 18px bold
│                       Desc text  │  ← 14px ink70, lh 1.5
│  ─────────────────────────────── │
│  24 RON              [+ Adaugă]  │  ← price 24px, btn 48px
└──────────────────────────────────┘
```

### Info grid (Welcome screen)
2-column grid showing table info. Each cell: label in 12px uppercase + value in 17px bold. Background: `T.card`, border-radius 20px, padding 16px.

### Timeline (Order Sent screen)
Flex rows per step. Dot indicator: 20px circle, filled accent when active (with glow shadow), filled ink when past, hollow border when future. Active step gets a coloured badge "ACUM".

### Cart stepper
```
[ − ]  1  [ + ]
```
Each button is 40×40px. Housed in a pill container `(background: T.paper, border-radius: 999)`.

---

## Food tile placeholder
When no photo is available, use `FoodTile` — a gradient blob placeholder:

```jsx
function FoodTile({ tone = 'terra', style = {} }) {
  const palettes = {
    terra: { a: '#FF4A1C', b: '#FFB38A', c: '#FFF4EC' },
    sage:  { a: '#5E7A4C', b: '#B8CFA0', c: '#EEF3E3' },
    gold:  { a: '#E0A528', b: '#FFE29A', c: '#FFF7DE' },
    ink:   { a: '#2A261E', b: '#6A6153', c: '#D8CFBE' },
  };
  const p = palettes[tone];
  return (
    <div style={{ position: 'relative', overflow: 'hidden', borderRadius: 14, background: p.c, ...style }}>
      <div style={{
        position: 'absolute', inset: '-20%',
        background: `radial-gradient(60% 50% at 30% 30%, ${p.a} 0%, transparent 60%),
                     radial-gradient(50% 40% at 75% 75%, ${p.b} 0%, transparent 60%)`,
        filter: 'blur(2px)',
      }} />
    </div>
  );
}
```

---

## Data Shape

### Dish
```ts
type Dish = {
  id: string
  name: string        // Romanian
  desc: string        // ingredients / method
  price: number       // RON
  tone: 'terra' | 'sage' | 'gold' | 'ink' | 'blue' | 'lime'
  tag?: string        // e.g. "Semnătura", "Vegetarian"
}
```

### Cart item
```ts
type CartItem = {
  id: number
  name: string
  qty: number
  price: number       // unit price RON
  tone: string
  note?: string       // e.g. "Sugerat de AI"
}
```

### Pebble category
```ts
type PebbleCategory = {
  id: string
  label: string       // display name
  count: number       // dish count
  path: number        // index into BLOB_PATHS array (0–5)
  img: string         // photo URL
}
```

---

## Implementation Notes for Claude Code

1. **Framework**: The reference implementation uses React 18 + inline styles. You may adapt to your project's stack (Next.js, Expo, React Native, etc.) using the same tokens and layout rules.

2. **React Native adaptation**: Replace `div` → `View`, `span` → `Text`, `button` → `TouchableOpacity` (minHeight 48). The SVG pebbles require `react-native-svg`.

3. **Font loading**: Use `@expo-google-fonts/inter-tight` for RN, or the Google Fonts `<link>` for web.

4. **Noise texture**: The SVG feTurbulence noise overlay is decorative — safe to omit on lower-end devices or simplify to a static PNG.

5. **Photo sources**: Pebble images currently pull from Unsplash. Replace with your own CDN assets using the same `preserveAspectRatio="xMidYMid slice"` behaviour.

6. **Colour mode**: Both `T` (light) and `TD` (dark) token sets are provided. Wire to system `prefers-color-scheme` or an in-app toggle.

7. **Spacing constant**: `STATUS_CLEAR = 58` — top padding to clear iOS status bar + Dynamic Island. On Android set to 40 or use `StatusBar.currentHeight`.

8. **Copy language**: All copy is Romanian. Run through your localisation pipeline; keys map 1:1 to the visible strings in the source files.

9. **Accessibility extras not yet in the prototype** (recommended for production):
   - `accessibilityLabel` on all icon-only buttons (←, ✕, +, −)
   - `accessibilityRole="button"` on touchable elements
   - Minimum colour-contrast ratio 4.5:1 — all token combinations pass for body text
   - `accessibilityLiveRegion="polite"` on the cart total when it updates

---

## Files in this package

```
handoff/
├── HANDOFF.md            ← this document
├── tokens.js             ← design tokens (light + dark) as a JS module
├── screens-a11y.jsx      ← Warm Light accessible screens (React/JSX)
└── screens-a11y-dark.jsx ← Dark accessible screens (React/JSX)
```
