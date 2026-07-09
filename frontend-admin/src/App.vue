<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { RouterLink, RouterView, useRoute, useRouter } from 'vue-router'
import { useTheme } from '@/composables/useTheme'
import { useBrandTheme } from '@/composables/useBrandTheme'
import BrandLogo from '@/components/BrandLogo.vue'
import VibeDineLockup from '@/components/VibeDineLockup.vue'
import ToastContainer from '@/components/ToastContainer.vue'
import { useAuthStore } from '@/stores/useAuthStore'
import { useMenuImportStore } from '@/stores/useMenuImportStore'
import { useWaiterCallsStore } from '@/stores/useWaiterCallsStore'
import { useInventoryStore } from '@/stores/useInventoryStore'
import { useFloorStore } from '@/stores/useFloorStore'
import { useBrandingStore } from '@/stores/useBrandingStore'
import { resolveMediaUrl } from '@/services/mediaUrl'
import { resolveLogoBackground } from '@shared/branding/derive'

const { theme, toggle: toggleTheme } = useTheme()
const auth = useAuthStore()
const route = useRoute()
const router = useRouter()
const importStore = useMenuImportStore()
const waiterCalls = useWaiterCallsStore()
const inventory = useInventoryStore()
const floor = useFloorStore()
const branding = useBrandingStore()
const brandTheme = useBrandTheme()

// Plate behind the tenant logo in the sidebar (Auto/White/Transparent/Custom).
const logoPlate = computed(() =>
  resolveLogoBackground(branding.branding?.logoBackground ?? null, branding.branding?.logoIsDark ?? null),
)

// Re-skin the shell for a branded restaurant on bootstrap/switch (Task 9). A
// fetch failure must never block the shell, so it falls back to the classic
// palette rather than surfacing an error.
async function refreshBranding() {
  try {
    await branding.load()
    const b = branding.branding
    if (b?.enabled && b.primary && b.secondary && b.background) {
      brandTheme.applyBrand({ primary: b.primary, secondary: b.secondary, background: b.background })
    } else {
      brandTheme.clearBrand()
    }
  } catch {
    brandTheme.clearBrand()
  }
}

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
    if (isAuth && rid) {
      void waiterCalls.start(rid)
      // Keep the active-order feed alive app-wide so the sidebar "Sala" dot and
      // the Comenzi board stay live regardless of which page is open.
      void floor.startRealtime(rid)
      // Load stock once per restaurant so the "Inventar" nav badge can show the low-stock count.
      if (!auth.isWaiter) void inventory.fetchIngredients()
      // Re-skin the shell to the restaurant's tenant palette, if branded.
      void refreshBranding()
    } else {
      void waiterCalls.stop()
      void floor.stopRealtime()
      brandTheme.clearBrand()
    }
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

// --- Sidebar: grouped navigation + mobile drawer ---------------------------
const mobileNavOpen = ref(false)

type NavItem = { to: string; icon: string; label: string; show: boolean; badge?: number }
type NavGroup = { id: string; label: string; items: NavItem[] }

// A single source of truth for the nav, organised into collapsible categories.
// `show` mirrors the old per-link role conditions; empty groups are dropped.
const navGroups = computed<NavGroup[]>(() => {
  const full = !auth.isWaiter
  const groups: NavGroup[] = [
    {
      id: 'operations',
      label: 'Operațiuni',
      items: [
        { to: '/', icon: '◆', label: 'Dashboard', show: full },
        { to: '/orders/active', icon: '▤', label: 'Comenzi active', show: true },
        { to: '/waiter-calls', icon: '◉', label: 'Chemări', show: true, badge: waiterCalls.count },
        { to: '/orders', icon: '◐', label: 'Jurnal comenzi', show: true },
      ],
    },
    {
      id: 'menu',
      label: 'Meniu & AI',
      items: [
        { to: '/menu', icon: '☰', label: 'Meniu', show: full },
        { to: '/inventory', icon: '▣', label: 'Inventar', show: full, badge: inventory.lowStockCount },
        { to: '/happy-hour', icon: '◷', label: 'Happy Hour', show: full },
        { to: '/flash-deal', icon: '◈', label: 'Flash Deal', show: full },
        { to: '/rules', icon: '∞', label: 'Reguli', show: full },
      ],
    },
    {
      id: 'venue',
      label: 'Sală & locație',
      items: [
        { to: '/floor', icon: '▢', label: 'Sala', show: true, badge: floor.unclaimedCount },
        { to: '/tables', icon: '▦', label: 'Coduri QR', show: full },
      ],
    },
    {
      id: 'team',
      label: 'Echipă',
      items: [{ to: '/waiters', icon: '⊕', label: 'Ospătari', show: auth.canManageWaitersOfActive }],
    },
  ]
  return groups
    .map((g) => ({ ...g, items: g.items.filter((i) => i.show) }))
    .filter((g) => g.items.length > 0)
})

