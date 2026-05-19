<script setup lang="ts">
import { computed } from 'vue'
import { RouterLink, RouterView, useRoute, useRouter } from 'vue-router'
import { useTheme } from '@/composables/useTheme'
import ToastContainer from '@/components/ToastContainer.vue'
import { useAuthStore } from '@/stores/useAuthStore'

const { theme, toggle: toggleTheme } = useTheme()
const auth = useAuthStore()
const route = useRoute()
const router = useRouter()

// Login / onboarding / accept-invite render full-screen without the admin shell.
const bareLayout = computed(() =>
  ['login', 'onboarding', 'accept-invite'].includes(route.name as string),
)
const showShell = computed(() => auth.isAuthenticated && !bareLayout.value)

function onSwitchRestaurant(e: Event) {
  auth.setActiveRestaurant((e.target as HTMLSelectElement).value)
  router.go(0)
}

function logout() {
  auth.logout()
  router.replace('/login')
}
</script>

<template>
  <RouterView v-if="!showShell" />
  <div v-else class="min-h-screen bg-paper text-ink flex flex-col md:flex-row relative overflow-hidden">
    <!-- Background blooms -->
    <div class="bloom bloom-accent" style="top: -120px; right: -140px; width: 420px; height: 420px;" />
    <div class="bloom bloom-lime" style="bottom: -120px; left: 12%; width: 360px; height: 360px;" />

    <!-- Single theme toggle, fixed top-right -->
    <button
      class="theme-toggle"
      :aria-label="theme === 'dark' ? 'Comutați la tema deschisă' : 'Comutați la tema închisă'"
      :aria-pressed="theme === 'dark'"
      @click="toggleTheme"
    >
      <svg v-if="theme === 'dark'" viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
        <circle cx="12" cy="12" r="4" />
        <path d="M12 2v2M12 20v2M4.93 4.93l1.41 1.41M17.66 17.66l1.41 1.41M2 12h2M20 12h2M4.93 19.07l1.41-1.41M17.66 6.34l1.41-1.41" />
      </svg>
      <svg v-else viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
        <path d="M21 12.79A9 9 0 1 1 11.21 3 7 7 0 0 0 21 12.79z" />
      </svg>
    </button>

    <nav
      class="relative z-10 w-full md:w-72 flex-shrink-0 p-5 md:p-6 flex md:flex-col gap-1 md:gap-1.5 md:min-h-screen md:border-r border-ink-12 nav-shell"
      aria-label="Navigare admin"
    >
      <!-- Brand block -->
      <div class="hidden md:block mb-8">
        <div class="eyebrow mb-2">SMART RESTAURANT</div>
        <div class="display-title text-[34px] leading-[0.95]">
          Admin<br /><span class="text-accent">Curții.</span>
        </div>
        <div class="mt-3 flex items-center gap-2">
          <span class="live-dot" aria-hidden="true"></span>
          <span class="font-mono text-[13px] uppercase tracking-[0.06em] text-ink font-semibold">Versiunea 1</span>
        </div>
      </div>

      <!-- Mobile brand (leaves room for the fixed top-right toggle) -->
      <div class="md:hidden flex items-center gap-3 pr-14 flex-1">
        <div class="display-title text-2xl">Smart Restaurant</div>
      </div>

      <div class="hidden md:block eyebrow mb-2 mt-2">Navigare</div>

      <RouterLink to="/" class="nav-link" active-class="nav-link-active">
        <span class="nav-icon" aria-hidden="true">◆</span>
        <span class="nav-label">Dashboard</span>
        <span class="nav-arrow" aria-hidden="true">→</span>
      </RouterLink>

      <RouterLink to="/menu" class="nav-link" active-class="nav-link-active">
        <span class="nav-icon">☰</span>
        <span class="nav-label">Meniu</span>
        <span class="nav-arrow">→</span>
      </RouterLink>

      <RouterLink to="/orders/active" class="nav-link" active-class="nav-link-active">
        <span class="nav-icon" aria-hidden="true">▤</span>
        <span class="nav-label">Comenzi active</span>
        <span class="nav-arrow" aria-hidden="true">→</span>
      </RouterLink>

      <RouterLink to="/ai-config" class="nav-link" active-class="nav-link-active">
        <span class="nav-icon" aria-hidden="true">✦</span>
        <span class="nav-label">Configurare AI</span>
        <span class="nav-arrow" aria-hidden="true">→</span>
      </RouterLink>

      <RouterLink to="/rules" class="nav-link" active-class="nav-link-active">
        <span class="nav-icon" aria-hidden="true">∞</span>
        <span class="nav-label">Reguli</span>
        <span class="nav-arrow" aria-hidden="true">→</span>
      </RouterLink>

      <RouterLink to="/orders" class="nav-link" active-class="nav-link-active">
        <span class="nav-icon" aria-hidden="true">◐</span>
        <span class="nav-label">Jurnal comenzi</span>
        <span class="nav-arrow" aria-hidden="true">→</span>
      </RouterLink>

      <RouterLink to="/tables" class="nav-link" active-class="nav-link-active">
        <span class="nav-icon" aria-hidden="true">▦</span>
        <span class="nav-label">Mese &amp; QR</span>
        <span class="nav-arrow" aria-hidden="true">→</span>
      </RouterLink>

      <RouterLink
        v-if="auth.isOrgOwnerOfActive"
        to="/organization"
        class="nav-link"
        active-class="nav-link-active"
      >
        <span class="nav-icon" aria-hidden="true">⬢</span>
        <span class="nav-label">Organizație</span>
        <span class="nav-arrow" aria-hidden="true">→</span>
      </RouterLink>

      <!-- Footer chip -->
      <div class="hidden md:flex mt-auto pt-6 items-center gap-3 text-ink-70">
        <div class="w-10 h-10 rounded-full bg-ink text-paperHi grid place-items-center font-bold text-base flex-shrink-0">
          {{ (auth.user?.fullName || 'A').charAt(0).toUpperCase() }}
        </div>
        <div class="leading-snug flex-1 min-w-0">
          <select
            v-if="auth.accessibleRestaurants.length"
            class="rest-switch"
            :value="auth.activeRestaurantId ?? ''"
            aria-label="Schimbă locația"
            @change="onSwitchRestaurant"
          >
            <option v-for="r in auth.accessibleRestaurants" :key="r.id" :value="r.id">
              {{ r.name }}
            </option>
          </select>
          <div class="text-base font-semibold text-ink truncate">{{ auth.user?.fullName || 'Manager' }}</div>
        </div>
      </div>

      <button
        type="button"
        class="nav-link nav-link-logout hidden md:flex mt-2"
        aria-label="Deconectare"
        @click="logout"
      >
        <span class="nav-icon" aria-hidden="true">⏻</span>
        <span class="nav-label">Deconectare</span>
        <span class="nav-arrow" aria-hidden="true">→</span>
      </button>
    </nav>

    <main class="relative z-10 flex-1 p-4 md:p-10 overflow-y-auto">
      <RouterView />
    </main>

    <ToastContainer />
  </div>
