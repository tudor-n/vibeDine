<script setup lang="ts">

import { onMounted } from 'vue'
import { useDirectivesStore } from '@/stores/useDirectivesStore'
import { useToast } from '@/composables/useToast'
import DailyPromptInput from '@/components/DailyPromptInput.vue'

const directivesStore = useDirectivesStore();
const toast = useToast();

onMounted(() => {
    directivesStore.fetchDirectives();
})

const handleNewPrompt = async (prompt: string) => {
    try{
        await directivesStore.postDirective(prompt);
        toast.success('Directiva a fost salvată și regulile au fost generate cu succes.');
    }catch(error){
        toast.error('A apărut o eroare la salvarea directivei.');
    }
}

</script>

<template>
  <div class="max-w-4xl mx-auto relative">

    <!-- Status row -->
    <div class="flex items-center justify-between mb-8">
      <div class="tag-pill">
        <span class="live-dot" style="background: var(--lime); box-shadow: 0 0 10px var(--lime);"></span>
        <span>brain · gpt</span>
      </div>
      <div class="tag-pill">module · upsell</div>
    </div>

    <header class="mb-10">
      <div class="eyebrow mb-4">Astăzi · directiva chefului</div>
      <h1 class="display-title text-[56px] md:text-[68px]">
        Spune AI-ului<br />
        <span class="text-accent">ce vinzi.</span>
      </h1>
      <p class="text-ink-70 text-lg mt-5 leading-relaxed max-w-2xl">
        Scrieți o frază — AI-ul transformă intenția în reguli concrete de
        cross-sell și upsell pentru fiecare comandă din sală.
      </p>
    </header>

    <DailyPromptInput
      :is-loading="directivesStore.isLoading"
      @submit="handleNewPrompt"
    />

    <!-- Active directive -->
    <div class="card-paper p-7 relative overflow-hidden">
      <div class="bloom bloom-lime" style="top: -60px; right: -60px; width: 220px; height: 220px; opacity: 0.22;" />
      <div class="relative z-10">
        <div class="flex items-center justify-between mb-5">
          <div>
            <div class="eyebrow mb-1">Directivă · activă acum</div>
            <h3 class="display-title text-2xl">Ce ascultă AI-ul</h3>
          </div>
          <span class="tag-pill" :style="directivesStore.activeDirective ? 'background: var(--lime); color: var(--onLime); border-color: var(--ink12);' : ''">
            <span v-if="directivesStore.activeDirective" class="w-2 h-2 rounded-full bg-ink"></span>
            {{ directivesStore.activeDirective ? 'live' : 'inactiv' }}
          </span>
        </div>

        <div v-if="directivesStore.isLoading && directivesStore.directives.length === 0" class="space-y-3">
          <div class="h-4 bg-ink-12 rounded-full w-3/4 animate-pulse"></div>
          <div class="h-4 bg-ink-12 rounded-full w-1/2 animate-pulse"></div>
        </div>

        <blockquote
          v-else-if="directivesStore.activeDirective"
          class="card-white p-6 relative"
        >
          <span class="absolute -top-3 left-6 tag-pill" style="background: var(--ink); color: var(--lime); border-color: var(--ink);">prompt</span>
          <p class="text-ink text-lg leading-relaxed font-medium pt-1">
            "{{ directivesStore.activeDirective.rawPrompt }}"
          </p>
        </blockquote>

        <div v-else class="card-white p-6 text-ink-70 italic">
          Nicio directivă AI nu este activă momentan. Sistemul rulează pe recomandări
          standard (bazate pe categorie).
        </div>
      </div>
    </div>
  </div>
</template>
