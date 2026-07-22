import { createApp } from 'vue'
import { createPinia } from 'pinia'
import router from './router'
import App from './App.vue'
import { useAuthStore } from './stores/useAuthStore'
import { hydrateBrandFromCache } from './composables/useBrandTheme'


import './style.css'

const app = createApp(App)


app.use(createPinia())

// Repaint the branded palette from cache before the first frame — App.vue only
// learns the real colours from the API, and until that lands the shell would show
// the default palette. Must run after app.use(pinia) (needs the persisted auth to
// know which restaurant) and before mount(). App.vue revalidates right after.
hydrateBrandFromCache(useAuthStore().activeRestaurantId)


app.use(router)


app.mount('#app')