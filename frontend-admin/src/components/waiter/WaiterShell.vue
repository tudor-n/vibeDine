<script setup lang="ts">
import { computed } from 'vue'
import { RouterLink, useRoute, useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/useAuthStore'
import { useBrandingStore } from '@/stores/useBrandingStore'
import { useTheme } from '@/composables/useTheme'
import { useWaiterCallsStore } from '@/stores/useWaiterCallsStore'
import { useWaiterRealtimeStore } from '@/stores/useWaiterRealtimeStore'

/**
 * Waiter Mode frame: a phone-first shell that replaces the sidebar console for
 * the `waiter-floor` / `waiter-table` / `waiter-order` routes. On desktop it
 * renders the exact same layout, centred — waiter mode is a phone product
 * everywhere, so there is no second (wide) design to keep in sync.
 *
 * The reverse "Mod admin" switch is hidden for `auth.isWaiter`: a waiter has no
 * admin console to go back to (the router guard would bounce them straight here).
 */
const route = useRoute()
const router = useRouter()
const auth = useAuthStore()
const branding = useBrandingStore()
const waiterCalls = useWaiterCallsStore()
const waiterRealtime = useWaiterRealtimeStore()
const { theme, toggle: toggleTheme } = useTheme()

const restaurantName = computed(() => auth.activeRestaurant?.name ?? 'Restaurant')

// "Mesele mele" is the floor filtered to the caller's own open sessions rather
// than a fourth route — the waiter-mode route set is frozen at three names.
const MINE_QUERY = 'ale-mele'
const mineActive = computed(
  () => route.name === 'waiter-floor' && route.query.filtru === MINE_QUERY,
)
const floorActive = computed(
  () =>
    (route.name === 'waiter-floor' && !mineActive.value) ||
    route.name === 'waiter-table' ||
    route.name === 'waiter-order',
)

const myTableCount = computed(() => waiterRealtime.myTables.length)

function backToAdmin() {
  router.push({ name: 'orders-active' })
}
</script>

<template>
  <div class="waiter-shell bg-paper text-ink">
    <header class="w-topbar">
      <div class="w-brand">
        <span class="w-eyebrow">Mod ospătar</span>
        <span class="w-rest" :title="restaurantName">{{ restaurantName }}</span>
      </div>
      <div class="w-topbar-actions">
        <button
          v-if="!branding.isEnabled"
          type="button"
          class="icon-btn"
          :aria-label="theme === 'dark' ? 'Comutați la tema deschisă' : 'Comutați la tema închisă'"
          :aria-pressed="theme === 'dark'"
          :title="theme === 'dark' ? 'Temă deschisă' : 'Temă închisă'"
          @click="toggleTheme"
        >
          <svg v-if="theme === 'dark'" viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
            <circle cx="12" cy="12" r="4" />
            <path d="M12 2v2M12 20v2M4.93 4.93l1.41 1.41M17.66 17.66l1.41 1.41M2 12h2M20 12h2M4.93 19.07l1.41-1.41M17.66 6.34l1.41-1.41" />
          </svg>
          <svg v-else viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
            <path d="M21 12.79A9 9 0 1 1 11.21 3 7 7 0 0 0 21 12.79z" />
          </svg>
        </button>
        <button
          v-if="!auth.isWaiter"
          type="button"
          class="icon-btn"
          title="Mod admin"
          aria-label="Mod admin"
          @click="backToAdmin"
        >
          <svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
            <rect x="3" y="3" width="7" height="7" rx="1.5" />
            <rect x="14" y="3" width="7" height="7" rx="1.5" />
            <rect x="3" y="14" width="7" height="7" rx="1.5" />
            <rect x="14" y="14" width="7" height="7" rx="1.5" />
          </svg>
        </button>
      </div>
    </header>

    <main class="w-main">
      <slot />
    </main>

    <nav class="w-tabbar" aria-label="Navigare mod ospătar">
      <RouterLink
        :to="{ name: 'waiter-floor' }"
        class="w-tab"
        :class="{ 'w-tab-active': floorActive }"
      >
        <span class="w-tab-icon" aria-hidden="true">▦</span>
        <span class="w-tab-label">Mese</span>
      </RouterLink>

      <RouterLink
        :to="{ name: 'waiter-floor', query: { filtru: MINE_QUERY } }"
        class="w-tab"
        :class="{ 'w-tab-active': mineActive }"
      >
        <span class="w-tab-icon" aria-hidden="true">
          ★
          <span v-if="myTableCount" class="w-tab-badge">{{ myTableCount }}</span>
        </span>
        <span class="w-tab-label">Mesele mele</span>
      </RouterLink>

      <RouterLink
        :to="{ name: 'waiter-active-orders' }"
        class="w-tab"
        active-class="w-tab-active"
      >
        <span class="w-tab-icon" aria-hidden="true">▤</span>
        <span class="w-tab-label">Comenzi</span>
      </RouterLink>

      <RouterLink :to="{ name: 'waiter-calls' }" class="w-tab">
        <span class="w-tab-icon" aria-hidden="true">
          ◉
          <span v-if="waiterCalls.count" class="w-tab-badge">{{ waiterCalls.count }}</span>
        </span>
        <span class="w-tab-label">Apeluri</span>
      </RouterLink>
    </nav>
  </div>
</template>

<style scoped>
/* The whole mode lives inside a phone-width column, centred on desktop. The
   fixed bars are pinned to that same column (not the viewport) so the layout
   reads identically at 390px and at 1920px. */
.waiter-shell {
  --w-frame: 28rem;
  --w-topbar-h: 60px;
  --w-tabbar-h: 68px;
  position: relative;
  min-height: 100vh;
  min-height: 100dvh;
  max-width: var(--w-frame);
  margin: 0 auto;
}

.w-topbar,
.w-tabbar {
  position: fixed;
  left: 50%;
  transform: translateX(-50%);
  width: min(100%, var(--w-frame));
  z-index: 40;
  background: color-mix(in srgb, var(--paperHi) 92%, transparent);
  backdrop-filter: blur(12px);
  -webkit-backdrop-filter: blur(12px);
}

.w-topbar {
  top: 0;
  height: var(--w-topbar-h);
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 0 14px;
  border-bottom: 1px solid var(--ink12);
}
.w-brand {
  display: flex;
  flex-direction: column;
  min-width: 0;
  flex: 1;
  line-height: 1.15;
}
.w-eyebrow {
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 10px;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.09em;
  color: var(--accent);
}
.w-rest {
  font-size: 16px;
  font-weight: 700;
  color: var(--ink);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.w-topbar-actions {
  display: flex;
  align-items: center;
  gap: 6px;
  flex-shrink: 0;
}

/* Content clears both bars; the extra bottom room keeps sticky in-page footers
   (the order screen's cart bar) above the tab bar. */
.w-main {
  padding: calc(var(--w-topbar-h) + 14px) 14px calc(var(--w-tabbar-h) + 22px);
}

.w-tabbar {
  bottom: 0;
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 4px;
  padding: 6px 8px calc(6px + env(safe-area-inset-bottom, 0px));
  border-top: 1px solid var(--ink12);
}
.w-tab {
  min-height: var(--tap-min);
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 3px;
  padding: 6px 4px;
  border-radius: 16px;
  color: var(--ink40);
  text-decoration: none;
  transition: background 0.16s ease, color 0.16s ease;
}
.w-tab:active {
  background: var(--ink06);
}
.w-tab-active {
  background: var(--ink);
  color: var(--lime);
}
.w-tab-icon {
  position: relative;
  font-size: 17px;
  line-height: 1;
}
.w-tab-label {
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 10.5px;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  text-align: center;
}
.w-tab-badge {
  position: absolute;
  top: -7px;
  left: 11px;
  min-width: 18px;
  height: 18px;
  padding: 0 5px;
  border-radius: 999px;
  background: var(--accent);
  color: var(--onAccent);
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 10.5px;
  font-weight: 800;
  display: inline-grid;
  place-items: center;
  line-height: 1;
}
</style>
