<script setup lang="ts">
import { computed, onMounted, watch } from 'vue'
import { RouterLink, RouterView, useRoute, useRouter } from 'vue-router'
import { useTheme } from '@/composables/useTheme'
import BrandLogo from '@/components/BrandLogo.vue'
import ToastContainer from '@/components/ToastContainer.vue'
import { useAuthStore } from '@/stores/useAuthStore'
import { useMenuImportStore } from '@/stores/useMenuImportStore'
import { useWaiterCallsStore } from '@/stores/useWaiterCallsStore'
import { useInventoryStore } from '@/stores/useInventoryStore'

const { theme, toggle: toggleTheme } = useTheme()
const auth = useAuthStore()
const route = useRoute()
const router = useRouter()
const importStore = useMenuImportStore()
const waiterCalls = useWaiterCallsStore()
const inventory = useInventoryStore()

// Login / onboarding / org-picker / accept-invite / waiter-signup / awaiting-approval render
// full-screen without the admin shell.
const bareLayout = computed(() =>
  ['login', 'onboarding', 'org-picker', 'accept-invite', 'waiter-signup', 'awaiting-approval'].includes(
    route.name as string,
  ),
)
const showShell = computed(() => auth.isAuthenticated && !bareLayout.value)

// Try resuming any in-flight import as soon as the shell mounts for an authenticated user.
onMounted(() => {
  if (auth.isAuthenticated) void importStore.tryResume()
})
watch(
  () => auth.isAuthenticated,
  (isAuth, was) => {
    if (isAuth && !was) void importStore.tryResume()
  },
)

// Keep the global waiter-call connection alive for any signed-in staffer with an
// active restaurant — independent of the current page — so calls surface anywhere.
watch(
  () => [auth.isAuthenticated, auth.activeRestaurantId] as const,
  ([isAuth, rid]) => {
    if (isAuth && rid) void waiterCalls.start(rid)
    else void waiterCalls.stop()
    // Load stock once per restaurant so the "Inventar" nav badge can show the low-stock count.
    if (isAuth && rid && !auth.isWaiter) void inventory.fetchIngredients()
  },
  { immediate: true },
)

// Global progress pill: visible whenever an import is in flight and we're not already on the import page.
const importInFlight = computed(() => {
  if (importStore.isScanning) return true
  if (importStore.mode === 'pdf' && importStore.pdfJob) {
    const s = importStore.pdfJob.status
    return s === 'uploading' || s === 'pending' || s === 'extracting'
  }
  return false
})
const showImportPill = computed(
  () => showShell.value && importInFlight.value && route.path !== '/menu/import',
)

// Persistent floating alert while calls are open — visible on every dashboard page
// except the calls view itself (where the full list already shows).
const showCallsPill = computed(
  () => showShell.value && waiterCalls.count > 0 && route.name !== 'waiter-calls',
)
const importPillLabel = computed(() => {
  if (importStore.mode === 'pdf' && importStore.pdfJob) {
    const j = importStore.pdfJob
    if (j.totalPages > 0) {
      const pct = Math.round((j.processedPages / j.totalPages) * 100)
      return `Import PDF · ${j.fileName} · ${pct}%`
    }
    return `Import PDF · ${j.fileName} · pornește`
  }
  if (importStore.isScanning) {
    const n = importStore.files.length
    return `Scanare meniu · ${n} ${n === 1 ? 'fotografie' : 'fotografii'}`
  }
  return 'Import în curs'
})

function onSwitchRestaurant(e: Event) {
  auth.setActiveRestaurant((e.target as HTMLSelectElement).value)
  router.go(0)
}

function switchOrganization() {
  auth.clearActiveOrganization()
  router.push('/select-organization')
}

function logout() {
  auth.logout()
  router.replace('/login')
}
</script>