// Total of any item badges in a group — surfaced on the header when collapsed.
function groupBadge(g: NavGroup) {
  return g.items.reduce((n, i) => n + (i.badge || 0), 0)
}

// Categories are expanded by default so every allowed tab is visible at a glance.
// Collapsing them by default hid tabs — e.g. a waiter on the orders page couldn't
// see "Sala", and on the Sala page couldn't see the orders tabs. The user can
// still collapse any category and that choice sticks.
const userToggled = ref<Record<string, boolean>>({})
function groupOpen(id: string) {
  if (id in userToggled.value) return userToggled.value[id]
  return true
}
function toggleGroup(id: string) {
  userToggled.value = { ...userToggled.value, [id]: !groupOpen(id) }
}

// Close the drawer on navigation; lock body scroll while it's open.
watch(() => route.fullPath, () => { mobileNavOpen.value = false })
watch(mobileNavOpen, (open) => {
  document.body.style.overflow = open ? 'hidden' : ''
})

function onNavKeydown(e: KeyboardEvent) {
  if (e.key === 'Escape') mobileNavOpen.value = false
}
onMounted(() => window.addEventListener('keydown', onNavKeydown))
onUnmounted(() => {
  window.removeEventListener('keydown', onNavKeydown)
  document.body.style.overflow = ''
})
</script>

