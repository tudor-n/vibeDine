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

const activeCount = computed(() => importStore.reviewItems.filter((r) => !r.skipped).length)
</script>

<template>
  <section class="space-y-4">
    <div class="step-intro">
      <p class="text-ink-70 text-sm">
        Editează numele și prețul, sări peste rândurile greșite. Importul final creează
        categoriile noi și produsele într-o singură tranzacție.
      </p>
      <span class="field-label">{{ activeCount }} de importat</span>
    </div>

    <div v-if="importStore.failedPages.length" class="alert alert-warning">
      <span class="alert-icon" aria-hidden="true">⚠</span>
      <div class="alert-body">
        <div class="alert-title">Pagini neclare</div>
        <ul class="failure-list">
          <li v-for="fp in importStore.failedPages" :key="fp.sourcePage">
            Pagina {{ fp.sourcePage + 1 }} · {{ fp.reason }}
          </li>
        </ul>
      </div>
    </div>

    <div v-if="importStore.reviewItems.length === 0" class="alert">
      <span class="alert-icon" aria-hidden="true">ℹ</span>
      <div class="alert-body">
        Nu am detectat niciun produs cu preț. Verifică dacă fotografiile arată un meniu cu prețuri vizibile.
      </div>
    </div>

    <div v-else class="groups">
      <section v-for="[cat, items] in grouped" :key="cat" class="group-card">
        <header class="group-head">
          <h3 class="section-title">{{ cat }}</h3>
          <span class="group-count">{{ items.length }} produse</span>
        </header>
        <ul class="row-list">
          <li
            v-for="row in items"
            :key="row.clientId"
            class="review-row"
            :class="{
              'is-skipped': row.skipped,
              'is-error': !!rowError(row),
            }"
            data-testid="review-row"
          >
            <span class="page-badge" :title="`Pagina ${row.sourcePage + 1}`">p{{ row.sourcePage + 1 }}</span>
            <input
              data-testid="row-name"
              class="field-input row-name-input"
              :class="{ 'is-error': rowError(row) === 'name' }"
              type="text"
              :maxlength="MENU_VALIDATION.NAME_MAX"
              :value="row.name"
              :disabled="row.skipped"
              :placeholder="rowError(row) === 'name' ? `nume (1–${MENU_VALIDATION.NAME_MAX} caractere)` : 'nume produs'"
              @input="(e) => importStore.updateItem(row.clientId, { name: (e.target as HTMLInputElement).value })"
            />
            <input
              data-testid="row-price"
              class="field-input row-price-input"
              :class="{ 'is-error': rowError(row) === 'price' }"
              type="number"
              step="0.01"
              :min="0.01"
              :value="row.price"
              :disabled="row.skipped"
              placeholder="0.00"
              @input="(e) => importStore.updateItem(row.clientId, { price: parseFloat((e.target as HTMLInputElement).value) })"
            />
            <button
              class="icon-btn"
              :class="{ 'icon-btn-danger': !row.skipped }"
              type="button"
              data-testid="row-skip"
              :title="row.skipped ? 'Reactivează' : 'Sări peste'"
              :aria-label="row.skipped ? 'Reactivează' : 'Sări peste'"
              @click="importStore.toggleSkip(row.clientId)"
            >
              {{ row.skipped ? '↺' : '×' }}
            </button>
          </li>
        </ul>
      </section>
    </div>

    <div v-if="importStore.importError" class="alert alert-error">
      <span class="alert-icon" aria-hidden="true">⚠</span>
      <div class="alert-body">{{ importStore.importError }}</div>
    </div>

    <div class="form-footer">
      <button class="btn-ghost" type="button" @click="importStore.$patch({ step: 2 })">
        ← Înapoi
      </button>
      <button
        class="pill-btn"
        type="button"
        data-testid="import-btn"
        :disabled="!importStore.canImport || importStore.isImporting"
        @click="onImport"
      >
        <template v-if="importStore.isImporting">Import în curs…</template>
        <template v-else>Importă {{ activeCount }} produse →</template>
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
.groups { display: grid; gap: 14px; }

.group-card {
  background: var(--paperHi);
  border: 1px solid var(--ink12);
  border-radius: 14px;
  padding: 14px 16px;
}
.group-head {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: 12px;
  margin-bottom: 10px;
  padding-bottom: 10px;
  border-bottom: 1px solid var(--ink12);
}
.group-count {
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 11px;
  text-transform: uppercase;
  letter-spacing: 0.14em;
  color: var(--ink40);
}
.row-list { list-style: none; padding: 0; display: grid; gap: 6px; margin: 0; }
.review-row {
  display: grid;
  grid-template-columns: auto minmax(0, 1fr) 110px auto;
  gap: 8px;
  align-items: center;
  padding: 6px 8px;
  border-radius: 10px;
  background: transparent;
  transition: background 0.12s ease;
}
.review-row:hover { background: var(--ink06); }
.review-row.is-skipped { opacity: 0.45; }
.review-row.is-error { background: var(--accentSoft); }

.page-badge {
  display: inline-grid;
  place-items: center;
  min-width: 32px;
  padding: 3px 8px;
  border-radius: 6px;
  background: var(--ink06);
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 10px;
  letter-spacing: 0.1em;
  color: var(--ink70);
}
.row-name-input,
.row-price-input {
  padding: 8px 12px;
  font-size: 14px;
  border-radius: 8px;
  background: var(--card);
}
.row-price-input {
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  text-align: right;
}
.field-input.is-error { border-color: var(--accent); }

.failure-list {
  margin: 4px 0 0;
  padding-left: 18px;
  font-size: 12px;
  color: var(--accent);
}

@media (max-width: 640px) {
  .review-row {
    grid-template-columns: auto 1fr auto;
    grid-template-areas:
      'badge name skip'
      '. price price';
  }
  .page-badge { grid-area: badge; }
  .row-name-input { grid-area: name; }
  .row-price-input { grid-area: price; }
  .review-row .icon-btn { grid-area: skip; }
}
</style>
