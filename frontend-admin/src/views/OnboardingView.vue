<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/useAuthStore'
import { useBrandingStore } from '@/stores/useBrandingStore'
import { orgApi } from '@/services/api-client'
import BrandControls from '@/components/branding/BrandControls.vue'
import { BRAND_PRESETS, type BrandDraft } from '@/components/branding/presets'

const auth = useAuthStore()
const branding = useBrandingStore()
const router = useRouter()

const step = ref<1 | 2>(1)

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
    const restaurant = await orgApi.createRestaurant(org.id, {
      name: restaurantName.value.trim(),
      address: address.value.trim() || undefined,
    })
    // Refresh cached org/restaurant lists, then activate the restaurant we
    // just created so the brand step (and its restaurant-scoped API calls)
    // resolve against it.
    await auth.loadAccessibleOrganizations()
    auth.setActiveRestaurant(restaurant.id)
    step.value = 2
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

// ── Step 2: optional brand setup ─────────────────────────────────────────
const classicPreset = BRAND_PRESETS.find((p) => p.key === 'classic')!

const draft = ref<BrandDraft>({
  enabled: true,
  primary: classicPreset.primary,
  secondary: classicPreset.secondary,
  background: classicPreset.background,
  presetKey: classicPreset.key,
  logoUrl: null,
  photoUrl: null,
})

const brandError = ref<string | null>(null)
const finishing = ref(false)
const skipping = ref(false)

async function onUploadLogo(file: File) {
  try {
    const url = await branding.uploadLogo(file)
    draft.value = { ...draft.value, logoUrl: url }
  } catch (e) {
    brandError.value = e instanceof Error ? e.message : 'Nu am putut încărca logo-ul.'
  }
}

async function onUploadPhoto(file: File) {
  try {
    const url = await branding.uploadPhoto(file)
    draft.value = { ...draft.value, photoUrl: url }
  } catch (e) {
    brandError.value = e instanceof Error ? e.message : 'Nu am putut încărca fotografia.'
  }
}

async function finalize() {
  brandError.value = null
  finishing.value = true
  try {
    await branding.save({
      enabled: true,
      primary: draft.value.primary,
      secondary: draft.value.secondary,
      background: draft.value.background,
      presetKey: draft.value.presetKey,
    })
    router.replace('/select-organization')
  } catch (e) {
    brandError.value = e instanceof Error ? e.message : 'Nu am putut salva brandul.'
  } finally {
    finishing.value = false
  }
}

async function skipBranding() {
  brandError.value = null
  skipping.value = true
  try {
    await branding.save({
      enabled: false,
      primary: draft.value.primary,
      secondary: draft.value.secondary,
      background: draft.value.background,
      presetKey: draft.value.presetKey,
    })
    router.replace('/select-organization')
  } catch (e) {
    brandError.value = e instanceof Error ? e.message : 'Nu am putut sări peste acest pas.'
  } finally {
    skipping.value = false
  }
}
</script>

<template>
  <div v-if="step === 1" class="min-h-screen grid place-items-center bg-paper text-ink p-6">
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

  <div v-else class="min-h-screen grid place-items-center bg-paper text-ink p-6">
    <div class="w-full max-w-4xl">
      <div class="eyebrow mb-2">CONT NOU · PASUL 2</div>
      <h1 class="display-title text-3xl mb-2">Personalizează-ți brandul</h1>
      <p class="text-ink-70 mb-6 text-sm max-w-lg">
        Aceste alegeri se aplică peste tot: în tabloul de bord al echipei și pe ecranele clienților din
        sală. Poți sări peste acest pas și reveni oricând din Setări.
      </p>

      <BrandControls v-model="draft" @upload-logo="onUploadLogo" @upload-photo="onUploadPhoto" />

      <p v-if="brandError" class="text-sm text-red-600 mt-4">{{ brandError }}</p>

      <div class="flex items-center gap-3 mt-6">
        <button type="button" class="auth-btn" style="width: auto" :disabled="finishing || skipping" @click="finalize">
          {{ finishing ? 'Se salvează…' : 'Finalizează și intră în cont' }}
        </button>
        <button
          type="button"
          class="text-sm underline text-ink-70"
          :disabled="finishing || skipping"
          @click="skipBranding"
        >
          {{ skipping ? 'Se salvează…' : 'Sări peste' }}
        </button>
      </div>
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