<template>
  <RouterView v-if="!showShell" />
  <div v-else class="min-h-screen md:h-screen bg-paper text-ink flex flex-col md:flex-row relative overflow-hidden">
    <!-- Background blooms -->
    <div class="bloom bloom-accent" style="top: -120px; right: -140px; width: 420px; height: 420px;" />
    <div class="bloom bloom-lime" style="bottom: -120px; left: 12%; width: 360px; height: 360px;" />

    <!-- Mobile top bar — the sidebar collapses into a drawer below md -->
    <header class="md:hidden mobile-topbar">
      <button
        type="button"
        class="hamburger"
        :aria-expanded="mobileNavOpen"
        aria-label="Deschide meniul"
        @click="mobileNavOpen = true"
      >
        <svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
          <line x1="3" y1="6" x2="21" y2="6" />
          <line x1="3" y1="12" x2="21" y2="12" />
          <line x1="3" y1="18" x2="21" y2="18" />
        </svg>
      </button>
      <div class="display-title text-xl">Smart Restaurant</div>
      <div class="topbar-right ml-auto">
        <span v-if="waiterCalls.count" class="nav-badge" aria-label="chemări active">{{ waiterCalls.count }}</span>
        <button
          v-if="!branding.isEnabled"
          type="button"
          class="hamburger"
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
      </div>
    </header>

    <!-- Drawer backdrop (mobile) -->
    <div v-if="mobileNavOpen" class="nav-backdrop md:hidden" @click="mobileNavOpen = false" />

    <nav
      class="nav-shell sidebar"
      :class="{ 'sidebar-open': mobileNavOpen }"
      aria-label="Navigare admin"
    >
      <!-- Drawer head (mobile): brand + close -->
      <div class="md:hidden drawer-head">
        <div class="display-title text-2xl">Admin <span class="text-accent">Curții.</span></div>
        <button type="button" class="icon-btn" aria-label="Închide meniul" @click="mobileNavOpen = false">
          <svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
            <line x1="18" y1="6" x2="6" y2="18" />
            <line x1="6" y1="6" x2="18" y2="18" />
          </svg>
        </button>
      </div>

      <!-- Brand block (desktop) -->
      <div class="hidden md:block mb-6">
        <template v-if="branding.isEnabled && branding.branding?.logoUrl">
          <div
            class="brand-logo-plate mb-2"
            :class="{ 'is-plated': logoPlate.plate }"
            :style="logoPlate.plate ? { background: logoPlate.background } : undefined"
          >
            <img
              :src="resolveMediaUrl(branding.branding.logoUrl)"
              alt=""
              style="height: 72px; object-fit: contain"
            />
          </div>
          <VibeDineLockup />
        </template>
        <template v-else>
          <BrandLogo class="mb-3" />
          <div class="display-title text-[34px] leading-[0.95]">
            {{ auth.activeRestaurant?.name ?? 'Restaurant' }}
          </div>
        </template>
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
      <div v-if="auth.activeOrganization" class="active-org mb-4">
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

      <!-- Grouped navigation — collapsible categories -->
      <div class="nav-groups">
        <div v-for="g in navGroups" :key="g.id" class="nav-group">
          <button
            type="button"
            class="nav-group-header"
            :class="{ 'nav-group-open': groupOpen(g.id) }"
            :aria-expanded="groupOpen(g.id)"
            @click="toggleGroup(g.id)"
          >
            <span class="nav-group-label">{{ g.label }}</span>
            <span
              v-if="!groupOpen(g.id) && groupBadge(g)"
              class="nav-badge"
              aria-label="notificări"
            >{{ groupBadge(g) }}</span>
            <span class="nav-group-chevron" aria-hidden="true">
              <svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round">
                <polyline points="6 9 12 15 18 9" />
              </svg>
            </span>
          </button>
          <div v-show="groupOpen(g.id)" class="nav-group-items">
            <RouterLink
              v-for="item in g.items"
              :key="item.to"
              :to="item.to"
              class="nav-link"
              active-class="nav-link-active"
            >
              <span class="nav-icon" aria-hidden="true">{{ item.icon }}</span>
              <span class="nav-label">{{ item.label }}</span>
              <span v-if="item.badge" class="nav-badge" aria-label="chemări active">{{ item.badge }}</span>
              <span v-else class="nav-arrow" aria-hidden="true">→</span>
            </RouterLink>
          </div>
        </div>
      </div>

      <!-- Schimbă organizația — bottom action -->
      <button
        type="button"
        class="nav-link switch-org mt-auto"
        aria-label="Schimbă organizația"
        @click="switchOrganization"
      >
        <span class="nav-icon" aria-hidden="true">⇄</span>
        <span class="nav-label">Schimbă organizația</span>
        <span class="nav-arrow" aria-hidden="true">→</span>
      </button>
    </nav>

    <main class="app-main relative z-10 flex-1 md:ml-72 p-4 md:p-10 overflow-y-auto">
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

/* Tenant logo plate — padded frame only when the resolver asks for a plate;
   bare (no box) when Auto picks transparent for a light/knockout logo. */
.brand-logo-plate {
  display: inline-flex;
  border-radius: 16px;
}
.brand-logo-plate.is-plated {
  padding: 10px 14px;
}

/* Sidebar shell — fixed left column on desktop (stays put while the main area
   scrolls), slide-in drawer on mobile. Because it's fixed (out of flow), <main>
   carries a matching left offset on desktop (md:ml-72) so content clears it. */
.sidebar {
  position: fixed;
  top: 0;
  left: 0;
  z-index: 10;
  display: flex;
  flex-direction: column;
  gap: 6px;
  width: 18rem;
  flex-shrink: 0;
  padding: 24px;
  height: 100vh;
  overflow-y: auto;
  border-right: 1px solid var(--ink12);
}

/* Mobile top bar — hidden on desktop, shown only in the mobile media query below.
   Controlled here (not via Tailwind md:hidden) because scoped styles outrank
   utility classes. */
