<script setup lang="ts">

import type { SimilarityRule } from '@shared/types/models';

defineProps<{
    rules: SimilarityRule[]
    isLoading: boolean
}>();

const emit = defineEmits<{
    (e: 'delete', id: string): void;
}>();

</script>


<template>
  <div class="bg-white rounded-xl shadow-sm border border-gray-100 overflow-hidden">
    
    <div v-if="isLoading && rules.length === 0" class="p-8 text-center text-gray-500">
      <span class="animate-pulse flex items-center justify-center gap-2">
        <span class="text-2xl">↻</span> Se încarcă regulile...
      </span>
    </div>

    <div v-else-if="rules.length === 0" class="p-8 text-center text-gray-500 italic">
      Nu există nicio regulă de recomandare momentan. 
      Mergi la "Configurare AI" pentru a genera reguli noi.
    </div>

    <div v-else class="overflow-x-auto">
      <table class="w-full text-left border-collapse">
        <thead>
          <tr class="bg-gray-50 text-gray-600 text-sm uppercase tracking-wider border-b border-gray-200">
            <th class="p-4 font-semibold">Când se adaugă...</th>
            <th class="p-4 font-semibold">Recomandăm...</th>
            <th class="p-4 font-semibold hidden md:table-cell">Motiv (Explicație AI)</th>
            <th class="p-4 font-semibold text-center">Scor / Tip</th>
            <th class="p-4 font-semibold text-right">Acțiuni</th>
          </tr>
        </thead>
        <tbody class="divide-y divide-gray-100">
          <tr v-for="rule in rules" :key="rule.id" class="hover:bg-gray-50 transition-colors">
            <td class="p-4 font-medium text-gray-900">
              {{ rule.source_item_id }} </td>
            
            <td class="p-4 font-medium text-brand">
              + {{ rule.target_item_id }}
            </td>
            
            <td class="p-4 text-sm text-gray-600 italic hidden md:table-cell max-w-xs truncate" :title="rule.reason">
              "{{ rule.reason }}"
            </td>
            
            <td class="p-4 text-center">
              <span class="px-2 py-1 bg-gray-100 text-xs font-bold rounded-lg text-gray-600 border border-gray-200">
                {{ (rule.score * 100).toFixed(0) }}% potrivire
              </span>
            </td>
            
            <td class="p-4 text-right">
              <button 
                @click="emit('delete', rule.id)"
                class="text-red-500 hover:text-red-700 bg-red-50 hover:bg-red-100 px-3 py-1.5 rounded text-sm font-semibold transition-colors"
              >
                Șterge
              </button>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>