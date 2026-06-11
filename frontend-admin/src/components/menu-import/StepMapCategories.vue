<script setup lang="ts">
import { computed, onMounted } from 'vue'

import BrandSelect, { type BrandSelectGroup } from '@/components/BrandSelect.vue'
import { useMenuImportStore, type MappedCategory } from '@/stores/useMenuImportStore'
import { useMenuManagementStore } from '@/stores/useMenuManagementStore'

const importStore = useMenuImportStore()
const menuStore = useMenuManagementStore()

function buildHintGroups(hint: string): BrandSelectGroup[] {
  return [
    {
      label: 'Categorii existente',
      options: menuStore.categories.map((c) => ({ value: `existing:${c.id}`, label: c.name })),
    },
    {
      label: 'Categorie nouă',
      options: [{ value: `new:${hint}`, label: `+ Crează „${hint}"` }],
    },
  ]
}

const unassignedGroups = computed<BrandSelectGroup[]>(() => [
  {
    label: 'Categorii existente',
    options: menuStore.categories.map((c) => ({ value: `existing:${c.id}`, label: c.name })),
  },
  {
    label: 'Categorie nouă',
    options: [{ value: 'new:Nesortate', label: '+ Crează „Nesortate"' }],
  },
])

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

function mappingKind(mapping: MappedCategory | undefined | null): 'existing' | 'new' | null {
  if (!mapping) return null
  return mapping.kind
}

function itemCountForHint(hint: string): number {
  return importStore.extractedItems.filter((it) => it.categoryHint === hint).length
}

const totalToMap = computed(
  () => importStore.detectedHints.length + (importStore.unassignedCount > 0 ? 1 : 0),
)
</script>

<template>
  <section class="space-y-4">
    <div class="step-intro">
      <p class="text-ink-70 text-sm">
        Asociază secțiunile detectate cu categoriile tale existente, sau lasă-le să fie create automat la import.
      </p>
      <span class="field-label">{{ totalToMap }} de mapat</span>
    </div>

    <div v-if="importStore.detectedHints.length === 0 && importStore.unassignedCount === 0" class="alert">
      <span class="alert-icon" aria-hidden="true">ℹ</span>
      <div class="alert-body">Niciun titlu de secțiune detectat. Continuă spre revizuire.</div>
    </div>

    <ul v-else class="mapping-list">
      <li v-for="hint in importStore.detectedHints" :key="hint" class="mapping-row">
        <div class="hint-block">
          <span class="hint-badge" :class="`hint-badge-${mappingKind(importStore.categoryMapping[hint])}`">
            {{ mappingKind(importStore.categoryMapping[hint]) === 'existing' ? '⇢ existentă' : '+ nouă' }}
          </span>
          <span class="hint-name">{{ hint }}</span>
          <span class="hint-count">{{ itemCountForHint(hint) }} produse</span>
        </div>
        <BrandSelect
          class="mapping-select"
          :model-value="selectValueFor(importStore.categoryMapping[hint], hint)"
          :groups="buildHintGroups(hint)"
          :aria-label="`Mapează „${hint}”`"
          @update:model-value="(v) => onMappingChange(hint, v)"
        />
      </li>

      <li v-if="importStore.unassignedCount > 0" class="mapping-row">
        <div class="hint-block">
          <span class="hint-badge hint-badge-orphan">fără heading</span>
          <span class="hint-name">{{ importStore.unassignedCount }} produse fără secțiune</span>
          <span class="hint-count">trebuie redirecționate</span>
        </div>
        <BrandSelect
          class="mapping-select"
          :model-value="selectValueFor(importStore.unassignedMapping, null)"
          :groups="unassignedGroups"
          placeholder="— alege categoria —"
          aria-label="Mapează produsele fără secțiune"
          @update:model-value="onUnassignedChange"
        />
      </li>
    </ul>

    <div class="form-footer">
      <button class="btn-ghost" type="button" @click="importStore.$patch({ step: 1 })">
        ← Înapoi
      </button>
      <button
        class="pill-btn"
        type="button"
        :disabled="!importStore.mappingComplete"
        @click="importStore.advanceToReview()"
      >
        Continuă la revizuire →
      </button>
    </div>
  </section>
</template>

<style scoped>
.step-intro {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  flex-wrap: wrap;
}
.mapping-list { list-style: none; padding: 0; display: grid; gap: 8px; margin: 0; }
.mapping-row {
  padding: 14px 16px;
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(0, 1.4fr);
  gap: 16px;
  align-items: center;
  background: var(--paperHi);
  border: 1px solid var(--ink12);
  border-radius: 14px;
  transition: border-color 0.15s ease;
  min-width: 0;
}
.mapping-row:hover { border-color: var(--ink40); }

.hint-block {
  display: grid;
  grid-template-columns: auto minmax(0, 1fr);
  grid-template-areas: 'badge name' 'badge count';
  column-gap: 10px;
  row-gap: 1px;
  align-items: center;
  min-width: 0;
}
.hint-badge {
  grid-area: badge;
  display: inline-flex;
  align-items: center;
  padding: 4px 10px;
  border-radius: 999px;
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 10px;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.12em;
  white-space: nowrap;
}
.hint-badge-existing {
  background: rgba(217, 245, 74, 0.25);
  color: var(--onLime);
  border: 1px solid rgba(217, 245, 74, 0.5);
}
.hint-badge-new {
  background: var(--accentSoft);
  color: var(--accent);
  border: 1px solid var(--accentBorder);
}
.hint-badge-orphan {
  background: var(--ink06);
  color: var(--ink70);
  border: 1px solid var(--ink12);
}
.hint-name {
  grid-area: name;
  font-size: 15px;
  font-weight: 600;
  color: var(--ink);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  min-width: 0;
}
.hint-count {
  grid-area: count;
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 11px;
  color: var(--ink40);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  min-width: 0;
}
.mapping-select {
  background: var(--paper);
  min-width: 0;
  max-width: 100%;
}

@media (max-width: 640px) {
  .mapping-row {
    grid-template-columns: 1fr;
    gap: 10px;
  }
}
</style>
