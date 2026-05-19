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
    await auth.loadAccessibleRestaurants()
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

/**
 * Switch the active location and jump into its dashboard. Menu, orders and
 * AI config are scoped per restaurant, so a full reload guarantees every
 * view and store re-fetches against the newly selected location.
 */
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
  <div class="max-w-6xl mx-auto relative">
    <div class="mb-8">
      <div class="eyebrow mb-4">Organizație</div>
      <h1 class="display-title text-[44px] md:text-[56px]">Locații &amp; invitații</h1>
    </div>

    <div v-if="!canManage" class="card-paper p-6 text-ink italic text-base">
      Doar proprietarul organizației poate gestiona locațiile și invitațiile.
    </div>

    <template v-else>
      <!-- Restaurants -->
      <section class="card-paper p-6 mb-6">
        <div class="eyebrow mb-2">Locații</div>
        <h2 class="display-title text-3xl mb-1">Restaurantele lanțului</h2>
        <p class="text-ink-70 text-sm mb-4">
          Apasă pe o locație pentru a o gestiona — meniul, comenzile și configurarea AI
          sunt separate pentru fiecare locație.
        </p>

        <button
          v-for="r in restaurants"
          :key="r.id"
          type="button"
          class="card-white loc-card p-4 mb-2 w-full flex items-center justify-between gap-3 text-left"
          :class="{ 'loc-card-active': r.id === auth.activeRestaurantId }"
          @click="selectLocation(r)"
        >
          <div class="min-w-0">
            <div class="text-ink font-semibold truncate">{{ r.name }}</div>
            <div class="text-ink-70 text-sm truncate">{{ r.address || '—' }}</div>
          </div>
          <div class="flex items-center gap-2 flex-shrink-0">
            <span v-if="r.id === auth.activeRestaurantId" class="tag-pill tag-pill-active">activ acum</span>
            <span v-else class="tag-pill">{{ r.isActive ? 'activ' : 'inactiv' }}</span>
            <span class="loc-arrow" aria-hidden="true">→</span>
          </div>
        </button>

        <form class="mt-4 flex flex-col md:flex-row gap-2" @submit.prevent="createRestaurant">
          <input v-model="newRestaurantName" placeholder="Nume locație nouă" class="org-input flex-1" required />
          <input v-model="newRestaurantAddress" placeholder="Adresă (opțional)" class="org-input flex-1" />
          <button type="submit" class="pill-btn" :disabled="busy">Adaugă locație</button>
        </form>
      </section>

      <!-- Invitations -->
      <section class="card-paper p-6">
        <div class="eyebrow mb-2">Co-proprietari</div>
        <h2 class="display-title text-3xl mb-4">Invitații</h2>

        <form class="flex flex-col md:flex-row gap-2 mb-4" @submit.prevent="sendInvite">
          <input v-model="inviteEmail" type="email" placeholder="email@exemplu.ro" class="org-input flex-1" required />
          <select v-model="inviteRestaurantId" class="org-input">
            <option v-for="r in restaurants" :key="r.id" :value="r.id">{{ r.name }}</option>
          </select>
          <button type="submit" class="pill-btn" :disabled="busy">Invită</button>
        </form>

        <div v-if="!invitations.length" class="card-white p-4 text-ink italic text-base">
          Nicio invitație încă.
        </div>
        <div
          v-for="i in invitations"
          :key="i.id"
          class="card-white p-4 mb-2 flex items-center justify-between gap-3"
        >
          <div class="min-w-0">
            <div class="text-ink font-semibold truncate">{{ i.email }}</div>
            <div class="text-ink-70 text-sm">{{ i.status }} · expiră {{ new Date(i.expiresAt).toLocaleDateString() }}</div>
          </div>
          <div class="flex gap-2 flex-shrink-0">
            <button
              v-if="i.status === 'pending'"
              type="button"
              class="pill-btn pill-btn-ghost"
              @click="copyLink(i)"
            >
              Copiază link
            </button>
            <button
              v-if="i.status === 'pending'"
              type="button"
              class="pill-btn pill-btn-danger"
              @click="revoke(i)"
            >
              Revocă
            </button>
          </div>
        </div>
      </section>
    </template>
  </div>
</template>

<style scoped>
.org-input {
  padding: 12px 14px;
  border-radius: 12px;
  border: 1px solid var(--ink12, rgba(0, 0, 0, 0.12));
  background: var(--card, #fff);
  color: var(--ink, #15140f);
  font-size: 15px;
}

/* Clickable location card — reuses .card-white, resets native <button> chrome */
.loc-card {
  font: inherit;
  color: inherit;
  cursor: pointer;
  border: 1px solid var(--ink12, rgba(0, 0, 0, 0.12));
  transition: border-color 0.18s ease, box-shadow 0.18s ease, transform 0.18s ease;
}
.loc-card:hover {
  border-color: var(--ink);
  box-shadow: var(--shadow-soft);
  transform: translateY(-1px);
}
.loc-card-active {
  border-color: var(--ink);
  box-shadow: var(--shadow-soft);
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
.tag-pill-active {
  background: var(--ink);
  color: var(--paperHi);
}
</style>
