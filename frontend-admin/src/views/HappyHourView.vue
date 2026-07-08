<script setup lang="ts">
import { onMounted, computed } from 'vue'
import { useHappyHourStore, type HappyHourPayload } from '@/stores/useHappyHourStore'
import { useToast } from '@/composables/useToast'
import HappyHourForm from '@/components/HappyHourForm.vue'

const store = useHappyHourStore()
const toast = useToast()

onMounted(() => {
  store.fetchConfig()
})

async function handleSave(payload: HappyHourPayload) {
  try {
    await store.save(payload)
    toast.success('Happy hour salvat. Oferta va porni automat în interval.')
  } catch {
    toast.error('A apărut o eroare la salvarea happy hour.')
  }
}

async function handleClear() {
  try {
    await store.clear()
    toast.success('Happy hour dezactivat.')
  } catch {
    toast.error('A apărut o eroare la ștergerea happy hour.')
  }
}

// Best-effort "active now" badge using the browser clock. The authoritative
// check runs server-side in the restaurant's timezone (see FlashDealService);
// this is only a display hint and can differ if the admin is in another tz.
const isLiveNow = computed(() => {
  const c = store.config
  if (!c || !c.isEnabled) return false
  const now = new Date()
  const cur = now.getHours() * 60 + now.getMinutes()
  const [sh, sm] = c.startTime.split(':').map(Number)
  const [eh, em] = c.endTime.split(':').map(Number)
  const start = sh * 60 + sm
  const end = eh * 60 + em
  if (start === end) return false
  return start < end ? cur >= start && cur < end : cur >= start || cur < end
})

const isOvernight = computed(() => {
  const c = store.config
  return !!c && c.endTime <= c.startTime
})
</script>

<template>
  <div class="max-w-4xl mx-auto relative">
    <!-- Status row -->
    <div class="flex items-center justify-between mb-8">
      <div class="tag-pill" role="status" aria-label="Happy hour">
        <span class="live-dot" style="background: var(--lime); box-shadow: 0 0 10px var(--lime);" aria-hidden="true"></span>
        <span>Happy hour</span>
      </div>
      <div class="tag-pill" aria-label="Ofertă automată">ofertă automată zilnică</div>
    </div>

    <header class="mb-10">
      <div class="eyebrow mb-4">Promoție programată</div>
      <h1 class="display-title text-[44px] md:text-[56px]">
        Oferă un produs<br />
        <span class="text-accent">la oră fixă.</span>
      </h1>
      <p class="text-ink text-lg mt-5 leading-relaxed max-w-2xl">
        Setează o singură dată produsul, reducerea și intervalul. Sistemul oferă
        automat reducerea fiecărui client care comandă în acel interval — fără AI,
        fără să rescrii nimic în fiecare zi.
      </p>
    </header>

    <HappyHourForm
      :config="store.config"
      :is-loading="store.isLoading"
      @save="handleSave"
      @clear="handleClear"
    />

    <!-- Current config -->
    <div class="card-paper p-7 relative overflow-hidden">
      <div class="bloom bloom-lime" style="top: -60px; right: -60px; width: 220px; height: 220px; opacity: 0.16;" />
      <div class="relative z-10">
        <div class="flex items-center justify-between mb-5">
          <div>
            <div class="eyebrow mb-1">Happy hour configurat</div>
            <h3 class="display-title text-2xl">Ce rulează acum</h3>
          </div>
          <span
            v-if="store.config"
            class="tag-pill"
            :style="isLiveNow ? 'background: var(--lime); color: var(--onLime); border-color: var(--ink12);' : ''"
            :aria-label="isLiveNow ? 'Activ acum' : (store.config.isEnabled ? 'Programat' : 'Pe pauză')"
          >
            <span v-if="isLiveNow" class="w-2 h-2 rounded-full bg-ink" aria-hidden="true"></span>
            {{ isLiveNow ? 'în direct acum' : (store.config.isEnabled ? 'programat' : 'pe pauză') }}
          </span>
        </div>

        <div v-if="store.isLoading && !store.config" class="space-y-3">
          <div class="h-4 bg-ink-12 rounded-full w-3/4 animate-pulse"></div>
          <div class="h-4 bg-ink-12 rounded-full w-1/2 animate-pulse"></div>
        </div>

        <div v-else-if="store.config" class="card-white p-6 relative" aria-label="Happy hour activ">
          <span class="absolute -top-3 left-6 tag-pill" style="background: var(--ink); color: var(--lime); border-color: var(--ink);" aria-hidden="true">happy hour</span>
          <div class="pt-1 flex flex-wrap items-baseline gap-x-3 gap-y-1">
            <span class="text-ink text-2xl font-bold">{{ store.config.productName ?? 'Produs' }}</span>
            <span class="text-accent text-2xl font-black">−{{ store.config.discountPercent }}%</span>
          </div>
          <p class="text-ink text-lg leading-relaxed font-medium mt-2">
            În fiecare zi, între
            <strong>{{ store.config.startTime }}</strong> și
            <strong>{{ store.config.endTime }}</strong
            ><template v-if="isOvernight"> (a doua zi)</template>.
          </p>
        </div>

        <div v-else class="card-white p-6 text-ink italic text-base">
          Niciun happy hour nu este configurat. Adaugă unul mai sus pentru a oferi
          automat o reducere într-un interval orar.
        </div>
      </div>
    </div>
  </div>
</template>
