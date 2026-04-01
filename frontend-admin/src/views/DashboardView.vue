<script setup lang="ts">

import { onMounted, onUnmounted } from 'vue'
import { useMetricsStore } from '@/stores/useMetricsStore';
import PerformanceMetrics from '@/components/PerformanceMetrics.vue';

const metricsStore = useMetricsStore();

onMounted(() => {
    metricsStore.startPolling();
})


onUnmounted(() => {
    metricsStore.stopPolling();
})
</script>


<template>
  <div class="p-6 max-w-7xl mx-auto">
    
    <header class="mb-8 flex justify-between items-center">
      <div>
        <h1 class="text-3xl font-bold text-gray-900">Dashboard Live</h1>
        <p class="text-gray-500 mt-1">Monitorizează performanța ofertelor și a recomandărilor AI.</p>
      </div>

      <div class="flex items-center gap-2 text-sm text-gray-500 bg-white px-4 py-2 rounded-full shadow-sm border border-gray-200">
        <span v-if="metricsStore.isLoading" class="flex h-3 w-3 relative">
          <span class="animate-ping absolute inline-flex h-full w-full rounded-full bg-brand opacity-75"></span>
          <span class="relative inline-flex rounded-full h-3 w-3 bg-brand"></span>
        </span>
        <span v-else class="h-3 w-3 rounded-full bg-green-500"></span>
        {{ metricsStore.isLoading ? 'Se actualizează...' : 'Date la zi' }}
      </div>
    </header>

    <div v-if="metricsStore.error" class="bg-red-50 border-l-4 border-red-500 p-4 mb-8 rounded-r-lg">
      <div class="flex">
        <div class="flex-shrink-0">
          <span class="text-red-500 font-bold">⚠ Eroare</span>
        </div>
        <div class="ml-3">
          <p class="text-sm text-red-700">{{ metricsStore.error }}</p>
        </div>
      </div>
    </div>

    <PerformanceMetrics 
      :metrics="metricsStore.metrics" 
      :is-loading="metricsStore.isLoading" 
    />

    <div class="bg-white rounded-xl shadow-sm border border-gray-100 p-6 h-64 flex items-center justify-center text-gray-400">
      <p>Aici va veni secțiunea de activitate recentă...</p>
    </div>

  </div>
</template>