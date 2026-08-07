// The magic qrToken that switches the whole app into the embeddable demo
// session (see docs/superpowers/specs/2026-08-07-embedded-demo-session-design.md).
// Deliberately a token-like slug (not "/demo") so it isn't guessable on the
// production customer domain. Leaf module: NO store imports — production code
// (api-client, useWebSocket, OrderingView, main.ts) static-imports this file,
// and anything heavier would drag demo machinery into every session's bundle
// path or create circular imports.
export const DEMO_TOKEN = 'demo-vibey-x7k2'

export function isDemoToken(token: string | null | undefined): boolean {
  return token === DEMO_TOKEN
}

// True when THIS browsing context is the demo. The URL is authoritative
// whenever it carries a token: a real table URL must never be hijacked by a
// stale demo qrToken left in sessionStorage from an earlier demo visit in the
// same tab. Only when the URL carries no token at all (later calls that only
// know the restaurant-scoped endpoint, e.g. menu/happy-hour fetches) do we
// fall back to the stored session.
export function isDemoSession(): boolean {
  const urlToken = window.location.pathname.split('/')[1] || null
  if (urlToken) return isDemoToken(urlToken)
  return isDemoToken(sessionStorage.getItem('qrToken'))
}

// True when the previous tab occupant (per stored sessionStorage['qrToken'])
// was in a different mode (demo vs. real) than the page about to boot. Used
// to wipe sessionStorage before anything reads it, so a demo tab's leftovers
// can't pollute a real session and vice versa.
export function shouldClearCrossModeStorage(
  urlToken: string | null,
  storedToken: string | null,
): boolean {
  if (!urlToken || !storedToken) return false
  return isDemoToken(urlToken) !== isDemoToken(storedToken)
}
