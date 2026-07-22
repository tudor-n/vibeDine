import { deriveBrandPalette, hexToRgb, type BrandInput, type BrandPalette } from '@shared/branding/derive'
import { readCachedBrand } from '@shared/branding/cache'

// Order matters only for readability; keys are the authoritative list used by
// both applyBrand (write) and clearBrand (remove) so they stay symmetric.
function paletteToVarMap(p: BrandPalette): Record<string, string> {
  const { r, g, b } = hexToRgb(p.ink)
  const bg = hexToRgb(p.paper)
  return {
    '--paper': p.paper,
    '--paperHi': p.paperHi,
    '--card': p.card,
    '--ink': p.ink,
    '--ink-rgb': `${r}, ${g}, ${b}`,
    '--paper-rgb': `${bg.r}, ${bg.g}, ${bg.b}`,
    '--ink70': p.ink70,
    '--ink40': p.ink40,
    '--ink12': p.ink12,
    '--ink06': p.ink06,
    '--accent': p.accent,
    '--accentDk': p.accentDk,
    '--accentSoft': p.accentSoft,
    '--accentBorder': p.accentBorder,
    '--onAccent': p.onAccent,
    '--lime': p.lime,
    '--onLime': p.onLime,
    '--gold': p.gold,
  }
}

const VAR_KEYS = [
  '--paper', '--paperHi', '--card', '--ink', '--ink-rgb', '--paper-rgb',
  '--ink70', '--ink40', '--ink12', '--ink06',
  '--accent', '--accentDk', '--accentSoft', '--accentBorder', '--onAccent',
  '--lime', '--onLime', '--gold',
]

/**
 * Paint the last-known palette for `scope` before Vue mounts, so a branded tenant
 * never flashes the default palette while `GET .../branding` is in flight. Called
 * from main.ts; App.vue's refreshBranding() then revalidates and corrects it.
 * A cache miss is a no-op — the :root defaults in style.css stay as they are.
 */
export function hydrateBrandFromCache(scope: string | null | undefined): void {
  const cached = readCachedBrand(scope)
  if (!cached) return
  useBrandTheme().applyBrand(cached)
}

export function useBrandTheme() {
  function brandVarMap(colors: BrandInput): Record<string, string> {
    return paletteToVarMap(deriveBrandPalette(colors))
  }

  function applyBrand(colors: BrandInput): void {
    if (typeof document === 'undefined') return
    const map = brandVarMap(colors)
    const style = document.documentElement.style
    for (const [k, v] of Object.entries(map)) style.setProperty(k, v)
  }

  function clearBrand(): void {
    if (typeof document === 'undefined') return
    const style = document.documentElement.style
    for (const k of VAR_KEYS) style.removeProperty(k)
  }

  return { brandVarMap, applyBrand, clearBrand }
}
