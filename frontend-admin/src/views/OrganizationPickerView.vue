<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore, type AccessibleOrganization } from '@/stores/useAuthStore'

const auth = useAuthStore()
const router = useRouter()
const loading = ref(true)
const error = ref<string | null>(null)

onMounted(async () => {
  try {
    // Always refresh on visit — memberships may have changed (waiter approval, new invite).
    await auth.loadAccessibleOrganizations()
  } catch (e) {
    error.value = e instanceof Error ? e.message : 'Nu am putut încărca organizațiile.'
  } finally {
    loading.value = false
  }
})

const orgs = computed<AccessibleOrganization[]>(() => auth.accessibleOrganizations)

function roleLabel(role: string) {
  if (role === 'OrgOwner') return 'Proprietar'
  if (role === 'RestaurantAdmin') return 'Administrator'
  if (role === 'Waiter') return 'Ospătar'
  return role
}

function enter(orgId: string) {
  auth.setActiveOrganization(orgId)
  router.push('/')
}

function goOnboarding() {
  router.push('/onboarding')
}

function logout() {
  auth.logout()
  router.replace('/login')
}
</script>

<template>
  <div class="min-h-screen bg-paper text-ink relative overflow-hidden flex flex-col">
    <div class="bloom bloom-accent" style="top: -120px; right: -140px; width: 420px; height: 420px;" />
    <div class="bloom bloom-lime" style="bottom: -120px; left: 12%; width: 360px; height: 360px;" />

    <header class="relative z-10 flex items-center justify-between p-6 md:px-10">
      <div>
        <div class="eyebrow">SMART RESTAURANT</div>
        <div class="display-title text-2xl mt-1">Alege o organizație</div>
      </div>
      <button class="nav-link nav-link-logout" type="button" @click="logout">
        <span class="nav-icon" aria-hidden="true">⏻</span>
        <span class="nav-label">Deconectare</span>
      </button>
    </header>

    <main class="relative z-10 flex-1 px-6 md:px-10 pb-10 max-w-5xl w-full mx-auto">
      <p class="text-ink-70 mb-6">
        Selectează organizația cu care vrei să lucrezi. Vei putea schimba mai târziu din bara laterală.
      </p>

      <div v-if="loading" class="text-ink-70">Se încarcă...</div>

      <div v-else-if="error" class="card p-6 border border-rose-300 text-rose-700">
        {{ error }}
      </div>

      <div v-else-if="orgs.length === 0" class="card p-6">
        <div class="text-lg font-semibold mb-2">Nu ești încă în nicio organizație</div>
        <p class="text-ink-70 mb-4">
          Creează prima ta organizație pentru a începe să gestionezi restaurantele și meniurile.
        </p>
        <button class="btn btn-primary" type="button" @click="goOnboarding">
          Creează o organizație
        </button>
      </div>

      <div v-else class="grid gap-4 md:grid-cols-2">
        <button
          v-for="o in orgs"
          :key="o.id"
          type="button"
          class="org-tile text-left"
          @click="enter(o.id)"
        >
          <div class="flex items-start justify-between gap-3">
            <div class="min-w-0">
              <div class="text-lg font-semibold truncate">{{ o.name }}</div>
              <div class="mt-1 font-mono text-[12px] uppercase tracking-[0.06em] text-ink-70">
                {{ roleLabel(o.role) }} · {{ o.restaurantCount }} restaurant{{ o.restaurantCount === 1 ? '' : 'e' }}
              </div>
            </div>
            <span class="nav-arrow" aria-hidden="true">→</span>
          </div>
        </button>
      </div>
    </main>
  </div>
</template>

<style scoped>
.org-tile {
  background: var(--card, var(--paperHi));
  border: 1px solid var(--ink12);
  border-radius: 18px;
  padding: 22px;
  transition: transform 0.18s ease, background 0.18s ease, border-color 0.18s ease, box-shadow 0.18s ease;
  cursor: pointer;
  box-shadow: var(--shadow-soft);
}
.org-tile:hover {
  background: var(--ink06);
  border-color: var(--ink);
  transform: translateY(-2px);
}
.org-tile:focus-visible {
  outline: 2px solid var(--accent, currentColor);
  outline-offset: 2px;
}
.nav-arrow {
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  color: var(--ink40);
}
.btn {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  padding: 10px 18px;
  border-radius: 999px;
  font-weight: 600;
  border: 1px solid var(--ink);
  cursor: pointer;
}
.btn-primary {
  background: var(--ink);
  color: var(--paperHi);
}
.nav-link {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  padding: 8px 16px;
  border-radius: 999px;
  background: transparent;
  border: 1px solid var(--ink12);
  cursor: pointer;
  color: var(--ink);
}
.nav-link:hover {
  background: var(--ink06);
}
</style>
