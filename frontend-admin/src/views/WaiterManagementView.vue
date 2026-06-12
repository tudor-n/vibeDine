<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useAuthStore } from '@/stores/useAuthStore'
import { waiterApi, type WaiterRow, type WaiterSignupLink } from '@/services/api-client'
import { useToast } from '@/composables/useToast'

const auth = useAuthStore()
const toast = useToast()

const restaurantId = computed(() => auth.activeRestaurantId)
const restaurantName = computed(() => auth.activeRestaurant?.name ?? '')

const link = ref<WaiterSignupLink | null>(null)
const waiters = ref<WaiterRow[]>([])
const loading = ref(false)
const busy = ref(false)

const pending = computed(() => waiters.value.filter((w: WaiterRow) => w.status === 'pending_approval'))
const active = computed(() => waiters.value.filter((w: WaiterRow) => w.status === 'active'))
const rejected = computed(() => waiters.value.filter((w: WaiterRow) => w.status === 'rejected'))

async function refresh() {
  if (!restaurantId.value) return
  loading.value = true
  try {
    const [l, ws] = await Promise.all([
      waiterApi.getLink(restaurantId.value),
      waiterApi.listByRestaurant(restaurantId.value),
    ])
    link.value = l
    waiters.value = ws
  } catch (e) {
    toast.error(e instanceof Error ? e.message : 'Eroare la încărcarea ospătarilor.')
  } finally {
    loading.value = false
  }
}

onMounted(refresh)
watch(restaurantId, refresh)

const linkUrl = computed(() => (link.value ? `${window.location.origin}${link.value.signupPath}` : ''))

async function copyLink() {
  if (!linkUrl.value) return
  await navigator.clipboard.writeText(linkUrl.value)
  toast.success('Link copiat.')
}

async function regenerate() {
  if (!restaurantId.value) return
  if (!confirm('Vechiul link va deveni invalid. Continui?')) return
  busy.value = true
  try {
    link.value = await waiterApi.regenerateLink(restaurantId.value)
    toast.success('Link regenerat.')
  } catch (e) {
    toast.error(e instanceof Error ? e.message : 'Eroare la regenerarea linkului.')
  } finally {
    busy.value = false
  }
}

async function revoke() {
  if (!restaurantId.value) return
  if (!confirm('După revocare, nimeni nu va mai putea folosi linkul actual.')) return
  busy.value = true
  try {
    await waiterApi.revokeLink(restaurantId.value)
    await refresh()
    toast.success('Link revocat.')
  } catch (e) {
    toast.error(e instanceof Error ? e.message : 'Eroare la revocarea linkului.')
  } finally {
    busy.value = false
  }
}

async function approve(w: WaiterRow) {
  busy.value = true
  try {
    await waiterApi.approve(w.membershipId)
    await refresh()
    toast.success(`${w.fullName} aprobat.`)
  } catch (e) {
    toast.error(e instanceof Error ? e.message : 'Eroare la aprobare.')
  } finally {
    busy.value = false
  }
}

async function reject(w: WaiterRow) {
  busy.value = true
  try {
    await waiterApi.reject(w.membershipId)
    await refresh()
    toast.success(`${w.fullName} respins.`)
  } catch (e) {
    toast.error(e instanceof Error ? e.message : 'Eroare la respingere.')
  } finally {
    busy.value = false
  }
}