<template>
  <RouterView v-if="!showShell" />
  <div v-else class="min-h-screen md:h-screen bg-paper text-ink flex flex-col md:flex-row relative overflow-hidden">
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
      class="relative z-10 w-full md:w-72 flex-shrink-0 p-5 md:p-6 flex md:flex-col gap-1 md:gap-1.5 md:h-screen md:overflow-y-auto md:border-r border-ink-12 nav-shell"
      aria-label="Navigare admin"
    >
      <!-- Brand block -->
      <div class="hidden md:block mb-6">
        <BrandLogo class="mb-3" />
        <div class="display-title text-[34px] leading-[0.95]">
          {{ auth.activeRestaurant?.name ?? 'Restaurant' }}
        </div>
      </div>

      <!-- Mobile brand (leaves room for the fixed top-right toggle) -->
      <div class="md:hidden flex items-center gap-3 pr-14 flex-1">
        <BrandLogo />
      </div>

      <!-- Account chip — moved to the top with inline Settings + Logout buttons -->
      <div class="hidden md:flex account-chip mb-6">
        <div class="account-avatar">
          <img
            v-if="auth.user?.avatarUrl"
            :src="auth.user.avatarUrl"
            alt=""
          />
          <template v-else>{{ (auth.user?.fullName || 'A').charAt(0).toUpperCase() }}</template>
        </div>
        <div class="account-meta">
          <div class="account-name" :title="auth.user?.fullName">
            {{ auth.user?.fullName || 'Manager' }}
          </div>
          <div v-if="auth.user?.email" class="account-email" :title="auth.user.email">
            {{ auth.user.email }}
          </div>
        </div>
        <div class="account-actions">
          <RouterLink
            to="/settings/profile"
            class="icon-btn"
            aria-label="Setări"
            title="Setări"
            active-class="icon-btn-active"
          >
            <svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
              <circle cx="12" cy="12" r="3" />
              <path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-4 0v-.09a1.65 1.65 0 0 0-1-1.51 1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1 0-4h.09a1.65 1.65 0 0 0 1.51-1 1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 2.83-2.83l.06.06a1.65 1.65 0 0 0 1.82.33h0a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 4 0v.09a1.65 1.65 0 0 0 1 1.51h0a1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82v0a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1z" />
            </svg>
          </RouterLink>
          <button
            type="button"
            class="icon-btn icon-btn-danger"
            aria-label="Deconectare"
            title="Deconectare"
            @click="logout"
          >
            <svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
              <path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4" />
              <polyline points="16 17 21 12 16 7" />
              <line x1="21" y1="12" x2="9" y2="12" />
            </svg>
          </button>
        </div>
      </div>

      <!-- Active org + restaurant selector -->
      <div v-if="auth.activeOrganization" class="hidden md:flex active-org mb-4">
        <div class="eyebrow mb-1">Organizația</div>
        <div class="active-org-name">{{ auth.activeOrganization.name }}</div>
        <select
          v-if="auth.restaurantsInActiveOrg.length"
          class="rest-switch mt-1"
          :value="auth.activeRestaurantId ?? ''"
          aria-label="Schimbă locația"
          @change="onSwitchRestaurant"
        >
          <option v-for="r in auth.restaurantsInActiveOrg" :key="r.id" :value="r.id">
            {{ r.name }}
          </option>
        </select>
      </div>

      <div class="hidden md:block eyebrow mb-2 mt-2">Navigare</div>

      <RouterLink v-if="!auth.isWaiter" to="/" class="nav-link" active-class="nav-link-active">
        <span class="nav-icon" aria-hidden="true">◆</span>
        <span class="nav-label">Dashboard</span>
        <span class="nav-arrow" aria-hidden="true">→</span>
      </RouterLink>

      <RouterLink v-if="!auth.isWaiter" to="/menu" class="nav-link" active-class="nav-link-active">
        <span class="nav-icon">☰</span>
        <span class="nav-label">Meniu</span>
        <span class="nav-arrow">→</span>
      </RouterLink>

      <RouterLink v-if="!auth.isWaiter" to="/inventory" class="nav-link" active-class="nav-link-active">
        <span class="nav-icon" aria-hidden="true">▣</span>
        <span class="nav-label">Inventar</span>
        <span v-if="inventory.lowStockCount" class="nav-badge" aria-label="ingrediente cu stoc redus">{{ inventory.lowStockCount }}</span>
        <span v-else class="nav-arrow" aria-hidden="true">→</span>
      </RouterLink>

      <RouterLink to="/orders/active" class="nav-link" active-class="nav-link-active">
        <span class="nav-icon" aria-hidden="true">▤</span>
        <span class="nav-label">Comenzi active</span>
        <span class="nav-arrow" aria-hidden="true">→</span>
      </RouterLink>

      <RouterLink to="/waiter-calls" class="nav-link" active-class="nav-link-active">
        <span class="nav-icon" aria-hidden="true">◉</span>
        <span class="nav-label">Chemări</span>
        <span v-if="waiterCalls.count" class="nav-badge" aria-label="chemări active">{{ waiterCalls.count }}</span>
        <span v-else class="nav-arrow" aria-hidden="true">→</span>
      </RouterLink>

      <RouterLink v-if="!auth.isWaiter" to="/ai-config" class="nav-link" active-class="nav-link-active">
        <span class="nav-icon" aria-hidden="true">✦</span>
        <span class="nav-label">Configurare AI</span>
        <span class="nav-arrow" aria-hidden="true">→</span>
      </RouterLink>

      <RouterLink v-if="!auth.isWaiter" to="/rules" class="nav-link" active-class="nav-link-active">
        <span class="nav-icon" aria-hidden="true">∞</span>
        <span class="nav-label">Reguli</span>
        <span class="nav-arrow" aria-hidden="true">→</span>
      </RouterLink>

      <RouterLink to="/orders" class="nav-link" active-class="nav-link-active">
        <span class="nav-icon" aria-hidden="true">◐</span>
        <span class="nav-label">Jurnal comenzi</span>
        <span class="nav-arrow" aria-hidden="true">→</span>
      </RouterLink>

      <RouterLink v-if="!auth.isWaiter" to="/tables" class="nav-link" active-class="nav-link-active">
        <span class="nav-icon" aria-hidden="true">▦</span>
        <span class="nav-label">Mese &amp; QR</span>
        <span class="nav-arrow" aria-hidden="true">→</span>
      </RouterLink>

      <RouterLink
        v-if="auth.canManageWaitersOfActive"
        to="/waiters"
        class="nav-link"
        active-class="nav-link-active"
      >
        <span class="nav-icon" aria-hidden="true">⊕</span>
        <span class="nav-label">Ospătari</span>
        <span class="nav-arrow" aria-hidden="true">→</span>
      </RouterLink>

      <!-- Schimbă organizația — sole bottom action -->
      <button
        type="button"
        class="nav-link hidden md:flex mt-auto"
        aria-label="Schimbă organizația"
        @click="switchOrganization"
      >
        <span class="nav-icon" aria-hidden="true">⇄</span>
        <span class="nav-label">Schimbă organizația</span>
        <span class="nav-arrow" aria-hidden="true">→</span>
      </button>
    </nav>

    <main class="relative z-10 flex-1 p-4 md:p-10 overflow-y-auto">
      <RouterView />
    </main>

    <RouterLink
      v-if="showImportPill"
      to="/menu/import"
      class="import-pill"
      role="status"
      aria-live="polite"
    >
      <span class="import-pill-spinner" aria-hidden="true"></span>
      <span class="import-pill-label">{{ importPillLabel }}</span>
      <span class="import-pill-arrow" aria-hidden="true">→</span>
    </RouterLink>

    <RouterLink
      v-if="showCallsPill"
      to="/waiter-calls"
      class="calls-pill"
      role="status"
      aria-live="polite"
    >
      <span class="calls-pill-dot" aria-hidden="true"></span>
      <span class="calls-pill-label">
        {{ waiterCalls.count }} {{ waiterCalls.count === 1 ? 'chemare' : 'chemări' }} · Vezi
      </span>
      <span class="calls-pill-arrow" aria-hidden="true">→</span>
    </RouterLink>

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

