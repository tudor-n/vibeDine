import { ref, watch } from 'vue'

export type ThemeMode = 'light' | 'dark'

const STORAGE_KEY = 'clarifi.admin.theme'

function readInitial(): ThemeMode {
  if (typeof localStorage === 'undefined') return 'light'
  const stored = localStorage.getItem(STORAGE_KEY)
  if (stored === 'light' || stored === 'dark') return stored
  return 'light'
}

const theme = ref<ThemeMode>(readInitial())

function applyToDocument(v: ThemeMode) {
  if (typeof document === 'undefined') return
  document.documentElement.setAttribute('data-theme', v)
}

applyToDocument(theme.value)

watch(theme, (v) => {
  applyToDocument(v)
  try {
    localStorage.setItem(STORAGE_KEY, v)
  } catch {
    // ignore — private mode / disabled storage
  }
})

function toggle() {
  theme.value = theme.value === 'dark' ? 'light' : 'dark'
}

function setTheme(mode: ThemeMode) {
  theme.value = mode
}

export function useTheme() {
  return { theme, toggle, setTheme }
}
