<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/useAuthStore'
import { orgApi, type InvitationSummary, type RestaurantSummary } from '@/services/api-client'
import { useToast } from '@/composables/useToast'

const auth = useAuthStore()
const toast = useToast()
const router = useRouter()

const orgId = computed(() => auth.activeOrganizationId)
const canManage = computed(() => auth.isOrgOwnerOfActive)

const restaurants = ref<RestaurantSummary[]>([])
const invitations = ref<InvitationSummary[]>([])

const newRestaurantName = ref('')
const newRestaurantAddress = ref('')
const inviteEmail = ref('')
const inviteRestaurantId = ref('')
const busy = ref(false)

async function refresh() {
  if (!orgId.value) return
  restaurants.value = await orgApi.listRestaurants(orgId.value)
  if (canManage.value) {
    invitations.value = await orgApi.listInvitations(orgId.value)
  }
  if (!inviteRestaurantId.value && restaurants.value.length) {
    inviteRestaurantId.value = restaurants.value[0].id
  }
}

onMounted(refresh)

async function createRestaurant() {
  if (!orgId.value || !newRestaurantName.value.trim()) return
  busy.value = true
  try {
    await orgApi.createRestaurant(orgId.value, {
      name: newRestaurantName.value.trim(),
      address: newRestaurantAddress.value.trim() || undefined,
    })
    newRestaurantName.value = ''
    newRestaurantAddress.value = ''
    await auth.loadAccessibleOrganizations()
    await refresh()
    toast.success('Locație creată.')
  } catch (e) {
    toast.error(e instanceof Error ? e.message : 'Eroare la crearea locației.')
  } finally {
    busy.value = false
  }
}

async function sendInvite() {
  if (!orgId.value || !inviteEmail.value.trim() || !inviteRestaurantId.value) return
  busy.value = true
  try {
    await orgApi.inviteCoOwner(orgId.value, inviteEmail.value.trim(), inviteRestaurantId.value)
    inviteEmail.value = ''
    await refresh()
    toast.success('Invitație creată.')
  } catch (e) {
    toast.error(e instanceof Error ? e.message : 'Eroare la trimiterea invitației.')
  } finally {
    busy.value = false
  }
}

function inviteLink(i: InvitationSummary) {
  return `${window.location.origin}${i.acceptPath}`
}

async function copyLink(i: InvitationSummary) {
  await navigator.clipboard.writeText(inviteLink(i))
  toast.success('Link copiat.')
}

async function revoke(i: InvitationSummary) {
  if (!orgId.value) return
  await orgApi.revokeInvitation(orgId.value, i.id)
  await refresh()
}

function selectLocation(r: RestaurantSummary) {
  if (r.id === auth.activeRestaurantId) {
    router.push('/')
    return
  }
  auth.setActiveRestaurant(r.id)
  window.location.assign(import.meta.env.BASE_URL || '/')
}
</script>

