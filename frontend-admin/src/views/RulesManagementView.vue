<script setup lang="ts">

import { onMounted } from 'vue'
import { useSimilarityRulesStore } from '@/stores/useSimilarityRulesStore'
import SimilarityRuleTable from '@/components/SimilarityRuleTable.vue'

const rulesStore = useSimilarityRulesStore();

onMounted(() => {
    rulesStore.fetchRules();
})


const handleDeleteRule = async (id: string) => {
    if (confirm('Ești sigur că vrei să ștergi această regulă de recomandare?')) {
        try{
            await rulesStore.deleteRule(id);
        }catch(error){
            alert('Eroare la ștergerea regulii.');
        }
    }
}
</script>



<template>
  <div class="p-6 max-w-6xl mx-auto">
    
    <header class="mb-8 flex flex-col md:flex-row md:justify-between md:items-center gap-4">
      <div>
        <h1 class="text-3xl font-bold text-gray-900">Reguli Recomandări</h1>
        <p class="text-gray-500 mt-1">Gestionează legăturile dintre produse pentru cross-sell și upsell.</p>
      </div>
      
      <button 
        class="bg-gray-900 text-white px-5 py-2.5 rounded-lg shadow-sm font-semibold hover:bg-gray-800 transition-colors flex items-center justify-center gap-2"
        @click="alert('Funcționalitatea de adăugare manuală a regulilor va fi disponibilă în curând!')"
      >
        <span>+</span> Adaugă Regulă Manuală
      </button>
    </header>

    <div v-if="rulesStore.error" class="bg-red-50 border border-red-200 text-red-700 p-4 mb-6 rounded-lg">
      {{ rulesStore.error }}
    </div>

    <SimilarityRuleTable 
      :rules="rulesStore.rules" 
      :is-loading="rulesStore.isLoading"
      @delete="handleDeleteRule"
    />

  </div>
</template>