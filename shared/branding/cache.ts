// Last-known brand colours, cached per scope so a reload can paint the tenant's
// palette on the FIRST frame instead of flashing the default Clarifi/VibeDine
// theme until the branding API answers. Both frontends only learn their colours
// from the network (admin: GET branding, customer: POST join), and on a deployed
// split-origin setup that round-trip is long enough to see.
//
// The cache is a render hint, never a source of truth: the API call still runs on
// every boot and overwrites whatever was painted. Storing only the 3 input colours
// (not the derived palette) keeps entries tiny and lets derive.ts change freely.

import type { BrandInput } from './derive'

const PREFIX = 'clarifi.brand.'

// Scope keys the entry to one tenant: restaurantId (admin) or qrToken (customer),
// so switching restaurants or scanning another table can't paint the wrong brand.
function keyFor(scope: string): string {
  return `${PREFIX}${scope}`
}

// These values are written straight into CSS custom properties, so anything that
// isn't a plain hex colour is rejected rather than trusted — localStorage is
// attacker-writable from any script that lands on the origin.
function isHex(value: unknown): value is string {
  return typeof value === 'string' && /^#[0-9a-fA-F]{3}(?:[0-9a-fA-F]{3})?$/.test(value)
}

/** Cached colours for `scope`, or null when absent, unparsable, or not 3 valid hexes. */
export function readCachedBrand(scope: string | null | undefined): BrandInput | null {
  if (!scope || typeof localStorage === 'undefined') return null
  try {
    const raw = localStorage.getItem(keyFor(scope))
    if (!raw) return null
    const parsed = JSON.parse(raw) as Partial<BrandInput>
    if (!isHex(parsed.primary) || !isHex(parsed.secondary) || !isHex(parsed.background)) return null
    return { primary: parsed.primary, secondary: parsed.secondary, background: parsed.background }
  } catch {
    return null
  }
}

/**
 * Cache `colors` for `scope`. Passing null removes the entry — that's how an
 * unbranded (or newly un-branded) tenant stops painting a stale palette on the
 * next boot. Storage failures are ignored: private mode must not break the app.
 */
export function writeCachedBrand(scope: string | null | undefined, colors: BrandInput | null): void {
  if (!scope || typeof localStorage === 'undefined') return
  try {
    if (!colors) {
      localStorage.removeItem(keyFor(scope))
      return
    }
    const { primary, secondary, background } = colors
    localStorage.setItem(keyFor(scope), JSON.stringify({ primary, secondary, background }))
  } catch {
    /* ignore — private mode / quota */
  }
}