<template>
  <div v-if="!canManage" class="card p-6 italic">
    Doar proprietarul organizației poate gestiona locațiile și colaboratorii.
  </div>

  <template v-else>
    <section class="card p-6 mb-6">
      <div class="eyebrow mb-2">Locații</div>
      <h2 class="display-title text-2xl mb-1">Restaurantele lanțului</h2>
      <p class="text-ink-70 text-sm mb-4">
        Apasă pe o locație pentru a o gestiona — meniul, comenzile și configurarea AI
        sunt separate pentru fiecare locație.
      </p>

      <button
        v-for="r in restaurants"
        :key="r.id"
        type="button"
        class="loc-card mb-2"
        :class="{ 'loc-card-active': r.id === auth.activeRestaurantId }"
        @click="selectLocation(r)"
      >
        <div class="min-w-0 flex-1 text-left">
          <div class="font-semibold truncate">{{ r.name }}</div>
          <div class="text-ink-70 text-sm truncate">{{ r.address || '—' }}</div>
        </div>
        <div class="flex items-center gap-2 flex-shrink-0">
          <span v-if="r.id === auth.activeRestaurantId" class="status-pill status-active">activ acum</span>
          <span v-else-if="r.isActive" class="status-pill status-on">activ</span>
          <span v-else class="status-pill status-off">inactiv</span>
          <span class="loc-arrow" aria-hidden="true">→</span>
        </div>
      </button>

      <form class="mt-4 flex flex-col md:flex-row gap-2" @submit.prevent="createRestaurant">
        <input v-model="newRestaurantName" placeholder="Nume locație nouă" class="input flex-1" required />
        <input v-model="newRestaurantAddress" placeholder="Adresă (opțional)" class="input flex-1" />
        <button type="submit" class="btn btn-primary" :disabled="busy">Adaugă locație</button>
      </form>
    </section>

    <section class="card p-6">
      <div class="eyebrow mb-2">Co-proprietari</div>
      <h2 class="display-title text-2xl mb-1">Invitații</h2>
      <p class="text-ink-70 text-sm mb-4">
        Invită un alt administrator la una dintre locațiile tale. Va primi acces complet la
        acel restaurant, dar nu și la configurarea organizației.
      </p>

      <form class="flex flex-col md:flex-row gap-2 mb-4" @submit.prevent="sendInvite">
        <input v-model="inviteEmail" type="email" placeholder="email@exemplu.ro" class="input flex-1" required />
        <select v-model="inviteRestaurantId" class="input">
          <option v-for="r in restaurants" :key="r.id" :value="r.id">{{ r.name }}</option>
        </select>
        <button type="submit" class="btn btn-primary" :disabled="busy">Invită</button>
      </form>

      <div v-if="!invitations.length" class="text-ink-70 italic text-sm">Nicio invitație încă.</div>
      <ul v-else class="divide-y divide-ink-12">
        <li v-for="i in invitations" :key="i.id" class="py-3 flex flex-col items-stretch gap-3 sm:flex-row sm:items-center">
          <div class="min-w-0 flex-1">
            <div class="font-semibold truncate">{{ i.email }}</div>
            <div class="text-ink-70 text-xs">
              {{ i.status }} · expiră {{ new Date(i.expiresAt).toLocaleDateString() }}
            </div>
          </div>
          <div v-if="i.status === 'pending'" class="flex gap-2 flex-wrap shrink-0">
            <button type="button" class="btn flex-1 sm:flex-none" @click="copyLink(i)">Copiază link</button>
            <button type="button" class="btn btn-danger flex-1 sm:flex-none" @click="revoke(i)">Revocă</button>
          </div>
        </li>
      </ul>
    </section>
  </template>
</template>

<style scoped>
.card {
  background: var(--card, var(--paperHi));
  border: 1px solid var(--ink12);
  border-radius: 18px;
  box-shadow: var(--shadow-soft);
}
.input {
  width: 100%;
  padding: 12px 14px;
  border-radius: 12px;
  border: 1px solid var(--ink12);
  background: var(--card, #fff);
  color: var(--ink);
  font-size: 15px;
}
.btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
  padding: 10px 16px;
  min-height: var(--tap-min);
  border-radius: 999px;
  border: 1px solid var(--ink12);
  background: transparent;
  color: var(--ink);
  font-weight: 600;
  font-size: 14px;
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
.loc-card {
  width: 100%;
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 14px 16px;
  border-radius: 14px;
  background: var(--card, var(--paperHi));
  border: 1px solid var(--ink12);
  cursor: pointer;
  transition: border-color 0.18s ease, box-shadow 0.18s ease, transform 0.18s ease;
  font: inherit;
  color: inherit;
}
.loc-card:hover,
.loc-card-active {
  border-color: var(--ink);
  box-shadow: var(--shadow-soft);
  transform: translateY(-1px);
}
.loc-arrow {
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  color: var(--ink40);
  opacity: 0;
  transform: translateX(-4px);
  transition: opacity 0.18s ease, transform 0.18s ease;
}
.loc-card:hover .loc-arrow,
.loc-card-active .loc-arrow {
  opacity: 1;
  transform: translateX(0);
  color: var(--ink);
}
.status-pill {
  display: inline-flex;
  align-items: center;
  padding: 4px 10px;
  border-radius: 999px;
  font-size: 11px;
  font-weight: 600;
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  text-transform: uppercase;
  letter-spacing: 0.06em;
}
.status-active { background: var(--ink); color: var(--paperHi); }
.status-on { background: var(--lime, #b8e986); color: var(--onLime, #0f3a0f); }
.status-off { background: #e5e7eb; color: #6b7280; }
</style>
