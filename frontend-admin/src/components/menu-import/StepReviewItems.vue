<script setup lang="ts">
import { computed } from 'vue'

import { useMenuImportStore } from '@/stores/useMenuImportStore'
import { useMenuManagementStore } from '@/stores/useMenuManagementStore'
import { MENU_VALIDATION } from '@shared/types/menu-validation'

const importStore = useMenuImportStore()
const menuStore = useMenuManagementStore()

const grouped = computed(() => {
  const buckets = new Map<string, typeof importStore.reviewItems>()
  for (const item of importStore.reviewItems) {
    const key = labelFor(item.resolvedCategory)
    if (!buckets.has(key)) buckets.set(key, [])
    buckets.get(key)!.push(item)
  }
  return Array.from(buckets.entries())
})

function labelFor(mapping: typeof importStore.reviewItems[0]['resolvedCategory']) {
  if (mapping.kind === 'existing') {
    const cat = menuStore.categories.find((c) => c.id === mapping.categoryId)
    return cat?.name ?? 'Necunoscut'
  }
  return `${mapping.name} (nouă)`
}

function rowError(item: typeof importStore.reviewItems[0]) {
  if (item.skipped) return null
  const r = importStore.validateRow(item)
  if (r.ok) return null
  return r.field
}

async function onImport() {
  try {
    await importStore.importAll()
  } catch {
    // importStore.importError is already set
  }
}
</script>

<template>
  <section>
    <div class="eyebrow mb-3">pasul 3 · revizuire</div>
    <h2 class="display-title text-2xl mb-2">Verifică produsele detectate</h2>
    <p class="text-ink-70 text-sm mb-5">
      Editează numele și prețul, sări peste rândurile greșite. Importul final creează
      categoriile noi și produsele într-o singură tranzacție.
    </p>

    <div v-if="importStore.failedPages.length" class="failures card-paper p-4 mb-5">
      <span class="font-mono text-xs uppercase tracking-[0.18em] text-accent font-bold">
        ⚠ Pagini neclare
      </span>
      <ul>
        <li v-for="fp in importStore.failedPages" :key="fp.sourcePage">
          Pagina {{ fp.sourcePage + 1 }} · {{ fp.reason }}
        </li>
      </ul>
    </div>

    <div v-if="importStore.reviewItems.length === 0" class="card-paper p-8 text-ink-70 italic">
      Nu am detectat niciun produs cu preț. Verifică dacă fotografiile arată un meniu cu prețuri vizibile.
    </div>

    <div v-else class="groups">
      <section v-for="[cat, items] in grouped" :key="cat" class="card-paper p-5">
        <header class="group-head">
          <div class="eyebrow">categorie</div>
          <h3 class="display-title text-xl">{{ cat }}</h3>
          <div class="text-ink-70 text-sm">{{ items.length }} produse</div>
        </header>
        <ul class="row-list">
          <li
            v-for="row in items"
            :key="row.clientId"
            class="card-white review-row"
            :class="{ 'is-skipped': row.skipped }"
            data-testid="review-row"
          >
            <span class="page-badge">p{{ row.sourcePage + 1 }}</span>
            <div class="row-inputs">
              <label class="field" :class="{ 'is-error': rowError(row) === 'name' }">
                <span class="field-label">Nume</span>
                <input
                  data-testid="row-name"
                  class="field-input"
                  type="text"
                  :maxlength="MENU_VALIDATION.NAME_MAX"
                  :value="row.name"
                  @input="(e) => importStore.updateItem(row.clientId, { name: (e.target as HTMLInputElement).value })"
                />
                <span v-if="rowError(row) === 'name'" data-testid="row-name-error" class="field-error">
                  Numele este obligatoriu (1–{{ MENU_VALIDATION.NAME_MAX }} caractere).
                </span>
              </label>
              <label class="field" :class="{ 'is-error': rowError(row) === 'price' }">
                <span class="field-label">Preț RON</span>
                <input
                  data-testid="row-price"
                  class="field-input"
                  type="number"
                  step="0.01"
                  :min="0.01"
                  :value="row.price"
                  @input="(e) => importStore.updateItem(row.clientId, { price: parseFloat((e.target as HTMLInputElement).value) })"
                />
                <span v-if="rowError(row) === 'price'" class="field-error">Preț invalid.</span>
              </label>
            </div>
            <button
              class="pill-btn pill-btn-ghost"
              type="button"
              data-testid="row-skip"
              @click="importStore.toggleSkip(row.clientId)"
            >
              {{ row.skipped ? 'Reactivează' : 'Sări' }}
            </button>
          </li>
        </ul>
      </section>
    </div>

    <div v-if="importStore.importError" class="card-paper p-4 mt-5" style="border-color: var(--accentBorder);">
      <span class="font-mono text-xs uppercase tracking-[0.18em] text-accent font-bold">
        ⚠ {{ importStore.importError }}
      </span>
    </div>

    <div class="footer-row">
      <button
        class="pill-btn pill-btn-ghost"
        type="button"
        @click="importStore.$patch({ step: 2 })"
      >
        Înapoi
      </button>
      <button
        class="pill-btn"
        type="button"
        data-testid="import-btn"
        :disabled="!importStore.canImport || importStore.isImporting"
        @click="onImport"
      >
        <template v-if="importStore.isImporting">Import în curs…</template>
        <template v-else>
          Importă {{ importStore.reviewItems.filter((r) => !r.skipped).length }} produse
        </template>
      </button>
    </div>
  </section>
</template>

<style scoped>
.groups { display: grid; gap: 18px; }
.group-head { display: flex; flex-direction: column; gap: 4px; margin-bottom: 12px; }
.row-list { list-style: none; padding: 0; display: grid; gap: 10px; }
.review-row {
  padding: 14px;
  display: grid;
  grid-template-columns: auto minmax(0, 1fr) auto;
  gap: 14px;
  align-items: start;
}
.review-row.is-skipped { opacity: 0.45; }
.page-badge {
  display: inline-grid;
  place-items: center;
  min-width: 38px;
  padding: 4px 8px;
  border-radius: 999px;
  background: var(--ink06);
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 11px;
  letter-spacing: 0.12em;
  color: var(--ink70);
}
.row-inputs {
  display: grid;
  grid-template-columns: 2fr 1fr;
  gap: 10px;
}
.field { display: flex; flex-direction: column; gap: 4px; }
.field-error { color: var(--accent); font-size: 11px; }
.field.is-error .field-input { border-color: var(--accent); }
.failures ul { margin-top: 6px; padding-left: 18px; font-size: 13px; color: var(--ink70); }
.footer-row { margin-top: 28px; display: flex; justify-content: space-between; }
</style>
