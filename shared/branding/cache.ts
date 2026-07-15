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

// Marks a scope as known-to-have-no-brand. Without this, "unbranded restaurant"
// and "never looked it up" both read as an absent entry — and the customer app has
// to tell them apart: the first can render immediately (the default palette IS
// correct), the second must not render a palette at all until it knows.
const UNBRANDED = { unbranded: true } as const

export interface CachedBrandEntry {
  /** Have we ever resolved this scope's branding? False = never looked, or junk. */
  known: boolean
  /** The colours, or null when known-unbranded (or not known at all). */
  colors: BrandInput | null
}

/**
 * What we know about `scope`'s branding, distinguishing "no brand" from "no idea".
 * Anything unparsable is reported as unknown rather than trusted or guessed at.
 */
export function readCachedBrandEntry(scope: string | null | undefined): CachedBrandEntry {
  if (!scope || typeof localStorage === 'undefined') return { known: false, colors: null }
  try {
    const raw = localStorage.getItem(keyFor(scope))
    if (!raw) return { known: false, colors: null }
    const parsed = JSON.parse(raw) as Partial<BrandInput> & { unbranded?: boolean }
    if (parsed?.unbranded === true) return { known: true, colors: null }
    if (!isHex(parsed?.primary) || !isHex(parsed?.secondary) || !isHex(parsed?.background)) {
      return { known: false, colors: null }
    }
    return {
      known: true,
      colors: { primary: parsed.primary, secondary: parsed.secondary, background: parsed.background },
    }
  } catch {
    return { known: false, colors: null }
  }
}

/** Cached colours for `scope`, or null when absent, unparsable, or not 3 valid hexes. */
export function readCachedBrand(scope: string | null | undefined): BrandInput | null {
  return readCachedBrandEntry(scope).colors
}

/**
 * Cache what we now know about `scope`. Passing null records known-unbranded rather
 * than deleting, so a newly un-branded tenant stops painting a stale palette AND
 * still counts as resolved (no needless loading screen on its next boot).
 *
 * Storage failures are swallowed: private mode must not break the app. The caller
 * cannot assume the write stuck — readCachedBrandEntry will simply report `unknown`
 * next time, which every caller already handles.
 */
export function writeCachedBrand(scope: string | null | undefined, colors: BrandInput | null): void {
  if (!scope || typeof localStorage === 'undefined') return
  try {
    const entry = colors
      ? { primary: colors.primary, secondary: colors.secondary, background: colors.background }
      : UNBRANDED
    localStorage.setItem(keyFor(scope), JSON.stringify(entry))
  } catch {
    /* ignore — private mode / quota */
  }
}
