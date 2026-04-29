// Clarifi Design Tokens
// Use these in your implementation. Both light and dark sets are exported.

// ─── Warm Light (Ospătăria Curții) ────────────────────────────
export const T = {
  // Surfaces
  paper:    '#F4EEE2',
  paperHi:  '#FBF7EC',
  card:     '#FFFFFF',

  // Text
  ink:      '#15140F',
  ink70:    'rgba(21,20,15,0.62)',
  ink40:    'rgba(21,20,15,0.34)',
  ink12:    'rgba(21,20,15,0.10)',
  ink06:    'rgba(21,20,15,0.05)',

  // Accents
  accent:   '#FF4A1C',   // terracotta — primary CTA
  accentDk: '#C0340F',   // hover / pressed state
  lime:     '#D9F54A',   // AI badge, highlight pop
  blue:     '#2B6BFF',   // informational

  // Typography
  sans:     '"Inter Tight", -apple-system, system-ui, sans-serif',
  display:  '"Inter Tight", -apple-system, system-ui, sans-serif',
  mono:     '"JetBrains Mono", ui-monospace, Menlo, monospace',

  // Layout
  statusClear: 58,       // top padding to clear iOS status bar + Dynamic Island
};

// ─── Dark (Supporter Sports Pub) ──────────────────────────────
export const TD = {
  // Surfaces
  paper:    '#1B1814',
  paperHi:  '#221F1A',
  card:     '#2C2923',

  // Text
  ink:      '#F5F0E8',
  ink70:    'rgba(245,240,232,0.65)',
  ink40:    'rgba(245,240,232,0.38)',
  ink12:    'rgba(245,240,232,0.10)',
  ink06:    'rgba(245,240,232,0.05)',

  // Accents
  accent:   '#E8255A',   // hot pink — primary CTA
  accentDk: '#B81A47',
  lime:     '#FFD166',   // warm gold — AI badge, highlight pop
  blue:     '#6B9FFF',

  // Typography (same stacks)
  sans:     '"Inter Tight", -apple-system, system-ui, sans-serif',
  display:  '"Inter Tight", -apple-system, system-ui, sans-serif',
  mono:     '"JetBrains Mono", ui-monospace, Menlo, monospace',

  // Layout
  statusClear: 58,
};

// ─── Accessible sizing scale ───────────────────────────────────
export const A = {
  // Text sizes
  textXS:   12,   // labels, metadata
  textSM:   14,   // secondary body, descriptions
  textMD:   16,   // filter chips, secondary labels
  textLG:   18,   // primary body copy — minimum for readable content
  textXL:   20,   // dish names, card titles
  text2XL:  24,   // prices, totals
  text3XL:  32,   // large totals
  text4XL:  36,   // section headings
  text5XL:  48,   // hero headings
  text6XL:  60,   // welcome screen hero

  // Touch targets
  targetSM:  44,  // minimum (icon buttons, chips)
  targetMD:  48,  // standard (back buttons, steppers)
  targetLG:  56,  // primary CTA buttons

  // Border radius
  radiusSM:  14,
  radiusMD:  20,
  radiusLG:  22,
  radiusPill: 999,

  // Spacing
  pagePad:  24,   // horizontal page padding
  cardPad:  14,   // card internal padding
  gap:      12,   // standard gap between cards
  sectionGap: 22, // gap between major sections

  // Line heights
  lhBody:   1.5,
  lhLoose:  1.55,
  lhTight:  1.2,
};

// ─── Pebble blob paths (6 organic shapes) ─────────────────────
// Use with SVG <clipPath> to mask food photos into organic blobs.
// Each path fits a 100×100 viewBox.
export const BLOB_PATHS = [
  "M50,5 C68,3 88,16 92,36 C97,58 84,82 64,92 C44,102 18,94 9,74 C0,54 8,26 24,14 C36,5 42,6 50,5 Z",
  "M50,10 C70,6 90,18 94,40 C98,62 86,86 64,93 C42,100 16,90 8,68 C0,46 10,20 28,12 C38,7 44,12 50,10 Z",
  "M50,4 C66,2 84,18 88,40 C92,64 80,88 60,95 C40,102 16,92 8,70 C0,48 10,22 28,10 C38,4 44,5 50,4 Z",
  "M52,6 C72,4 92,20 94,42 C96,66 82,88 60,94 C38,100 12,88 6,66 C0,44 12,18 32,9 C42,4 46,7 52,6 Z",
  "M50,8 C68,5 88,20 90,42 C92,64 78,86 58,93 C38,100 14,90 7,68 C0,46 12,20 30,11 C40,6 44,9 50,8 Z",
  "M50,7 C72,4 92,18 96,40 C100,64 86,88 62,95 C38,102 12,92 5,68 C-2,44 10,18 30,9 C40,4 42,9 50,7 Z",
];

// ─── Food tile colour palettes ─────────────────────────────────
// Used for gradient placeholder tiles when no photo is available.
export const FOOD_PALETTES = {
  terra: { a: '#FF4A1C', b: '#FFB38A', c: '#FFF4EC' },
  sage:  { a: '#5E7A4C', b: '#B8CFA0', c: '#EEF3E3' },
  gold:  { a: '#E0A528', b: '#FFE29A', c: '#FFF7DE' },
  ink:   { a: '#2A261E', b: '#6A6153', c: '#D8CFBE' },
  blue:  { a: '#2B6BFF', b: '#9BB6FF', c: '#E6EDFF' },
  lime:  { a: '#9DC41A', b: '#D9F54A', c: '#F2FBC3' },
  // Dark variants
  pink:  { a: '#E8255A', b: '#FF7CA0', c: '#3A1520' },
};
