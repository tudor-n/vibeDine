import { createApp } from 'vue'
import { createPinia } from 'pinia'

import App from './App.vue'
import router from './router'
import { hydrateBrandFromCache } from './composables/useTheme'
import './style.css'

// The single route is /:qrToken, so the table is known from the URL before the
// router (or the join request) runs — enough to repaint a returning diner's brand
// on the first frame. Must happen before mount().
function qrTokenFromUrl(): string | null {
  return window.location.pathname.split('/')[1] || null
}

hydrateBrandFromCache(qrTokenFromUrl())

const app = createApp(App)

app.use(createPinia())
app.use(router)

app.mount('#app')
