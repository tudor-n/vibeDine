<script setup lang="ts">

import { onMounted } from 'vue'
import { useDirectivesStore } from '@/stores/useDirectivesStore'
import DailyPromptInput from '@/components/DailyPromptInput.vue'

const directivesStore = useDirectivesStore();

onMounted(() => {
    directivesStore.fetchDirectives();
})

const handleNewPrompt = async (prompt: string) => {
    try{
        await directivesStore.postDirective(prompt);
        alert('Directiva a fost salvată și regulile au fost generate cu succes!');
    }catch(error){
        alert('A apărut o eroare la salvarea directivei.');
    }
}

</script>

<template>
  <div class="p-6 max-w-4xl mx-auto">
    <header class="mb-8">
      <h1 class="text-3xl font-bold text-gray-900">Configurare AI</h1>
      <p class="text-gray-500 mt-1">Gestionează comportamentul de recomandare și vânzare (upsell).</p>
    </header>

    <DailyPromptInput 
      :is-loading="directivesStore.isLoading" 
      @submit="handleNewPrompt" 
    />

    <div class="bg-gray-50 rounded-xl border border-gray-200 p-6">
      <h3 class="text-lg font-bold text-gray-700 mb-4 flex items-center gap-2">
        <span class="w-3 h-3 rounded-full bg-green-500"></span>
        Directiva Activă în acest moment
      </h3>

      <div v-if="directivesStore.isLoading && directivesStore.directives.length === 0" class="animate-pulse">
        <div class="h-4 bg-gray-200 rounded w-3/4 mb-2"></div>
        <div class="h-4 bg-gray-200 rounded w-1/2"></div>
      </div>
      
      <div v-else-if="directivesStore.activeDirective" class="bg-white p-4 rounded border border-gray-100 shadow-sm text-gray-800 italic">
        "{{ directivesStore.activeDirective.rawPrompt }}"
      </div>
      
      <div v-else class="text-gray-500 italic">
        Nicio directivă AI nu este activă momentan. Sistemul rulează pe recomandări standard (bazate pe categorie).
      </div>
    </div>
  </div>
</template>