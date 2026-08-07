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

// True when THIS browsing context is the demo: the URL says so (fresh load,
// before any join) or the stored session does (later calls that only know the
// restaurant-scoped endpoint, e.g. menu/happy-hour fetches).
export function isDemoSession(): boolean {
  const urlToken = window.location.pathname.split('/')[1] || null
  return isDemoToken(urlToken) || isDemoToken(sessionStorage.getItem('qrToken'))
}
