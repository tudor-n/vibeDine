// Clarifi design tokens — accessible variant.
// Mirrors handoff/tokens.js (Warm Light + Dark) with the same key names.
// All token sets share the same shape so consumers can swap T <-> TD freely.

export type ThemeTokens = {
  paper: string
  paperHi: string
  card: string
  ink: string
  ink70: string
  ink40: string
  ink12: string
  ink06: string
  accent: string
  accentDk: string
  lime: string
  blue: string
  sans: string
  display: string
  mono: string
  statusClear: number
  onAccent: string
  onLime: string
}

export const T: ThemeTokens = {
  paper:    '#F4EEE2',
  paperHi:  '#FBF7EC',
  card:     '#FFFFFF',
  ink:      '#15140F',
  ink70:    'rgba(21,20,15,0.62)',
  ink40:    'rgba(21,20,15,0.34)',
  ink12:    'rgba(21,20,15,0.10)',
  ink06:    'rgba(21,20,15,0.05)',
  accent:   '#FF4A1C',
  accentDk: '#C0340F',
  lime:     '#D9F54A',
  blue:     '#2B6BFF',
  sans:     '"Inter Tight", -apple-system, system-ui, sans-serif',
  display:  '"Inter Tight", -apple-system, system-ui, sans-serif',
  mono:     '"JetBrains Mono", ui-monospace, Menlo, monospace',
  statusClear: 58,
  onAccent: '#FFF4EC',
  onLime:   '#15140F',
}

export const TD: ThemeTokens = {
  paper:    '#1B1814',
  paperHi:  '#221F1A',
  card:     '#2C2923',
  ink:      '#F5F0E8',
  ink70:    'rgba(245,240,232,0.65)',
  ink40:    'rgba(245,240,232,0.38)',
  ink12:    'rgba(245,240,232,0.10)',
  ink06:    'rgba(245,240,232,0.05)',
  accent:   '#E8255A',
  accentDk: '#B81A47',
  lime:     '#FFD166',
  blue:     '#6B9FFF',
  sans:     '"Inter Tight", -apple-system, system-ui, sans-serif',
  display:  '"Inter Tight", -apple-system, system-ui, sans-serif',
  mono:     '"JetBrains Mono", ui-monospace, Menlo, monospace',
  statusClear: 58,
  onAccent: '#1B1814',
  onLime:   '#1B1814',
}

export const A = {
  textXS: 12,
  textSM: 14,
  textMD: 16,
  textLG: 18,
  textXL: 20,
  text2XL: 24,
  text3XL: 32,
  text4XL: 36,
  text5XL: 48,
  text6XL: 60,
  targetSM: 44,
  targetMD: 48,
  targetLG: 56,
  radiusSM: 14,
  radiusMD: 20,
  radiusLG: 22,
  radiusPill: 999,
  pagePad: 24,
  cardPad: 14,
  gap: 12,
  sectionGap: 22,
  lhBody: 1.5,
  lhLoose: 1.55,
  lhTight: 1.2,
} as const

export const BLOB_PATHS: readonly string[] = [
  'M50,5 C68,3 88,16 92,36 C97,58 84,82 64,92 C44,102 18,94 9,74 C0,54 8,26 24,14 C36,5 42,6 50,5 Z',
  'M50,10 C70,6 90,18 94,40 C98,62 86,86 64,93 C42,100 16,90 8,68 C0,46 10,20 28,12 C38,7 44,12 50,10 Z',
  'M50,4 C66,2 84,18 88,40 C92,64 80,88 60,95 C40,102 16,92 8,70 C0,48 10,22 28,10 C38,4 44,5 50,4 Z',
  'M52,6 C72,4 92,20 94,42 C96,66 82,88 60,94 C38,100 12,88 6,66 C0,44 12,18 32,9 C42,4 46,7 52,6 Z',
  'M50,8 C68,5 88,20 90,42 C92,64 78,86 58,93 C38,100 14,90 7,68 C0,46 12,20 30,11 C40,6 44,9 50,8 Z',
  'M50,7 C72,4 92,18 96,40 C100,64 86,88 62,95 C38,102 12,92 5,68 C-2,44 10,18 30,9 C40,4 42,9 50,7 Z',
] as const

export type FoodTone =
  | 'terra' | 'sage' | 'gold' | 'ink' | 'blue' | 'lime' | 'pink'

export const FOOD_PALETTES: Record<FoodTone, { a: string; b: string; c: string }> = {
  terra: { a: '#FF4A1C', b: '#FFB38A', c: '#FFF4EC' },
  sage:  { a: '#5E7A4C', b: '#B8CFA0', c: '#EEF3E3' },
  gold:  { a: '#E0A528', b: '#FFE29A', c: '#FFF7DE' },
  ink:   { a: '#2A261E', b: '#6A6153', c: '#D8CFBE' },
  blue:  { a: '#2B6BFF', b: '#9BB6FF', c: '#E6EDFF' },
  lime:  { a: '#9DC41A', b: '#D9F54A', c: '#F2FBC3' },
  pink:  { a: '#E8255A', b: '#FF7CA0', c: '#3A1520' },
}

// Pick a deterministic food tone from a string (e.g. product id) so the
// gradient placeholder is stable across renders without needing an image asset.
const TONE_ROTATION: FoodTone[] = ['terra', 'gold', 'sage', 'ink', 'blue', 'lime']
export function toneFromKey(key: string): FoodTone {
  let h = 0
  for (let i = 0; i < key.length; i++) h = (h * 31 + key.charCodeAt(i)) >>> 0
  return TONE_ROTATION[h % TONE_ROTATION.length] ?? 'terra'
}
