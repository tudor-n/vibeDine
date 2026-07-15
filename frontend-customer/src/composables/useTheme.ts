import { ref, computed, watch } from 'vue'
import { T, TD, type ThemeTokens } from '@/design/tokens'
import { deriveBrandPalette, type BrandInput } from '@shared/branding/derive'
import { readCachedBrand } from '@shared/branding/cache'

export type ThemeMode = 'light' | 'dark'

const STORAGE_KEY = 'clarifi.theme'

function readInitial(): ThemeMode {
  if (typeof localStorage === 'undefined') return 'light'
  const stored = localStorage.getItem(STORAGE_KEY)
  if (stored === 'light' || stored === 'dark') return stored
  return 'light'
}

const theme = ref<ThemeMode>(readInitial())
const brandTokens = ref<ThemeTokens | null>(null)
const brandMode = ref<ThemeMode | null>(null)

const tokens = computed<ThemeTokens>(() =>
  brandTokens.value ?? (theme.value === 'dark' ? TD : T),
)
const mode = computed<ThemeMode>(() => brandMode.value ?? theme.value)
const isBranded = computed(() => brandTokens.value !== null)

watch(theme, (v) => {
  try { localStorage.setItem(STORAGE_KEY, v) } catch { /* ignore — private mode / disabled storage */ }
})

function toggle() {
  theme.value = theme.value === 'dark' ? 'light' : 'dark'
}
function setTheme(m: ThemeMode) {
  theme.value = m
}

/** Enable branded tokens from 3 colours, or pass null to fall back to light/dark. */
function applyBrand(colors: BrandInput | null) {
  if (!colors) {
    brandTokens.value = null
    brandMode.value = null
    return
  }
  const p = deriveBrandPalette(colors)
  const base = p.mode === 'dark' ? TD : T
  brandTokens.value = {
    ...base, // keeps sans/display/mono/statusClear/blue
    paper: p.paper,
    paperHi: p.paperHi,
    card: p.card,
    ink: p.ink,
    ink70: p.ink70,
    ink40: p.ink40,
    ink12: p.ink12,
    ink06: p.ink06,
    accent: p.accent,
    accentDk: p.accentDk,
    lime: p.lime,
    onAccent: p.onAccent,
    onLime: p.onLime,
  }
  brandMode.value = p.mode
}

/**
 * Paint the last-known palette for `scope` (the table's qrToken) before the app
 * mounts, so a branded restaurant doesn't flash the default tokens while the join
 * request is in flight — on a deployed split-origin setup that POST is slow enough
 * to see. useSessionStore then applies the authoritative colours when join lands.
 * A cache miss is a no-op, leaving the normal light/dark tokens.
 */
export function hydrateBrandFromCache(scope: string | null | undefined): void {
  const cached = readCachedBrand(scope)
  if (cached) applyBrand(cached)
}

export function useTheme() {
  return { theme, tokens, mode, isBranded, toggle, setTheme, applyBrand }
}