</template>

<style scoped>
.nav-shell {
  background: color-mix(in srgb, var(--paperHi) 80%, transparent);
  backdrop-filter: blur(12px);
  -webkit-backdrop-filter: blur(12px);
}

.nav-link {
  display: flex;
  align-items: center;
  gap: 14px;
  padding: 14px 18px;
  min-height: var(--tap-min);
  border-radius: 999px;
  font-size: 17px;
  font-weight: 600;
  color: var(--ink);
  border: 1px solid transparent;
  transition: background 0.18s ease, color 0.18s ease, border-color 0.18s ease, transform 0.18s ease;
  position: relative;
}
.nav-link:hover {
  background: var(--ink06);
  color: var(--ink);
}
.nav-icon {
  width: 34px;
  height: 34px;
  border-radius: 50%;
  display: grid;
  place-items: center;
  background: var(--ink06);
  font-size: 16px;
  flex-shrink: 0;
  color: var(--ink);
}
.nav-label { flex: 1; }
.nav-arrow {
  opacity: 0;
  transform: translateX(-4px);
  transition: opacity 0.18s ease, transform 0.18s ease;
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  color: var(--ink40);
}
.nav-link:hover .nav-arrow {
  opacity: 1;
  transform: translateX(0);
}
.nav-link-active {
  background: var(--ink) !important;
  color: var(--paperHi) !important;
  border-color: var(--ink) !important;
  box-shadow: var(--shadow-soft);
}
.nav-link-active .nav-icon {
  background: var(--lime);
  color: var(--onLime);
}
.nav-link-active .nav-arrow {
  opacity: 1;
  transform: translateX(0);
  color: var(--lime);
}

/* Single floating theme toggle — fixed top-right of the viewport */
.theme-toggle {
  position: fixed;
  top: 18px;
  right: 18px;
  width: var(--tap-min);
  height: var(--tap-min);
  border-radius: 50%;
  background: var(--card);
  color: var(--ink);
  border: 1px solid var(--ink12);
  display: grid;
  place-items: center;
  cursor: pointer;
  z-index: 60;
  font-family: inherit;
  box-shadow: var(--shadow-soft);
  transition: transform 0.15s ease, background 0.18s ease, box-shadow 0.18s ease;
}
.theme-toggle:hover {
  background: var(--ink06);
  transform: translateY(-1px);
}
.theme-toggle:active { transform: scale(0.92); }

/* Restaurant switcher — styled to read like the existing mono eyebrow label */
.rest-switch {
  appearance: none;
  background: transparent;
  border: none;
  padding: 0;
  margin: 0 0 1px;
  max-width: 100%;
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 12px;
  text-transform: uppercase;
  letter-spacing: 0.06em;
  font-weight: 600;
  color: var(--ink70, var(--ink));
  cursor: pointer;
}
.rest-switch:focus-visible {
  outline: 2px solid var(--accent, currentColor);
  outline-offset: 2px;
}
/* Logout reuses the sidebar nav-link language; reset the native <button> chrome */
button.nav-link {
  width: 100%;
  background: transparent;
  font-family: inherit;
  cursor: pointer;
  text-align: left;
}
.nav-link-logout:hover {
  background: var(--ink06);
}
.nav-link-logout:hover .nav-icon {
  background: var(--ink);
  color: var(--paperHi);
}
</style>
