import { deriveBrandPalette, hexToRgb, type BrandInput, type BrandPalette } from '@shared/branding/derive'

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