.mobile-topbar {
  /* Fixed (not sticky): the root container has overflow-hidden and isn't itself a
     scroll container on mobile, so a sticky bar scrolls away with the page. Fixed
     pins it to the viewport top no matter which element scrolls. <main> carries a
     matching padding-top below so the first content isn't hidden behind it. */
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  z-index: 40;
  display: none;
  align-items: center;
  gap: 12px;
  padding: 12px 14px;
  background: color-mix(in srgb, var(--paperHi) 88%, transparent);
  backdrop-filter: blur(12px);
  -webkit-backdrop-filter: blur(12px);
  border-bottom: 1px solid var(--ink12);
}
.hamburger {
  width: var(--tap-min);
  height: var(--tap-min);
  border-radius: 14px;
  display: grid;
  place-items: center;
  background: var(--card, var(--paperHi));
  border: 1px solid var(--ink12);
  color: var(--ink);
  cursor: pointer;
  flex-shrink: 0;
  transition: transform 0.12s ease, background 0.15s ease;
}
.hamburger:hover { background: var(--ink06); }
.hamburger:active { transform: scale(0.94); }

/* Right-hand cluster of the mobile top bar: calls badge + theme toggle */
.topbar-right {
  display: flex;
  align-items: center;
  gap: 10px;
}

/* Drawer backdrop */
.nav-backdrop {
  position: fixed;
  inset: 0;
  z-index: 65;
  background: rgba(0, 0, 0, 0.42);
  -webkit-backdrop-filter: blur(2px);
  backdrop-filter: blur(2px);
  animation: nav-backdrop-in 0.2s ease;
}
@keyframes nav-backdrop-in {
  from { opacity: 0; }
  to { opacity: 1; }
}

/* Drawer header (brand + close) — mobile only; shown in the media query below. */
.drawer-head {
  display: none;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 18px;
}

/* Collapsible category groups */
.nav-groups {
  display: flex;
  flex-direction: column;
  gap: 2px;
}
.nav-group { display: flex; flex-direction: column; }
.nav-group + .nav-group { margin-top: 4px; }
.nav-group-header {
  display: flex;
  align-items: center;
  gap: 8px;
  width: 100%;
  padding: 9px 14px;
  background: transparent;
  border: none;
  cursor: pointer;
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 11px;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.08em;
  color: var(--ink);
  opacity: 0.6;
  border-radius: 12px;
  transition: opacity 0.15s ease, background 0.15s ease;
}
.nav-group-header:hover {
  opacity: 1;
  background: var(--ink06);
}
.nav-group-header.nav-group-open { opacity: 1; }
.nav-group-label {
  flex: 1;
  text-align: left;
}
.nav-group-chevron {
  display: grid;
  place-items: center;
  opacity: 0.7;
  transition: transform 0.2s ease;
}
.nav-group-open .nav-group-chevron { transform: rotate(180deg); }
.nav-group-items {
  display: flex;
  flex-direction: column;
  gap: 3px;
  padding: 2px 0 6px;
}

.switch-org { margin-top: auto; }

/* Below md: the sidebar becomes an off-canvas drawer; the top bar + drawer head
   appear. */
@media (max-width: 767.98px) {
  .sidebar {
    position: fixed;
    inset: 0 auto 0 0;
    width: min(85vw, 330px);
    height: 100%;
    transform: translateX(-100%);
    transition: transform 0.3s cubic-bezier(0.22, 1, 0.36, 1);
    z-index: 70;
    box-shadow: 0 24px 60px -24px rgba(0, 0, 0, 0.5);
  }
  .sidebar.sidebar-open { transform: translateX(0); }
  .mobile-topbar { display: flex; }
  .drawer-head { display: flex; }
  /* Clear the fixed top bar (~44px tap target + 12px×2 padding + 1px border)
     plus a small gap, so page content starts below it. */
  .app-main { padding-top: calc(var(--tap-min) + 40px); }
}
@media (prefers-reduced-motion: reduce) {
  .sidebar { transition: none; }
  .nav-backdrop { animation: none; }
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
  display: flex;
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
  color: var(--onAccent);
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
  color: var(--onAccent);
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
