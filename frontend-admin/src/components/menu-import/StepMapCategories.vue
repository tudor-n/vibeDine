<script setup lang="ts">
import { onMounted } from 'vue'

import { useMenuImportStore, type MappedCategory } from '@/stores/useMenuImportStore'
import { useMenuManagementStore } from '@/stores/useMenuManagementStore'

const importStore = useMenuImportStore()
const menuStore = useMenuManagementStore()

onMounted(async () => {
  if (menuStore.categories.length === 0) await menuStore.fetchMenu()
  importStore.autoMatch(menuStore.categories.map((c) => ({ id: c.id, name: c.name })))
})

function onMappingChange(hint: string, raw: string) {
  if (raw.startsWith('existing:')) {
    importStore.setMapping(hint, { kind: 'existing', categoryId: raw.slice('existing:'.length) })
  } else {
    importStore.setMapping(hint, { kind: 'new', tempId: crypto.randomUUID(), name: hint })
  }
}

function onUnassignedChange(raw: string) {
  if (raw.startsWith('existing:')) {
    importStore.setUnassignedMapping({ kind: 'existing', categoryId: raw.slice('existing:'.length) })
  } else if (raw.startsWith('new:')) {
    const name = raw.slice('new:'.length)
    importStore.setUnassignedMapping({ kind: 'new', tempId: crypto.randomUUID(), name })
  }
}

function selectValueFor(mapping: MappedCategory | undefined | null, hint: string | null): string {
  if (!mapping) return hint ? `new:${hint}` : ''
  if (mapping.kind === 'existing') return `existing:${mapping.categoryId}`
  return `new:${mapping.name}`
}
</script>

<template>
  <section>
    <div class="eyebrow mb-3">pasul 2 · categorii</div>
    <h2 class="display-title text-2xl mb-2">Asociază secțiunile detectate</h2>
    <p class="text-ink-70 text-sm mb-5">
      Folosim titlurile de secțiune pe care le-am citit de pe meniu. Le poți redirecționa
      către categorii existente sau le creăm noi la importul final.
    </p>

    <div v-if="importStore.detectedHints.length === 0 && importStore.unassignedCount === 0" class="card-paper p-5">
      <p class="text-ink-70 italic">Niciun titlu de secțiune detectat. Continuă spre revizuire.</p>
    </div>

    <ul v-else class="mapping-list">
      <li v-for="hint in importStore.detectedHints" :key="hint" class="mapping-row card-white">
        <div class="hint-block">
          <span class="eyebrow">detectat</span>
          <span class="hint-name">„{{ hint }}"</span>
        </div>
        <div class="select-block">
          <label class="field-label">Mapează la</label>
          <select
            class="field-input"
            :value="selectValueFor(importStore.categoryMapping[hint], hint)"
            @change="(e) => onMappingChange(hint, (e.target as HTMLSelectElement).value)"
          >
            <optgroup label="Existente">
              <option
                v-for="cat in menuStore.categories"
                :key="cat.id"
                :value="`existing:${cat.id}`"
              >
                {{ cat.name }}
              </option>
            </optgroup>
            <optgroup label="Crează nouă">
              <option :value="`new:${hint}`">+ „{{ hint }}" (nouă)</option>
            </optgroup>
          </select>
        </div>
      </li>

      <li v-if="importStore.unassignedCount > 0" class="mapping-row card-white">
        <div class="hint-block">
          <span class="eyebrow">fără heading</span>
          <span class="hint-name">{{ importStore.unassignedCount }} produse</span>
        </div>
        <div class="select-block">
          <label class="field-label">Mapează la</label>
          <select
            class="field-input"
            :value="selectValueFor(importStore.unassignedMapping, null)"
            @change="(e) => onUnassignedChange((e.target as HTMLSelectElement).value)"
          >
            <option value="" disabled>— alege categoria —</option>
            <optgroup label="Existente">
              <option
                v-for="cat in menuStore.categories"
                :key="cat.id"
                :value="`existing:${cat.id}`"
              >
                {{ cat.name }}
              </option>
            </optgroup>
            <optgroup label="Crează nouă">
              <option value="new:Nesortate">+ „Nesortate" (nouă)</option>
            </optgroup>
          </select>
        </div>
      </li>
    </ul>

    <div class="footer-row">
      <button class="pill-btn pill-btn-ghost" type="button" @click="importStore.$patch({ step: 1 })">
        Înapoi
      </button>
      <button
        class="pill-btn"
        type="button"
        :disabled="!importStore.mappingComplete"
        @click="importStore.advanceToReview()"
      >
        Continuă la revizuire
      </button>
    </div>
  </section>
</template>

<style scoped>
.mapping-list { list-style: none; padding: 0; display: grid; gap: 12px; }
.mapping-row {
  padding: 16px;
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(0, 1.6fr);
  gap: 16px;
  align-items: center;
}
.hint-block { display: flex; flex-direction: column; gap: 4px; }
.hint-name { font-family: 'Playfair Display', serif; font-size: 18px; }
.select-block { display: flex; flex-direction: column; gap: 6px; }
.footer-row { margin-top: 28px; display: flex; justify-content: space-between; }
</style>
