<script setup lang="ts">

import { onMounted, computed } from 'vue'
import { useSimilarityRulesStore } from '@/stores/useSimilarityRulesStore'
import SimilarityRuleTable from '@/components/SimilarityRuleTable.vue'

const rulesStore = useSimilarityRulesStore();

onMounted(() => {
    rulesStore.fetchRules();
})


const showAddRuleMessage = () => {
    window.alert('Funcționalitatea de adăugare manuală a regulilor va fi disponibilă în curând!');
}

const handleDeleteRule = async (id: string) => {
    if (window.confirm('Ești sigur că vrei să ștergi această regulă de recomandare?')) {
        try{
            await rulesStore.deleteRule(id);
        }catch(error){
            alert('Eroare la ștergerea regulii.');
        }
    }
}

const ruleCount = computed(() => rulesStore.rules?.length ?? 0)
</script>



<template>
  <div class="max-w-6xl mx-auto relative">

    <!-- Status row -->
    <div class="flex items-center justify-between mb-8">
      <div class="tag-pill">
        <span class="live-dot" style="background: var(--lime); box-shadow: 0 0 10px var(--lime);"></span>
        <span>cross-sell · graf</span>
      </div>
      <div class="tag-pill">{{ ruleCount }} reguli active</div>
    </div>

    <header class="mb-10 flex flex-col md:flex-row md:items-end md:justify-between gap-6">
      <div class="max-w-2xl">
        <div class="eyebrow mb-4">Recomandări · pebble pairing</div>
        <h1 class="display-title text-[56px] md:text-[68px]">
          Reguli<br />
          <span class="text-accent">recomandări.</span>
        </h1>
        <p class="text-ink-70 text-lg mt-5 leading-relaxed">
          Legăturile dintre produse care alimentează cross-sell-ul și upsell-ul
          în sală — fie generate de AI, fie scrise manual.
        </p>
      </div>

      <button
        class="pill-btn whitespace-nowrap"
        @click="showAddRuleMessage"
      >
        <span>+</span>
        <span>Adaugă regulă manuală</span>
      </button>
    </header>

    <div
      v-if="rulesStore.error"
      class="card-paper p-5 mb-6"
      style="border-color: var(--accentBorder); background: var(--accentSoft);"
    >
      <span class="font-mono text-xs uppercase tracking-[0.18em] text-accent font-bold">⚠ {{ rulesStore.error }}</span>
    </div>

    <SimilarityRuleTable
      :rules="rulesStore.rules"
      :is-loading="rulesStore.isLoading"
      @delete="handleDeleteRule"
    />

  </div>
</template>
