// WCAG relative-luminance contrast helper, ported from the Vibey design
// prototype's `theme()` method (see design_handoff_vibey_recommendations/
// Cart AI Recommendations.dc.html, `hx`/`lum`/onSec). Pure — no DOM, no
// tokens — so any tenant secondary colour (white-label) gets a legible
// foreground without hard-coding per-palette overrides.

const LIGHT_FG = '#FFF4EC'
const DARK_FG = '#15140F'

function hexToRgb(hex: string): [number, number, number] {
  let h = (hex || '').replace('#', '')
  if (h.length === 3) h = h.split('').map((c) => c + c).join('')
  return [parseInt(h.slice(0, 2), 16), parseInt(h.slice(2, 4), 16), parseInt(h.slice(4, 6), 16)]
}

function channelLuminance(v: number): number {
  const s = v / 255
  return s <= 0.03928 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4
}

/** WCAG relative luminance (0..1) of a `#rgb`/`#rrggbb` colour. */
export function relativeLuminance(hex: string): number {
  const [r, g, b] = hexToRgb(hex)
  return 0.2126 * channelLuminance(r) + 0.7152 * channelLuminance(g) + 0.0722 * channelLuminance(b)
}

/**
 * Best-contrast foreground for a given background colour: light `#FFF4EC`
 * on dark/mid backgrounds, dark `#15140F` on light backgrounds. Mirrors the
 * prototype's onSec derivation (`lum < 0.55 ? light : dark`) so it stays
 * correct for any tenant secondary colour.
 */
export function bestOn(hex: string): string {
  return relativeLuminance(hex) < 0.55 ? LIGHT_FG : DARK_FG
}
