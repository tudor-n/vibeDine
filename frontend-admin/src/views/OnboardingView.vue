<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/useAuthStore'
import { orgApi } from '@/services/api-client'

const auth = useAuthStore()
const router = useRouter()

const orgName = ref('')
const restaurantName = ref('')
const address = ref('')
const error = ref<string | null>(null)
const busy = ref(false)

async function submit() {
  error.value = null
  busy.value = true
  try {
    const org = await orgApi.createOrganization(orgName.value.trim())
    await orgApi.createRestaurant(org.id, {
      name: restaurantName.value.trim(),
      address: address.value.trim() || undefined,
    })
    // Refresh cached org/restaurant lists, then route through the picker.
    // The picker is the canonical landing page; routing through it keeps
    // first-time and returning UX consistent.
    await auth.loadAccessibleOrganizations()
    auth.clearActiveOrganization()
    router.replace('/select-organization')
  } catch (e) {
    error.value = e instanceof Error ? e.message : 'Nu am putut crea organizația.'
  } finally {
    busy.value = false
  }
}

function signOut() {
  auth.logout()
  router.replace('/login')
}
</script>

<template>
  <div class="min-h-screen grid place-items-center bg-paper text-ink p-6">
    <div class="w-full max-w-md">
      <div class="eyebrow mb-2">PRIMUL PAS</div>
      <h1 class="display-title text-3xl mb-2">Creează organizația</h1>
      <p class="text-ink-70 mb-6 text-sm">
        Bun venit, {{ auth.user?.fullName }}. Creează lanțul tău și prima locație ca să începi.
      </p>

      <form class="flex flex-col gap-3" @submit.prevent="submit">
        <label class="text-sm font-semibold">Numele organizației (lanț)</label>
        <input v-model="orgName" required placeholder="ex. Bistro Group" class="auth-input" />

        <label class="text-sm font-semibold mt-2">Numele primei locații</label>
        <input v-model="restaurantName" required placeholder="ex. Bistro Centru" class="auth-input" />

        <label class="text-sm font-semibold mt-2">Adresă (opțional)</label>
        <input v-model="address" placeholder="Str. Exemplu 1" class="auth-input" />

        <p v-if="error" class="text-sm text-red-600">{{ error }}</p>

        <button type="submit" :disabled="busy" class="auth-btn mt-2">
          {{ busy ? 'Se creează…' : 'Creează și continuă' }}
        </button>
      </form>

      <button class="mt-4 text-sm underline text-ink-70" type="button" @click="signOut">
        Deconectare
      </button>
    </div>
  </div>
</template>

<style scoped>
.auth-input {
  width: 100%;
  padding: 12px 14px;
  border-radius: 12px;
  border: 1px solid var(--ink12, rgba(0, 0, 0, 0.12));
  background: var(--card, #fff);
  color: var(--ink, #15140f);
  font-size: 15px;
}
.auth-btn {
  width: 100%;
  padding: 12px 14px;
  border-radius: 999px;
  background: var(--ink, #15140f);
  color: var(--paperHi, #fff);
  font-weight: 600;
  cursor: pointer;
}
.auth-btn:disabled {
  opacity: 0.6;
  cursor: progress;
}
</style>
