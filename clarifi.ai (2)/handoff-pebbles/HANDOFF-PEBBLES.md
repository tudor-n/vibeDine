# Clarifi · Accessible Pebble Navigation — Claude Code Handoff

## What changed & what to implement

Two updates to the **accessible pebble category screen** (`PebblesScreenA11y` / `PebblesScreenA11yDark`):

---

## 1. Always-on accent outline + text

Previously the pebble stroke and label colour were conditional on selection state. Now they are **always accent-coloured**, regardless of interaction state.

### SVG stroke — always accent
```jsx
// Before (conditional)
<path d={path} fill="none"
  stroke={isActive ? T.accent : 'rgba(21,20,15,0.14)'}
  strokeWidth={isActive ? 2 : 0.8} />

// After (always accent, just thicker when active)
<path d={path} fill="none"
  stroke={T.accent}
  strokeWidth={isActive ? 2.5 : 1.6} />
```

For dark mode, replace `T.accent` with `TD.accent` (`#E8255A`).

### Label colour — always accent
```jsx
// Before
color: isActive ? T.accent : T.ink

// After
color: T.accent   // always
```

---

## 2. Tap pebble → navigate directly to menu

Tapping a pebble now:
1. Briefly scales the pebble up (320ms spring animation via existing CSS transform)
2. Then immediately renders the `MenuScreen` in place — no intermediate state, no bottom bar

### State shape
```jsx
const [active, setActive] = React.useState(null);    // drives scale animation
const [navigated, setNavigated] = React.useState(null); // drives screen swap
```

### Handler
```jsx
const handlePebbleClick = (pb) => {
  setActive(pb.id);                          // trigger scale-up
  setTimeout(() => setNavigated(pb), 320);   // after spring settles → navigate
};
```

### Conditional render
```jsx
if (navigated) {
  return (
    <div style={{ width: '100%', height: '100%', position: 'relative' }}>
      <MenuScreen />
      {/* Back button floated over the menu's own header back button */}
      <button
        onClick={() => { setNavigated(null); setActive(null); }}
        style={{
          position: 'absolute',
          top: STATUS_CLEAR + 10,   // 58 + 10 = 68px from top
          left: 20,
          width: 48, height: 48,
          borderRadius: '50%',
          background: T.card,
          border: `1px solid ${T.ink12}`,
          display: 'grid', placeItems: 'center',
          fontSize: 20, cursor: 'pointer',
          zIndex: 20,
          boxShadow: '0 4px 12px rgba(21,20,15,0.12)',
        }}
      >←</button>
    </div>
  );
}
// else render the pebble grid as normal
```

### In a real router (React Navigation / Next.js)
Replace the `navigated` state + conditional render with a standard push navigation:
```jsx
const handlePebbleClick = (pb) => {
  setActive(pb.id);
  setTimeout(() => {
    navigation.push('Menu', { categoryId: pb.id, categoryLabel: pb.label });
  }, 320);
};
```
The 320ms delay preserves the tactile scale-up animation before the screen transition fires. You can reduce or remove it if your navigator's transition already provides enough feedback.

---

## 3. Bottom CTA bar — removed

The slide-up "Deschide: [category]" bar has been removed entirely. The pebble tap is the sole interaction trigger. No replacement needed.

---

## Pebble data shape (for reference)
```ts
type PebbleCategory = {
  id: string          // e.g. 'principale'
  label: string       // display name, e.g. 'Feluri principale'
  count: number       // dish count shown below label
  path: number        // index 0–5 into BLOB_PATHS array
  img: string         // photo URL (preserveAspectRatio xMidYMid slice)
}
```

---

## Files in this package
```
handoff-pebbles/
├── HANDOFF-PEBBLES.md        ← this document
├── screens-a11y.jsx          ← updated warm light accessible screens
└── screens-a11y-dark.jsx     ← updated dark accessible screens
```
