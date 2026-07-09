// Deterministic 3-colour -> full semantic palette derivation for white-label
// branding. Pure functions, shared by both frontends and unit-tested for WCAG AA.

export interface BrandInput {
  primary: string
  secondary: string
  background: string
}

export interface BrandPalette {
  mode: 'light' | 'dark'
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
  accentSoft: string
  accentBorder: string
  onAccent: string
  lime: string
  onLime: string
  gold: string
}

const LIGHT_INK = '#15140F'
const DARK_INK = '#F5F0E8'
const WHITE = '#FFFFFF'
const DARK_MODE_LUMINANCE = 0.35 // background below this is treated as a dark theme

export function hexToRgb(hex: string): { r: number; g: number; b: number } {
  const h = hex.replace('#', '')
  const n = h.length === 3 ? h.split('').map((c) => c + c).join('') : h
  return {
    r: parseInt(n.slice(0, 2), 16),
    g: parseInt(n.slice(2, 4), 16),
    b: parseInt(n.slice(4, 6), 16),
  }
}

function rgbToHex(r: number, g: number, b: number): string {
  const c = (v: number) => Math.max(0, Math.min(255, Math.round(v))).toString(16).padStart(2, '0')
  return `#${c(r)}${c(g)}${c(b)}`.toUpperCase()
}

function rgbToHsl(r: number, g: number, b: number): { h: number; s: number; l: number } {
  r /= 255; g /= 255; b /= 255
  const max = Math.max(r, g, b), min = Math.min(r, g, b)
  let h = 0, s = 0
  const l = (max + min) / 2
  if (max !== min) {
    const d = max - min
    s = l > 0.5 ? d / (2 - max - min) : d / (max + min)
    switch (max) {
      case r: h = (g - b) / d + (g < b ? 6 : 0); break
      case g: h = (b - r) / d + 2; break
      default: h = (r - g) / d + 4
    }
    h /= 6
  }
  return { h, s, l }
}

function hslToRgb(h: number, s: number, l: number): { r: number; g: number; b: number } {
  if (s === 0) return { r: l * 255, g: l * 255, b: l * 255 }
  const hue2rgb = (p: number, q: number, t: number) => {
    if (t < 0) t += 1
    if (t > 1) t -= 1
    if (t < 1 / 6) return p + (q - p) * 6 * t
    if (t < 1 / 2) return q
    if (t < 2 / 3) return p + (q - p) * (2 / 3 - t) * 6
    return p
  }
  const q = l < 0.5 ? l * (1 + s) : l + s - l * s
  const p = 2 * l - q
  return {
    r: hue2rgb(p, q, h + 1 / 3) * 255,
    g: hue2rgb(p, q, h) * 255,
    b: hue2rgb(p, q, h - 1 / 3) * 255,
  }
}

/** amount is 0..1 of lightness to add/subtract. */
export function lighten(hex: string, amount: number): string {
  const { r, g, b } = hexToRgb(hex)
  const { h, s, l } = rgbToHsl(r, g, b)
  const out = hslToRgb(h, s, Math.min(1, l + amount))
  return rgbToHex(out.r, out.g, out.b)
}

export function darken(hex: string, amount: number): string {
  const { r, g, b } = hexToRgb(hex)
  const { h, s, l } = rgbToHsl(r, g, b)
  const out = hslToRgb(h, s, Math.max(0, l - amount))
  return rgbToHex(out.r, out.g, out.b)
}

export function withAlpha(hex: string, alpha: number): string {
  const { r, g, b } = hexToRgb(hex)
  return `rgba(${r}, ${g}, ${b}, ${alpha})`
}

export function relativeLuminance(hex: string): number {
  const { r, g, b } = hexToRgb(hex)
  const chan = (v: number) => {
    const s = v / 255
    return s <= 0.03928 ? s / 12.92 : Math.pow((s + 0.055) / 1.055, 2.4)
  }
  return 0.2126 * chan(r) + 0.7152 * chan(g) + 0.0722 * chan(b)
}

export function contrastRatio(a: string, b: string): number {
  const la = relativeLuminance(a), lb = relativeLuminance(b)
  const hi = Math.max(la, lb), lo = Math.min(la, lb)
  return (hi + 0.05) / (lo + 0.05)
}

/** White or the given ink — whichever has the higher contrast on `bg`. */
export function bestOnColor(bg: string, ink: string): string {
  return contrastRatio(bg, WHITE) >= contrastRatio(bg, ink) ? WHITE : ink
}

export function deriveBrandPalette(input: BrandInput): BrandPalette {
  const { primary, secondary, background } = input
  const isDark = relativeLuminance(background) < DARK_MODE_LUMINANCE
  const mode: 'light' | 'dark' = isDark ? 'dark' : 'light'
  const ink = isDark ? DARK_INK : LIGHT_INK

  return {
    mode,
    paper: background,
    paperHi: lighten(background, isDark ? 0.06 : 0.04),
    card: isDark ? lighten(background, 0.1) : WHITE,
    ink,
    ink70: withAlpha(ink, 0.62),
    ink40: withAlpha(ink, 0.34),
    ink12: withAlpha(ink, 0.12),
    ink06: withAlpha(ink, 0.05),
    accent: primary,
    accentDk: darken(primary, 0.14),
    accentSoft: withAlpha(primary, 0.1),
    accentBorder: withAlpha(primary, 0.3),
    onAccent: bestOnColor(primary, ink),
    lime: secondary,
    onLime: bestOnColor(secondary, ink),
    gold: secondary,
  }
}
