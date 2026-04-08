<script setup lang="ts">

import { computed } from 'vue'

const props = defineProps<{
    metrics: any
    isLoading: boolean
}>()

const claimRate = computed(() => {
    if(!props.metrics || !props.metrics.flashDealsShown) return 0

    const rate = (props.metrics.flashDealsClaimed / props.metrics.flashDealsShow) * 100;
    return rate.toFixed(1);
})


</script>

<template>
  <div class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6 mb-8">
    
    <div class="bg-white rounded-xl shadow-sm border border-gray-100 p-6 flex flex-col">
      <h3 class="text-sm font-semibold text-gray-500 uppercase tracking-wider mb-2">Oferte Afișate Azi</h3>
      <div v-if="isLoading && !metrics" class="animate-pulse h-8 bg-gray-200 rounded w-1/3"></div>
      <div v-else class="text-3xl font-black text-gray-800">
        {{ metrics?.flashDealsShown || 0 }}
      </div>
    </div>

    <div class="bg-white rounded-xl shadow-sm border border-gray-100 p-6 flex flex-col">
      <h3 class="text-sm font-semibold text-gray-500 uppercase tracking-wider mb-2">Oferte Revendicate</h3>
      <div v-if="isLoading && !metrics" class="animate-pulse h-8 bg-gray-200 rounded w-1/3"></div>
      <div v-else class="text-3xl font-black text-brand">
        {{ metrics?.flashDealsClaimed || 0 }}
      </div>
    </div>

    <div class="bg-white rounded-xl shadow-sm border border-gray-100 p-6 flex flex-col">
      <h3 class="text-sm font-semibold text-gray-500 uppercase tracking-wider mb-2">Rata de Conversie</h3>
      <div v-if="isLoading && !metrics" class="animate-pulse h-8 bg-gray-200 rounded w-1/3"></div>
      <div v-else class="text-3xl font-black text-green-500">
        {{ claimRate }}%
      </div>
    </div>

    <div class="bg-white rounded-xl shadow-sm border border-gray-100 p-6 flex flex-col">
      <h3 class="text-sm font-semibold text-gray-500 uppercase tracking-wider mb-2">Venit Extra (AI Upsell)</h3>
      <div v-if="isLoading && !metrics" class="animate-pulse h-8 bg-gray-200 rounded w-1/3"></div>
      <div v-else class="text-3xl font-black text-gray-800">
        {{ metrics?.upsellRevenue || 0 }} <span class="text-lg text-gray-500 font-medium">RON</span>
      </div>
    </div>

    <div class="bg-white rounded-xl shadow-sm border border-gray-100 p-6 flex flex-col">
      <h3 class="text-sm font-semibold text-gray-500 uppercase tracking-wider mb-2">Venit Oferte Fulger</h3>
      <div v-if="isLoading && !metrics" class="animate-pulse h-8 bg-gray-200 rounded w-1/3"></div>
      <div v-else class="text-3xl font-black text-gray-800">
        {{ metrics?.flashDealRevenue || 0 }} <span class="text-lg text-gray-500 font-medium">RON</span>
      </div>
    </div>

  </div>
</template>
