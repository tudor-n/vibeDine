<script setup lang="ts">
import { onMounted } from 'vue'
import { useFlashDealRuleStore, type FlashDealPayload } from '@/stores/useFlashDealRuleStore'
import { useToast } from '@/composables/useToast'
import FlashDealForm from '@/components/FlashDealForm.vue'

const store = useFlashDealRuleStore()
const toast = useToast()

onMounted(() => {
  store.fetchConfig()
})

async function handleSave(payload: FlashDealPayload) {
  try {
    await store.save(payload)
    toast.success('Flash deal salvat. Va apărea după fiecare comandă eligibilă.')
  } catch {
    toast.error('A apărut o eroare la salvarea flash deal.')
  }
}

async function handleClear() {
  try {
    await store.clear()
    toast.success('Flash deal dezactivat.')
  } catch {
    toast.error('A apărut o eroare la ștergerea flash deal.')
  }
}
</script>

<template>
  <div class="max-w-4xl mx-auto relative">
    <!-- Status row -->
    <div class="flex items-center justify-between mb-8">
      <div class="tag-pill" role="status" aria-label="Flash deal">
        <span class="live-dot" style="background: var(--lime); box-shadow: 0 0 10px var(--lime);" aria-hidden="true"></span>
        <span>Flash deal</span>
      </div>
      <div class="tag-pill" aria-label="Ofertă după comandă">ofertă-fulger după comandă</div>
    </div>

    <header class="mb-10">
      <div class="eyebrow mb-4">Ofertă post-comandă</div>
      <h1 class="display-title text-[44px] md:text-[56px]">
        Un impuls<br />
        <span class="text-accent">la final de comandă.</span>
      </h1>
      <p class="text-ink text-lg mt-5 leading-relaxed max-w-2xl">
        Alege produsul, reducerea și (opțional) o sumă minimă a notei. După ce
        clientul trimite comanda, primește o ofertă-fulger de 2 minute pentru acel
        produs — determinist, fără AI.
      </p>
    </header>

    <FlashDealForm
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
            <div class="eyebrow mb-1">Flash deal configurat</div>
            <h3 class="display-title text-2xl">Ce rulează acum</h3>
          </div>
          <span
            v-if="store.config"
            class="tag-pill"
            :style="store.config.isEnabled ? 'background: var(--lime); color: var(--onLime); border-color: var(--ink12);' : ''"
            :aria-label="store.config.isEnabled ? 'Activ' : 'Pe pauză'"
          >
            <span v-if="store.config.isEnabled" class="w-2 h-2 rounded-full bg-ink" aria-hidden="true"></span>
            {{ store.config.isEnabled ? 'activ' : 'pe pauză' }}
          </span>
        </div>

        <div v-if="store.isLoading && !store.config" class="space-y-3">
          <div class="h-4 bg-ink-12 rounded-full w-3/4 animate-pulse"></div>
          <div class="h-4 bg-ink-12 rounded-full w-1/2 animate-pulse"></div>
        </div>

        <div v-else-if="store.config" class="card-white p-6 relative" aria-label="Flash deal activ">
          <span class="absolute -top-3 left-6 tag-pill" style="background: var(--ink); color: var(--lime); border-color: var(--ink);" aria-hidden="true">flash deal</span>
          <div class="pt-1 flex flex-wrap items-baseline gap-x-3 gap-y-1">
            <span class="text-ink text-2xl font-bold">{{ store.config.productName ?? 'Produs' }}</span>
            <span class="text-accent text-2xl font-black">−{{ store.config.discountPercent }}%</span>
          </div>
          <p class="text-ink text-lg leading-relaxed font-medium mt-2">
            Apare după comandă, timp de 2 minute.
            <template v-if="store.config.minOrderTotal != null">
              Doar pentru note de peste <strong>{{ store.config.minOrderTotal }} lei</strong>.
            </template>
            <template v-else>Fără sumă minimă.</template>
          </p>
        </div>

        <div v-else class="card-white p-6 text-ink italic text-base">
          Niciun flash deal nu este configurat. Adaugă unul mai sus pentru a oferi
          automat o ofertă-fulger după comandă.
        </div>
      </div>
    </div>
  </div>
</template>
