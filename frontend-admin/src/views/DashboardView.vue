<script setup lang="ts">

import { onMounted, onUnmounted, computed } from 'vue'
import { useMetricsStore } from '@/stores/useMetricsStore';
import PerformanceMetrics from '@/components/PerformanceMetrics.vue';

const metricsStore = useMetricsStore();

onMounted(() => {
    metricsStore.startPolling();
})


onUnmounted(() => {
    metricsStore.stopPolling();
})

const clockLabel = computed(() => {
  const d = new Date()
  return `${d.getHours().toString().padStart(2, '0')}:${d.getMinutes().toString().padStart(2, '0')}`
})
</script>


<template>
  <div class="max-w-7xl mx-auto relative">

    <!-- Status row, Clarifi-style -->
    <div class="flex items-center justify-between mb-8">
      <div class="tag-pill">
        <span class="live-dot"></span>
        <span>{{ metricsStore.isLoading ? 'Sincronizare' : 'Live · sala 1' }}</span>
      </div>
      <div class="tag-pill">{{ clockLabel }} · azi</div>
    </div>

    <!-- Hero -->
    <header class="mb-10 max-w-3xl">
      <div class="eyebrow mb-4">Astăzi · pulsul casei</div>
      <h1 class="display-title text-[56px] md:text-[68px]">
        Dashboard<br />
        <span class="text-accent">live.</span>
      </h1>
      <p class="text-ink-70 text-lg mt-5 leading-relaxed">
        Monitorizați performanța ofertelor fulger și a recomandărilor AI — în timp real,
        așa cum sunt servite la mesele din sală.
      </p>
    </header>

    <!-- Error -->
    <div
      v-if="metricsStore.error"
      class="card-paper p-5 mb-8 flex items-start gap-4"
      style="border-color: var(--accentBorder); background: var(--accentSoft);"
    >
      <span class="font-mono text-xs uppercase tracking-[0.18em] text-accent font-bold mt-0.5">⚠ Eroare</span>
      <p class="text-sm text-ink flex-1">{{ metricsStore.error }}</p>
    </div>

    <!-- Section: KPIs -->
    <div class="flex items-end justify-between mb-5">
      <div>
        <div class="eyebrow mb-1">Indicatori</div>
        <h2 class="display-title text-2xl">Cum se vinde acum</h2>
      </div>
      <div class="hidden md:block tag-pill">
        actualizare · {{ metricsStore.isLoading ? '...' : 'just now' }}
      </div>
    </div>

    <PerformanceMetrics
      :metrics="metricsStore.metrics"
      :is-loading="metricsStore.isLoading"
    />

    <!-- Activity card -->
    <div class="card-paper p-8 mt-8 relative overflow-hidden">
      <div class="bloom bloom-lime" style="top: -40px; right: -40px; width: 200px; height: 200px; opacity: 0.25;" />
      <div class="relative z-10">
        <div class="eyebrow mb-3">În curând</div>
        <h3 class="display-title text-3xl mb-3">Activitate recentă</h3>
        <p class="text-ink-70 leading-relaxed max-w-xl">
          Aici va apărea fluxul live al comenzilor, alăturat reacțiilor în sală la
          fiecare ofertă fulger pe care o trimiteți.
        </p>
        <div class="mt-6 flex items-center gap-3">
          <span class="tag-pill">flux · websocket</span>
          <span class="tag-pill">jurnal · timeline</span>
        </div>
      </div>
    </div>
  </div>
</template>