async function remove(w: WaiterRow) {
  if (!confirm(`Sigur vrei să elimini ${w.fullName} ca ospătar?`)) return
  busy.value = true
  try {
    await waiterApi.remove(w.membershipId)
    await refresh()
    toast.success(`${w.fullName} eliminat.`)
  } catch (e) {
    toast.error(e instanceof Error ? e.message : 'Eroare la ștergere.')
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <div class="max-w-6xl mx-auto">
    <div class="mb-8">
      <div class="eyebrow mb-4">Echipă</div>
      <h1 class="display-title text-[44px] md:text-[56px]">Ospătari</h1>
      <p class="text-ink-70 mt-2">{{ restaurantName }}</p>
    </div>

    <div v-if="!auth.canManageWaitersOfActive" class="card p-6 italic">
      Doar managerul restaurantului poate gestiona ospătarii.
    </div>

    <template v-else>
      <section class="card p-6 mb-6">
        <div class="eyebrow mb-2">Link de înscriere</div>
        <p class="text-sm text-ink-70 mb-4">
          Trimite linkul de mai jos ospătarilor. Oricine îl deschide poate crea un cont; tu îl aprobi
          de aici înainte ca el să poată folosi tabloul de comenzi.
        </p>

        <div v-if="link" class="flex flex-col md:flex-row md:items-center gap-3">
          <code class="link-code flex-1 break-all">{{ linkUrl }}</code>
          <div class="flex gap-2 flex-wrap">
            <button type="button" class="btn" :disabled="busy" @click="copyLink">Copiază</button>
            <button type="button" class="btn" :disabled="busy" @click="regenerate">Regenerează</button>
            <button type="button" class="btn btn-danger" :disabled="busy" @click="revoke">Revocă</button>
          </div>
        </div>
        <div v-else class="text-ink-70 text-sm">Se încarcă linkul…</div>
      </section>

      <section class="card p-6 mb-6">
        <div class="eyebrow mb-2">În așteptare ({{ pending.length }})</div>
        <div v-if="loading && !pending.length" class="text-ink-70 text-sm">Se încarcă…</div>
        <div v-else-if="!pending.length" class="text-ink-70 text-sm">Nicio cerere în așteptare.</div>
        <ul v-else class="divide-y divide-ink-12">
          <li v-for="w in pending" :key="w.membershipId" class="py-3 flex items-center gap-3">
            <div class="flex-1 min-w-0">
              <div class="font-semibold truncate">{{ w.fullName }}</div>
              <div class="text-xs text-ink-70 font-mono truncate">{{ w.email }}</div>
              <div v-if="!w.emailConfirmed" class="text-xs text-amber-700 mt-1">
                Email neconfirmat încă.
              </div>
            </div>
            <button type="button" class="btn btn-primary" :disabled="busy || !w.emailConfirmed" @click="approve(w)">
              Aprobă
            </button>
            <button type="button" class="btn btn-danger" :disabled="busy" @click="reject(w)">Respinge</button>
          </li>
        </ul>
      </section>

      <section class="card p-6 mb-6">
        <div class="eyebrow mb-2">Ospătari activi ({{ active.length }})</div>
        <div v-if="!active.length" class="text-ink-70 text-sm">Niciun ospătar activ.</div>
        <ul v-else class="divide-y divide-ink-12">
          <li v-for="w in active" :key="w.membershipId" class="py-3 flex items-center gap-3">
            <div class="flex-1 min-w-0">
              <div class="font-semibold truncate">{{ w.fullName }}</div>
              <div class="text-xs text-ink-70 font-mono truncate">{{ w.email }}</div>
            </div>
            <button type="button" class="btn btn-danger" :disabled="busy" @click="remove(w)">Elimină</button>
          </li>
        </ul>
      </section>

      <section v-if="rejected.length" class="card p-6">
        <div class="eyebrow mb-2">Respinși ({{ rejected.length }})</div>
        <ul class="divide-y divide-ink-12">
          <li v-for="w in rejected" :key="w.membershipId" class="py-3 flex items-center gap-3">
            <div class="flex-1 min-w-0">
              <div class="font-semibold truncate">{{ w.fullName }}</div>
              <div class="text-xs text-ink-70 font-mono truncate">{{ w.email }}</div>
            </div>
            <button type="button" class="btn" :disabled="busy" @click="remove(w)">Șterge</button>
          </li>
        </ul>
      </section>
    </template>
  </div>
</template>

<style scoped>
.card {
  background: var(--card, var(--paperHi));
  border: 1px solid var(--ink12);
  border-radius: 18px;
  box-shadow: var(--shadow-soft);
}
.btn {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 8px 14px;
  border-radius: 999px;
  border: 1px solid var(--ink12);
  background: transparent;
  color: var(--ink);
  font-weight: 600;
  cursor: pointer;
}
.btn:hover { background: var(--ink06); }
.btn:disabled { opacity: 0.55; cursor: not-allowed; }
.btn-primary {
  background: var(--ink);
  color: var(--paperHi);
  border-color: var(--ink);
}
.btn-danger {
  border-color: #b91c1c;
  color: #b91c1c;
}
.btn-danger:hover { background: rgba(185, 28, 28, 0.08); }
.link-code {
  display: inline-block;
  padding: 8px 12px;
  background: var(--ink06);
  border-radius: 10px;
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 12px;
}
</style>
