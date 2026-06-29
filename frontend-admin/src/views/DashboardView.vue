<script setup lang="ts">

import { onMounted, onUnmounted, computed } from 'vue'
import { RouterLink } from 'vue-router'
import { useMetricsStore } from '@/stores/useMetricsStore';
import { useInventoryStore } from '@/stores/useInventoryStore'
import { useToast } from '@/composables/useToast'
import PerformanceMetrics from '@/components/PerformanceMetrics.vue';
import type { AdminIngredient } from '@shared/types/models'

const metricsStore = useMetricsStore();
const inventoryStore = useInventoryStore()
const toast = useToast()

onMounted(() => {
    metricsStore.startPolling();
    inventoryStore.fetchIngredients()
})


onUnmounted(() => {
    metricsStore.stopPolling();
})

const clockLabel = computed(() => {
  const d = new Date()
  return `${d.getHours().toString().padStart(2, '0')}:${d.getMinutes().toString().padStart(2, '0')}`
})

const UNIT_LABELS: Record<string, string> = {
  piece: 'buc', gram: 'g', kilogram: 'kg', milliliter: 'ml', liter: 'l',
}
function unitLabel(u: string) {
  return UNIT_LABELS[u] ?? u
}
const qtyFormatter = new Intl.NumberFormat('ro-RO', { maximumFractionDigits: 3 })
function formatQty(n: number) {
  return qtyFormatter.format(n)
}

async function quickRestock(i: AdminIngredient) {
  const input = window.prompt(`Cât adaugi în stoc pentru „${i.name}" (${unitLabel(i.unit)})?`, '')
  if (input === null) return
  const delta = Number(input.trim().replace(',', '.'))
  if (!Number.isFinite(delta) || delta === 0) {
    toast.error('Introdu o cantitate validă.')
    return
  }
  try {
    await inventoryStore.adjustIngredient(i.id, { delta })
    toast.success(`Stoc actualizat pentru „${i.name}".`)
  } catch (e: any) {
    toast.error(e?.message ?? 'Eroare la actualizarea stocului.')
  }
}
</script>


<template>
  <div class="max-w-7xl mx-auto relative">

    <!-- Status row, Clarifi-style -->
    <div class="flex items-center justify-between mb-8">
      <div class="tag-pill" role="status" aria-live="polite">
        <span class="live-dot" aria-hidden="true"></span>
        <span>{{ metricsStore.isLoading ? 'Se sincronizează…' : 'Activ în sala 1' }}</span>
      </div>
      <div class="tag-pill" :aria-label="`Ora curentă ${clockLabel}`">{{ clockLabel }} · azi</div>
    </div>

    <!-- Hero -->
    <header class="mb-10 max-w-3xl">
      <div class="eyebrow mb-4">Astăzi în restaurant</div>
      <h1 class="display-title text-[44px] md:text-[56px]">
        Tablou de bord<br />
        <span class="text-accent">în direct.</span>
      </h1>
      <p class="text-ink text-lg mt-5 leading-relaxed">
        Urmăriți performanța ofertelor fulger și a recomandărilor AI — în timp real,
        așa cum sunt servite la mesele din sală.
      </p>
    </header>

    <!-- Error -->
    <div
      v-if="metricsStore.error"
      class="card-paper p-5 mb-8 flex items-start gap-4"
      style="border-color: var(--accentBorder); background: var(--accentSoft);"
      role="alert"
    >
      <span class="font-mono text-sm uppercase tracking-[0.06em] font-bold mt-0.5" style="color: var(--accentDk);">
        <span aria-hidden="true">⚠</span> Eroare
      </span>
      <p class="text-base text-ink flex-1">{{ metricsStore.error }}</p>
    </div>

    <!-- Section: KPIs -->
    <div class="flex items-end justify-between mb-5">
      <div>
        <div class="eyebrow mb-1">Indicatori</div>
        <h2 class="display-title text-2xl">Cum se vinde acum</h2>
      </div>
      <div class="hidden md:block tag-pill">
        {{ metricsStore.isLoading ? 'Se încarcă…' : 'Actualizat acum' }}
      </div>
    </div>

    <PerformanceMetrics
      :metrics="metricsStore.metrics"
      :is-loading="metricsStore.isLoading"
    />

    <!-- Low-stock alert -->
    <section v-if="inventoryStore.lowStockItems.length > 0" class="card-paper p-6 mt-8 low-card">
      <div class="flex items-end justify-between mb-4 gap-3 flex-wrap">
        <div>
          <div class="eyebrow mb-1">Atenție · stoc</div>
          <h2 class="display-title text-2xl">Stoc redus</h2>
        </div>
        <RouterLink to="/inventory" class="btn-ghost whitespace-nowrap">Vezi inventarul</RouterLink>
      </div>

      <div class="grid gap-2">
        <div
          v-for="i in inventoryStore.lowStockItems"
          :key="i.id"
          class="low-row card-white p-3"
        >
          <div class="flex-1 min-w-0">
            <div class="font-semibold text-ink truncate">
              {{ i.name }}
              <span v-if="i.linkedProductName" class="low-tag">produs</span>
            </div>
            <div class="low-meta font-mono">
              rămas {{ formatQty(i.quantityOnHand) }} {{ unitLabel(i.unit) }} · prag {{ formatQty(i.lowStockThreshold) }}
            </div>
          </div>
          <button class="btn-ghost text-sm whitespace-nowrap" type="button" @click="quickRestock(i)">
            + Stoc
          </button>
        </div>
      </div>
    </section>

    <!-- Activity card — hidden until implemented; flip v-if to true to restore -->
    <div v-if="false" class="card-paper p-8 mt-8 relative overflow-hidden">
      <div class="bloom bloom-lime" style="top: -40px; right: -40px; width: 200px; height: 200px; opacity: 0.18;" />
      <div class="relative z-10">
        <div class="eyebrow mb-3">În curând</div>
        <h3 class="display-title text-3xl mb-3">Activitate recentă</h3>
        <p class="text-ink leading-relaxed max-w-xl">
          Aici va apărea fluxul în direct al comenzilor, alături de reacțiile din sală la
          fiecare ofertă fulger pe care o trimiteți.
        </p>
        <div class="mt-6 flex items-center gap-3 flex-wrap">
          <span class="tag-pill">flux în direct</span>
          <span class="tag-pill">jurnal cronologic</span>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.low-card { border-color: var(--accentBorder); }
.low-row {
  display: flex;
  align-items: center;
  gap: 12px;
}
.low-meta {
  font-size: 11px;
  color: var(--ink40);
  margin-top: 2px;
}
.low-tag {
  display: inline-block;
  margin-left: 6px;
  padding: 1px 7px;
  border-radius: 999px;
  background: var(--ink06);
  color: var(--ink70);
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 10px;
  text-transform: uppercase;
  letter-spacing: 0.14em;
}
</style>
