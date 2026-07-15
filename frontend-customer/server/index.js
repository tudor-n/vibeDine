// Page server for the customer SPA.
//
// Why this exists instead of `serve -s dist`: the app is a static bundle, so on a
// first-ever scan the phone knows nothing about the restaurant and paints the
// default palette until POST /join answers — a visible theme flash on a real
// device. A static file server can't fix that; the colours have to travel WITH the
// HTML. So for a /<qrToken> request this fetches that table's colours and inlines
// them as window.__BRAND__, which the app reads before its first paint.
//
// Rules this file lives by:
//  1. The page must ALWAYS render. Branding is an enhancement — every failure path
//     (timeout, API down, bad token, garbage payload) falls through to the plain
//     HTML, where the client's localStorage cache and /join still apply the brand.
//  2. TTFB must not hang on the API. Hence a hard timeout well under human
//     perception, plus a short-lived in-memory cache so a busy table doesn't
//     re-ask per page load.

import express from 'express'
import compression from 'compression'
import { readFileSync } from 'node:fs'
import { join, dirname } from 'node:path'
import { fileURLToPath } from 'node:url'

const __dirname = dirname(fileURLToPath(import.meta.url))
const DIST = join(__dirname, '..', 'dist')
const PORT = Number(process.env.PORT || 3000)

// Server-side base URL for the API. Separate from the build-time VITE_API_URL the
// browser uses: on Railway this should be the internal URL, which is faster and
// doesn't leave the private network. Falls back to the public one.
const API_URL = (process.env.BRANDING_API_URL || process.env.VITE_API_URL || '').replace(/\/$/, '')

// Budget for the branding lookup. The HTML waits on this, so it's sized to stay
// under the ~100ms a person can notice: a slow API costs the injection (and the
// client falls back), never the page.
const BRANDING_TIMEOUT_MS = Number(process.env.BRANDING_TIMEOUT_MS || 400)
const BRANDING_TTL_MS = Number(process.env.BRANDING_TTL_MS || 60_000)
// Hard cap on cache entries. The key is the qrToken straight off a public URL, so
// it is attacker-chosen: without a bound, requests for made-up tokens would grow
// the Map forever. Sized well past any real restaurant's table count, so ordinary
// traffic never evicts; a flood of junk tokens just churns the cache (costing
// extra API lookups) instead of consuming the container's memory.
const BRANDING_CACHE_MAX = Number(process.env.BRANDING_CACHE_MAX || 500)

const UUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
// HAND-SYNCED with isHex() in shared/branding/cache.ts. The client re-validates
// window.__BRAND__ anyway; this is the server-side half of the same guard, and it
// is what makes the injected payload safe to serialize into a <script>.
const HEX_RE = /^#[0-9a-fA-F]{3}(?:[0-9a-fA-F]{3})?$/

const indexHtml = readFileSync(join(DIST, 'index.html'), 'utf8')

// token -> { brand: {primary,secondary,background}|null, at: epochMs }.
// Bounded LRU: a Map iterates in insertion order, so re-inserting on read makes the
// FIRST key the least-recently-used one, which is what eviction drops.
//
// A cached `null` is meaningful — it records "this token has no brand" (unbranded
// restaurant, or unknown table) and is worth keeping: without it, every page load
// for an unbranded tenant would pay a fresh API lookup inside the TTFB budget.
// `undefined` from cacheGet means "not cached", which is a different thing.
const brandCache = new Map()

function cacheGet(token) {
  const hit = brandCache.get(token)
  if (!hit) return undefined
  if (Date.now() - hit.at >= BRANDING_TTL_MS) {
    brandCache.delete(token)
    return undefined
  }
  brandCache.delete(token)
  brandCache.set(token, hit) // touch: move to the most-recently-used end
  return hit.brand
}

function cacheSet(token, brand) {
  brandCache.delete(token)
  brandCache.set(token, { brand, at: Date.now() })
  while (brandCache.size > BRANDING_CACHE_MAX) {
    brandCache.delete(brandCache.keys().next().value) // oldest first
  }
}

function isHex(v) {
  return typeof v === 'string' && HEX_RE.test(v)
}

async function fetchBrand(token) {
  const cached = cacheGet(token)
  if (cached !== undefined) return cached

  let brand = null
  if (API_URL) {
    const ac = new AbortController()
    const timer = setTimeout(() => ac.abort(), BRANDING_TIMEOUT_MS)
    try {
      const res = await fetch(`${API_URL}/api/tables/${token}/branding`, { signal: ac.signal })
      if (res.ok) {
        const b = await res.json()
        if (b?.enabled && isHex(b.primary) && isHex(b.secondary) && isHex(b.background)) {
          brand = { primary: b.primary, secondary: b.secondary, background: b.background }
        }
      }
    } catch {
      // Timeout / network / bad JSON — serve the page unbranded and let the client
      // recover. Deliberately not cached, so a blip doesn't stick for the full TTL.
      clearTimeout(timer)
      return null
    }
    clearTimeout(timer)
  }

  cacheSet(token, brand)
  return brand
}

function renderWithBrand(brand) {
  if (!brand) return indexHtml
  // Every value is already hex-validated, so this can only ever be 3 short colour
  // strings; escaping `<` keeps a `</script>` from terminating the block early even
  // if that guard is ever loosened.
  const json = JSON.stringify(brand).replace(/</g, '\\u003c')
  return indexHtml.replace('</head>', `<script>window.__BRAND__=${json}</script></head>`)
}

const app = express()
app.disable('x-powered-by')
app.use(compression())

// Hashed filenames — safe to cache hard. index:false so every navigation falls
// through to the SPA handler below and gets the branding injection.
app.use(express.static(DIST, { index: false, maxAge: '1y', immutable: true }))

app.get('/health', (_req, res) => res.type('text').send('ok'))

// SPA fallback: any path serves index.html (the route is /:qrToken?). The braces
// make the wildcard match ZERO or more segments — bare `/` must render the app
// (which shows its own "missing table" screen), not a server 404, which is what a
// plain `/*splat` would give it.
app.get('/{*splat}', async (req, res) => {
  const token = req.path.split('/')[1] || ''
  let brand = null
  if (UUID_RE.test(token)) {
    try {
      brand = await fetchBrand(token)
    } catch {
      brand = null // never let branding take the page down
    }
  }
  // The HTML is per-table and brand changes should land quickly; the bundle it
  // points at is content-hashed, so skipping the HTML cache costs one small
  // request and avoids serving one table's colours to another.
  res.set('Cache-Control', 'no-store')
  res.type('html').send(renderWithBrand(brand))
})

app.listen(PORT, '0.0.0.0', () => {
  console.log(`customer page server on :${PORT} (api=${API_URL || 'unset'})`)
})
