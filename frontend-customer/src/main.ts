import { createApp } from 'vue'
import { createPinia } from 'pinia'

import App from './App.vue'
import router from './router'
import { hydrateBrandFromCache } from './composables/useTheme'
import { isDemoToken, shouldClearCrossModeStorage } from './demo/config'
import './style.css'

// The single route is /:qrToken, so the table is known from the URL before the
// router (or the join request) runs — enough to repaint a returning diner's brand
// on the first frame. Must happen before mount().
function qrTokenFromUrl(): string | null {
  return window.location.pathname.split('/')[1] || null
}

// Embedding pages can force a clean demo run with ?fresh=1: wipe everything
// the demo persisted (screen, cart, totals, guest identity, brand cache) so
// the next visitor starts from the language screen. Must run before
// hydrateBrandFromCache AND before any store module reads sessionStorage —
// i.e. right here, before the app is created.
const bootToken = qrTokenFromUrl()

// A tab that previously held a session in the OTHER mode (demo vs. real)
// leaves its qrToken/tableId/cart behind in sessionStorage. Left alone, that
// pollutes the new boot in either direction: a demo tab's leftovers hijacking
// a real session, or a real tab's leftovers opening a real WebSocket inside
// the demo. Must run before hydrateBrandFromCache and before any store
// module reads sessionStorage.
if (shouldClearCrossModeStorage(bootToken, sessionStorage.getItem('qrToken'))) {
  sessionStorage.clear()
}

if (
  isDemoToken(bootToken) &&
  new URLSearchParams(window.location.search).get('fresh') === '1'
) {
  sessionStorage.clear()
  try {
    localStorage.removeItem(`party.guest.${bootToken}`)
    localStorage.removeItem(`clarifi.brand.${bootToken}`)
  } catch {
    // private mode — nothing persisted anyway
  }
}

hydrateBrandFromCache(bootToken)

const app = createApp(App)

app.use(createPinia())
app.use(router)

app.mount('#app')