/* Account chip — at the top of the sidebar, contains the avatar, name/email,
   and small Settings + Logout icon buttons. */
.account-chip {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 10px 12px;
  border-radius: 18px;
  background: var(--card, var(--paperHi));
  border: 1px solid var(--ink12);
  box-shadow: var(--shadow-soft);
}
.account-avatar {
  width: 42px;
  height: 42px;
  border-radius: 50%;
  background: var(--ink);
  color: var(--paperHi);
  display: grid;
  place-items: center;
  font-weight: 700;
  font-size: 16px;
  overflow: hidden;
  flex-shrink: 0;
}
.account-avatar img {
  width: 100%;
  height: 100%;
  object-fit: cover;
}
.account-meta {
  flex: 1;
  min-width: 0;
  line-height: 1.15;
}
.account-name {
  font-size: 14px;
  font-weight: 700;
  color: var(--ink);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.account-email {
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 11px;
  color: var(--ink-70, var(--ink));
  opacity: 0.7;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  margin-top: 2px;
}
.account-actions {
  display: flex;
  gap: 4px;
  flex-shrink: 0;
}
.icon-btn {
  width: 30px;
  height: 30px;
  border-radius: 50%;
  display: grid;
  place-items: center;
  background: transparent;
  border: 1px solid var(--ink12);
  color: var(--ink);
  cursor: pointer;
  transition: background 0.15s ease, color 0.15s ease, border-color 0.15s ease;
  flex-shrink: 0;
}
.icon-btn:hover {
  background: var(--ink06);
}
.icon-btn-active {
  background: var(--ink);
  color: var(--paperHi);
  border-color: var(--ink);
}
.icon-btn-danger:hover {
  background: rgba(185, 28, 28, 0.08);
  color: #b91c1c;
  border-color: #b91c1c;
}

/* Active org block sits between the account chip and the navigation list. */
.active-org {
  flex-direction: column;
  padding: 0 6px;
}
.active-org-name {
  font-size: 18px;
  font-weight: 700;
  color: var(--ink);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

/* Restaurant switcher — styled to read like the existing mono eyebrow label */
.rest-switch {
  appearance: none;
  background: transparent;
  border: none;
  padding: 0;
  margin: 0;
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

/* Logout / switch-org reuse the sidebar nav-link language; reset the native <button> chrome */
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

/* Floating import-in-progress pill — fixed bottom-right of the viewport */
.import-pill {
  position: fixed;
  bottom: 20px;
  right: 20px;
  z-index: 55;
  display: inline-flex;
  align-items: center;
  gap: 10px;
  padding: 10px 16px;
  border-radius: 999px;
  background: var(--ink);
  color: var(--paperHi);
  text-decoration: none;
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 12px;
  font-weight: 700;
  letter-spacing: 0.04em;
  box-shadow: var(--shadow-soft);
  border: 1px solid var(--ink);
  max-width: calc(100vw - 40px);
  transition: transform 0.15s ease, opacity 0.15s ease, background 0.15s ease;
}
.import-pill:hover {
  transform: translateY(-1px);
}
.import-pill-spinner {
  width: 10px;
  height: 10px;
  border-radius: 50%;
  background: var(--lime);
  box-shadow: 0 0 10px var(--lime);
  animation: import-pill-pulse 1.4s ease-in-out infinite;
  flex-shrink: 0;
}
.import-pill-label {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  max-width: 60vw;
}
.import-pill-arrow {
  opacity: 0.7;
  flex-shrink: 0;
}

@keyframes import-pill-pulse {
  0%, 100% { opacity: 0.55; transform: scale(0.92); }
  50%      { opacity: 1;    transform: scale(1.08); }
}

@media (prefers-reduced-motion: reduce) {
  .import-pill-spinner { animation: none; opacity: 1; }
}

/* Waiter-call count badge on the "Chemări" nav item */
.nav-badge {
  min-width: 22px;
  height: 22px;
  padding: 0 7px;
  border-radius: 999px;
  background: var(--accent);
  color: #fff;
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 12px;
  font-weight: 800;
  display: inline-grid;
  place-items: center;
  flex-shrink: 0;
  line-height: 1;
}
.nav-link-active .nav-badge {
  background: var(--lime);
  color: var(--onLime);
}

/* Floating waiter-call alert — fixed bottom-left so it never collides with the
   bottom-right import pill, the top-right theme toggle, or the mobile top nav.
   Accent colour + pulse so it's impossible to miss from any page. */
.calls-pill {
  position: fixed;
  bottom: 20px;
  left: 20px;
  z-index: 56;
  display: inline-flex;
  align-items: center;
  gap: 10px;
  padding: 11px 18px;
  border-radius: 999px;
  background: var(--accent);
  color: #fff;
  text-decoration: none;
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 13px;
  font-weight: 700;
  letter-spacing: 0.03em;
  box-shadow: 0 14px 30px -10px rgba(255, 74, 28, 0.6);
  border: 1px solid var(--accent);
  max-width: calc(100vw - 40px);
  animation: calls-pill-pulse 1.5s ease-in-out infinite;
  transition: transform 0.15s ease;
}
.calls-pill:hover {
  transform: translateY(-1px);
}
.calls-pill-dot {
  width: 9px;
  height: 9px;
  border-radius: 50%;
  background: #fff;
  box-shadow: 0 0 10px rgba(255, 255, 255, 0.9);
  flex-shrink: 0;
}
.calls-pill-label {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  max-width: 60vw;
}
.calls-pill-arrow {
  opacity: 0.85;
  flex-shrink: 0;
}
@keyframes calls-pill-pulse {
  0%, 100% { box-shadow: 0 14px 30px -10px rgba(255, 74, 28, 0.6), 0 0 0 0 rgba(255, 74, 28, 0.5); }
  50%      { box-shadow: 0 14px 30px -10px rgba(255, 74, 28, 0.6), 0 0 0 10px rgba(255, 74, 28, 0); }
}
@media (prefers-reduced-motion: reduce) {
  .calls-pill { animation: none; }
}
</style>
