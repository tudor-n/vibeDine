<script setup lang="ts">
import { computed, reactive, ref } from 'vue'
import { useMenuImportStore, type HierarchyMoveTarget } from '@/stores/useMenuImportStore'
import { resolvePhotoUrl } from '@/services/menuImportApi'
import type { ItemRef } from '@shared/types/menu-import'

const store = useMenuImportStore()

const search = ref('')
const collapsedCategories = reactive(new Set<string>())

// ─── Item lookup ────────────────────────────────────────────────────────────
type Extracted = { name: string; price: number; description?: string | null; imageUrl?: string | null }
const itemByKey = computed(() => {
  const m = new Map<string, Extracted>()
  for (const it of store.extractedItems) {
    m.set(`${it.sourcePage}:${it.sourceIndex}`, {
      name: it.name,
      price: it.price,
      description: it.description ?? null,
      imageUrl: it.imageUrl ?? null,
    })
  }
  return m
})

function itemOf(ref: ItemRef): Extracted | undefined {
  return itemByKey.value.get(`${ref.sourcePage}:${ref.sourceIndex}`)
}

function isLowConfidence(ref: ItemRef): boolean {
  return (ref.confidence ?? 0) < 0.7
}

function isSkipped(ref: ItemRef): boolean {
  return store.isSkipped({ sourcePage: ref.sourcePage, sourceIndex: ref.sourceIndex })
}

// ─── Search filter ──────────────────────────────────────────────────────────
function matchesSearch(ref: ItemRef): boolean {
  if (!search.value.trim()) return true
  const q = search.value.trim().toLowerCase()
  const it = itemOf(ref)
  if (!it) return false
  return it.name.toLowerCase().includes(q)
}

function visibleRefs(refs: ItemRef[]): ItemRef[] {
  // Hide skipped items from the main tree; they surface in the "Omise" section.
  return refs.filter((r) => !isSkipped(r) && matchesSearch(r))
}

// ─── Move dropdown options ─────────────────────────────────────────────────
const moveOptions = computed(() => {
  const opts: { value: string; label: string; target: HierarchyMoveTarget }[] = []
  if (!store.hierarchy) return opts
  for (const c of store.hierarchy.categories) {
    opts.push({
      value: `direct:${c.name}`,
      label: `${c.name} (direct)`,
      target: { kind: 'direct', categoryName: c.name },
    })
    for (const s of c.subcategories) {
      opts.push({
        value: `sub:${c.name}:${s.name}`,
        label: `${c.name} / ${s.name}`,
        target: { kind: 'subcategory', categoryName: c.name, subcategoryName: s.name },
      })
    }
  }
  opts.push({
    value: 'uncategorized',
    label: 'Necategorisite',
    target: { kind: 'uncategorized' },
  })
  return opts
})

function moveOptionsExcludingCurrent(
  ref: ItemRef,
  current: HierarchyMoveTarget,
): { value: string; label: string; target: HierarchyMoveTarget }[] {
  // Don't allow moving to the slot you're already in.
  return moveOptions.value.filter((o) => {
    if (o.target.kind !== current.kind) return true
    if (current.kind === 'uncategorized') return false
    if (
      current.kind === 'direct' &&
      o.target.kind === 'direct' &&
      o.target.categoryName === current.categoryName
    )
      return false
    if (
      current.kind === 'subcategory' &&
      o.target.kind === 'subcategory' &&
      o.target.categoryName === current.categoryName &&
      o.target.subcategoryName === current.subcategoryName
    )
      return false
    return true
  })
}

function onMoveSelect(ref: ItemRef, value: string) {
  if (!value) return
  const opt = moveOptions.value.find((o) => o.value === value)
  if (opt) store.moveItem(ref, opt.target)
}

// ─── Per-item editing handlers ─────────────────────────────────────────────
function onItemNameInput(ref: ItemRef, value: string) {
  store.updateExtractedItem(
    { sourcePage: ref.sourcePage, sourceIndex: ref.sourceIndex },
    { name: value },
  )
}

function onItemPriceInput(ref: ItemRef, value: string) {
  const parsed = parseFloat(value)
  if (Number.isNaN(parsed)) return
  store.updateExtractedItem(
    { sourcePage: ref.sourcePage, sourceIndex: ref.sourceIndex },
    { price: parsed },
  )
}

function onSkip(ref: ItemRef) {
  store.skipItem({ sourcePage: ref.sourcePage, sourceIndex: ref.sourceIndex })
}

function onUnskip(ref: ItemRef) {
  store.unskipItem({ sourcePage: ref.sourcePage, sourceIndex: ref.sourceIndex })
}

// ─── Category / subcategory handlers ───────────────────────────────────────
function onCategoryRename(oldName: string, e: Event) {
  const value = (e.target as HTMLInputElement).value.trim()
  if (!value || value === oldName) return
  store.renameCategory(oldName, value)
  // Keep collapse state in sync with the new name
  if (collapsedCategories.has(oldName)) {
    collapsedCategories.delete(oldName)
    collapsedCategories.add(value)
  }
}

function onSubcategoryRename(categoryName: string, oldName: string, e: Event) {
  const value = (e.target as HTMLInputElement).value.trim()
  if (!value || value === oldName) return
  store.renameSubcategory(categoryName, oldName, value)
}

function onAddCategory() {
  const name = window.prompt('Numele categoriei noi:', 'Nouă categorie')
  if (!name || !name.trim()) return
  store.addCategory(name.trim())
}

function onRemoveCategory(name: string) {
  if (!window.confirm(`Ștergi categoria "${name}"? Produsele vor merge la Necategorisite.`)) return
  store.removeCategory(name)
}

function onAddSubcategory(categoryName: string) {
  const name = window.prompt('Numele subcategoriei:', 'Nouă subcategorie')
  if (!name || !name.trim()) return
  store.addSubcategory(categoryName, name.trim())
}

function onRemoveSubcategory(categoryName: string, name: string) {
  if (!window.confirm(`Ștergi subcategoria "${name}"? Produsele vor merge la categoria părinte.`)) return
  store.removeSubcategory(categoryName, name)
}

function toggleCollapse(name: string) {
  if (collapsedCategories.has(name)) collapsedCategories.delete(name)
  else collapsedCategories.add(name)
}

// ─── Stats ─────────────────────────────────────────────────────────────────
const stats = computed(() => {
  if (!store.hierarchy) return { categories: 0, items: 0, uncategorized: 0, skipped: 0 }
  let items = 0
  for (const c of store.hierarchy.categories) {
    items += c.directItemRefs.length
    for (const s of c.subcategories) items += s.itemRefs.length
  }
  return {
    categories: store.hierarchy.categories.length,
    items,
    uncategorized: store.hierarchy.uncategorizedItemRefs.length,
    skipped: store.skippedItemKeys.size,
  }
})

// ─── Skipped items listing ─────────────────────────────────────────────────
// All refs anywhere in the hierarchy or uncategorized that are currently skipped.
const skippedRefs = computed<ItemRef[]>(() => {
  if (!store.hierarchy) return []
  const out: ItemRef[] = []
  for (const c of store.hierarchy.categories) {
    for (const r of c.directItemRefs) if (isSkipped(r)) out.push(r)
    for (const s of c.subcategories) for (const r of s.itemRefs) if (isSkipped(r)) out.push(r)
  }
  for (const r of store.hierarchy.uncategorizedItemRefs) if (isSkipped(r)) out.push(r)
  return out
})

function priceDisplay(p: number): string {
  return Number.isFinite(p) ? p.toFixed(2) : '0.00'
}
</script>

<template>
  <section v-if="store.hierarchy" class="review-shell">
    <!-- Banner -->
    <div
      v-if="store.hierarchy.source === 'hint-fallback'"
      data-test="hint-fallback-banner"
      class="alert alert-warning"
    >
      <span class="alert-icon" aria-hidden="true">⚠</span>
      <div class="alert-body">
        <div class="alert-title">Atenție</div>
        Categorisirea automată nu a fost disponibilă — produsele sunt grupate doar după titlurile de secțiune.
      </div>
    </div>
    <div v-else class="alert">
      <span class="alert-icon" aria-hidden="true">✦</span>
      <div class="alert-body">
        <div class="alert-title">AI</div>
        Categorisit cu AI — verifică înainte de a accepta.
      </div>
    </div>

    <!-- Stats + search row -->
    <div class="review-toolbar">
      <div class="review-stats">
        <span class="tag-pill">{{ stats.categories }} categorii</span>
        <span class="tag-pill">{{ stats.items }} produse</span>
        <span v-if="stats.uncategorized > 0" class="tag-pill tag-pill-accent">
          {{ stats.uncategorized }} necategorisite
        </span>
        <span v-if="stats.skipped > 0" class="tag-pill">{{ stats.skipped }} omise</span>
      </div>
      <div class="search-wrap">
        <input
          v-model="search"
          class="field-input search-input"
          type="search"
          placeholder="Caută după nume…"
          aria-label="Caută produse"
        />
      </div>
    </div>

    <!-- Categories -->
    <div class="categories-list">
      <article
        v-for="cat in store.hierarchy.categories"
        :key="cat.tempId"
        class="group-card cat-card"
      >
        <header class="cat-head">
          <button
            type="button"
            class="caret-btn"
            :aria-expanded="!collapsedCategories.has(cat.name)"
            :title="collapsedCategories.has(cat.name) ? 'Expandează' : 'Restrânge'"
            @click="toggleCollapse(cat.name)"
          >
            <span aria-hidden="true">{{ collapsedCategories.has(cat.name) ? '▸' : '▾' }}</span>
          </button>

          <label class="cat-name-wrap">
            <span class="sr-only">Nume categorie</span>
            <span class="cat-name-display" aria-hidden="true">{{ cat.name }}</span>
            <input
              :value="cat.name"
              type="text"
              class="field-input cat-name-input"
              aria-label="Nume categorie"
              @change="(e) => onCategoryRename(cat.name, e)"
            />
          </label>

          <span class="tag-pill cat-count">
            {{
              cat.directItemRefs.length +
              cat.subcategories.reduce((n, s) => n + s.itemRefs.length, 0)
            }}
          </span>

          <div class="cat-actions">
            <button
              type="button"
              class="btn-ghost btn-ghost-sm"
              title="Adaugă subcategorie"
              @click="onAddSubcategory(cat.name)"
            >
              + sub
            </button>
            <button
              type="button"
              class="icon-btn icon-btn-danger"
              :title="`Șterge categoria ${cat.name}`"
              :aria-label="`Șterge categoria ${cat.name}`"
              @click="onRemoveCategory(cat.name)"
            >
              ×
            </button>
          </div>
        </header>

        <div v-if="!collapsedCategories.has(cat.name)" class="cat-body">
          <!-- Subcategories -->
          <div v-for="sub in cat.subcategories" :key="sub.tempId" class="sub-block">
            <header class="sub-head">
              <label class="sub-name-wrap">
                <span class="sr-only">Nume subcategorie</span>
                <span class="sub-name-display" aria-hidden="true">{{ sub.name }}</span>
                <input
                  :value="sub.name"
                  type="text"
                  class="field-input sub-name-input"
                  aria-label="Nume subcategorie"
                  @change="(e) => onSubcategoryRename(cat.name, sub.name, e)"
                />
              </label>
              <span class="sub-count">{{ visibleRefs(sub.itemRefs).length }}</span>
              <button
                type="button"
                class="icon-btn icon-btn-danger"
                :title="`Șterge subcategoria ${sub.name}`"
                :aria-label="`Șterge subcategoria ${sub.name}`"
                @click="onRemoveSubcategory(cat.name, sub.name)"
              >
                ×
              </button>
            </header>
            <ul class="item-list">
              <li
                v-for="ref in visibleRefs(sub.itemRefs)"
                :key="`${ref.sourcePage}:${ref.sourceIndex}`"
                class="item-row"
                :class="{ 'is-low-conf': isLowConfidence(ref) }"
              >
                <span
                  v-if="isLowConfidence(ref)"
                  data-test="confidence-dot"
                  class="confidence-dot"
                  title="Confidență scăzută"
                />
                <span v-else class="confidence-dot-spacer" aria-hidden="true" />
                <span class="page-badge" :title="`Pagina ${ref.sourcePage + 1}`">
                  p{{ ref.sourcePage + 1 }}
                </span>
                <span class="hier-thumb-slot">
                  <template v-if="itemOf(ref)?.imageUrl">
                    <img
                      :data-testid="`hier-thumb-${ref.sourcePage}-${ref.sourceIndex}`"
                      class="hier-thumb"
                      :src="resolvePhotoUrl(itemOf(ref)?.imageUrl)"
                      :alt="itemOf(ref)?.name"
                    />
                    <button
                      type="button"
                      class="hier-thumb-remove"
                      :data-testid="`hier-thumb-remove-${ref.sourcePage}-${ref.sourceIndex}`"
                      title="Elimină fotografia"
                      aria-label="Elimină fotografia"
                      @click="store.removeItemPhoto({ sourcePage: ref.sourcePage, sourceIndex: ref.sourceIndex })"
                    >×</button>
                  </template>
                </span>
                <span class="sr-only">{{ itemOf(ref)?.name }}</span>
                <input
                  type="text"
                  class="field-input row-name-input"
                  :value="itemOf(ref)?.name"
                  aria-label="Nume produs"
                  @input="(e) => onItemNameInput(ref, (e.target as HTMLInputElement).value)"
                />
                <input
                  type="number"
                  step="0.01"
                  min="0.01"
                  class="field-input row-price-input"
                  :value="itemOf(ref)?.price"
                  aria-label="Preț"
                  @input="(e) => onItemPriceInput(ref, (e.target as HTMLInputElement).value)"
                />
                <select
                  class="field-input row-move-select"
                  :title="`Mută ${itemOf(ref)?.name ?? 'produsul'}`"
                  :aria-label="`Mută ${itemOf(ref)?.name ?? 'produsul'}`"
                  @change="(e) => { onMoveSelect(ref, (e.target as HTMLSelectElement).value); (e.target as HTMLSelectElement).value = '' }"
                >
                  <option value="">Mută în…</option>
                  <option
                    v-for="opt in moveOptionsExcludingCurrent(ref, { kind: 'subcategory', categoryName: cat.name, subcategoryName: sub.name })"
                    :key="opt.value"
                    :value="opt.value"
                  >
                    {{ opt.label }}
                  </option>
                </select>
                <button
                  type="button"
                  class="icon-btn icon-btn-danger"
                  :data-test="`skip-${ref.sourcePage}-${ref.sourceIndex}`"
                  title="Omite produsul"
                  aria-label="Omite produsul"
                  @click="onSkip(ref)"
                >
                  ×
                </button>
              </li>
            </ul>
          </div>

          <!-- Direct items -->
          <div v-if="visibleRefs(cat.directItemRefs).length > 0" class="direct-block">
            <header class="sub-head sub-head-direct">
              <span class="field-label">Produse directe</span>
              <span class="sub-count">{{ visibleRefs(cat.directItemRefs).length }}</span>
            </header>
            <ul class="item-list">
              <li
                v-for="ref in visibleRefs(cat.directItemRefs)"
                :key="`${ref.sourcePage}:${ref.sourceIndex}`"
                class="item-row"
                :class="{ 'is-low-conf': isLowConfidence(ref) }"
              >
                <span
                  v-if="isLowConfidence(ref)"
                  data-test="confidence-dot"
                  class="confidence-dot"
                  title="Confidență scăzută"
                />
                <span v-else class="confidence-dot-spacer" aria-hidden="true" />
                <span class="page-badge" :title="`Pagina ${ref.sourcePage + 1}`">
                  p{{ ref.sourcePage + 1 }}
                </span>
                <span class="hier-thumb-slot">
                  <template v-if="itemOf(ref)?.imageUrl">
                    <img
                      :data-testid="`hier-thumb-${ref.sourcePage}-${ref.sourceIndex}`"
                      class="hier-thumb"
                      :src="resolvePhotoUrl(itemOf(ref)?.imageUrl)"
                      :alt="itemOf(ref)?.name"
                    />
                    <button
                      type="button"
                      class="hier-thumb-remove"
                      :data-testid="`hier-thumb-remove-${ref.sourcePage}-${ref.sourceIndex}`"
                      title="Elimină fotografia"
                      aria-label="Elimină fotografia"
                      @click="store.removeItemPhoto({ sourcePage: ref.sourcePage, sourceIndex: ref.sourceIndex })"
                    >×</button>
                  </template>
                </span>
                <span class="sr-only">{{ itemOf(ref)?.name }}</span>
                <input
                  type="text"
                  class="field-input row-name-input"
                  :value="itemOf(ref)?.name"
                  aria-label="Nume produs"
                  @input="(e) => onItemNameInput(ref, (e.target as HTMLInputElement).value)"
                />
                <input
                  type="number"
                  step="0.01"
                  min="0.01"
                  class="field-input row-price-input"
                  :value="itemOf(ref)?.price"
                  aria-label="Preț"
                  @input="(e) => onItemPriceInput(ref, (e.target as HTMLInputElement).value)"
                />
                <select
                  class="field-input row-move-select"
                  :title="`Mută ${itemOf(ref)?.name ?? 'produsul'}`"
                  :aria-label="`Mută ${itemOf(ref)?.name ?? 'produsul'}`"
                  @change="(e) => { onMoveSelect(ref, (e.target as HTMLSelectElement).value); (e.target as HTMLSelectElement).value = '' }"
                >
                  <option value="">Mută în…</option>
                  <option
                    v-for="opt in moveOptionsExcludingCurrent(ref, { kind: 'direct', categoryName: cat.name })"
                    :key="opt.value"
                    :value="opt.value"
                  >
                    {{ opt.label }}
                  </option>
                </select>
                <button
                  type="button"
                  class="icon-btn icon-btn-danger"
                  :data-test="`skip-${ref.sourcePage}-${ref.sourceIndex}`"
                  title="Omite produsul"
                  aria-label="Omite produsul"
                  @click="onSkip(ref)"
                >
                  ×
                </button>
              </li>
            </ul>
          </div>

          <p
            v-if="
              visibleRefs(cat.directItemRefs).length === 0 &&
              cat.subcategories.every((s) => visibleRefs(s.itemRefs).length === 0)
            "
            class="empty-cat-hint"
          >
            <span v-if="search">Nicio potrivire în această categorie.</span>
            <span v-else>Această categorie nu are produse. Mută produse aici sau șterge-o.</span>
          </p>
        </div>
      </article>
    </div>

    <!-- Add category button -->
    <div class="add-cat-row">
      <button type="button" class="btn-ghost" @click="onAddCategory">
        + Adaugă categorie
      </button>
    </div>

    <!-- Uncategorized -->
    <article
      v-if="store.hierarchy.uncategorizedItemRefs.length > 0"
      class="group-card uncategorized-card"
    >
      <header class="cat-head cat-head-warning">
        <span class="section-title">Necategorisite</span>
        <span class="tag-pill tag-pill-accent">
          {{ store.hierarchy.uncategorizedItemRefs.length }}
        </span>
        <span class="field-label uncategorized-blocker">
          Blochează salvarea
        </span>
      </header>
      <ul class="item-list">
        <li
          v-for="ref in store.hierarchy.uncategorizedItemRefs"
          :key="`${ref.sourcePage}:${ref.sourceIndex}`"
          class="item-row"
          :class="{ 'is-low-conf': isLowConfidence(ref), 'is-skipped': isSkipped(ref) }"
        >
          <span
            data-test="confidence-dot"
            class="confidence-dot"
            title="Necategorisit"
          />
          <span class="page-badge" :title="`Pagina ${ref.sourcePage + 1}`">
            p{{ ref.sourcePage + 1 }}
          </span>
          <span class="hier-thumb-slot">
            <template v-if="itemOf(ref)?.imageUrl">
              <img
                :data-testid="`hier-thumb-${ref.sourcePage}-${ref.sourceIndex}`"
                class="hier-thumb"
                :src="resolvePhotoUrl(itemOf(ref)?.imageUrl)"
                :alt="itemOf(ref)?.name"
              />
              <button
                type="button"
                class="hier-thumb-remove"
                :data-testid="`hier-thumb-remove-${ref.sourcePage}-${ref.sourceIndex}`"
                title="Elimină fotografia"
                aria-label="Elimină fotografia"
                @click="store.removeItemPhoto({ sourcePage: ref.sourcePage, sourceIndex: ref.sourceIndex })"
              >×</button>
            </template>
          </span>
          <input
            type="text"
            class="field-input row-name-input"
            :value="itemOf(ref)?.name"
            aria-label="Nume produs"
            @input="(e) => onItemNameInput(ref, (e.target as HTMLInputElement).value)"
          />
          <input
            type="number"
            step="0.01"
            min="0.01"
            class="field-input row-price-input"
            :value="itemOf(ref)?.price"
            aria-label="Preț"
            @input="(e) => onItemPriceInput(ref, (e.target as HTMLInputElement).value)"
          />
          <select
            :data-test="`uncategorized-move-${ref.sourcePage}-${ref.sourceIndex}`"
            class="field-input row-move-select"
            @change="(e) => onMoveSelect(ref, (e.target as HTMLSelectElement).value)"
          >
            <option value="">Mută în…</option>
            <option v-for="opt in moveOptions" :key="opt.value" :value="opt.value">
              {{ opt.label }}
            </option>
          </select>
          <button
            type="button"
            class="icon-btn icon-btn-danger"
            :data-test="`skip-${ref.sourcePage}-${ref.sourceIndex}`"
            title="Omite produsul"
            aria-label="Omite produsul"
            @click="onSkip(ref)"
          >
            ×
          </button>
        </li>
      </ul>
    </article>

    <!-- Skipped -->
    <article v-if="skippedRefs.length > 0" class="group-card skipped-card">
      <header class="cat-head">
        <span class="section-title">Omise</span>
        <span class="tag-pill">{{ skippedRefs.length }}</span>
        <span class="field-label">Nu vor fi importate</span>
      </header>
      <ul class="item-list">
        <li
          v-for="ref in skippedRefs"
          :key="`skipped-${ref.sourcePage}:${ref.sourceIndex}`"
          class="item-row item-row-skipped"
        >
          <span class="confidence-dot-spacer" aria-hidden="true" />
          <span class="page-badge" :title="`Pagina ${ref.sourcePage + 1}`">
            p{{ ref.sourcePage + 1 }}
          </span>
          <span class="skipped-name">{{ itemOf(ref)?.name }}</span>
          <span class="skipped-price">{{ priceDisplay(itemOf(ref)?.price ?? 0) }} lei</span>
          <button
            type="button"
            class="icon-btn"
            title="Reactivează produsul"
            aria-label="Reactivează produsul"
            @click="onUnskip(ref)"
          >
            ↺
          </button>
        </li>
      </ul>
    </article>
  </section>
</template>

<style scoped>
.review-shell {
  display: grid;
  gap: 18px;
}

.review-toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 14px;
  flex-wrap: wrap;
}
.review-stats {
  display: flex;
  gap: 8px;
  flex-wrap: wrap;
}
.tag-pill-accent {
  background: var(--accentSoft);
  border-color: var(--accentBorder);
  color: var(--accent);
}
.search-wrap {
  flex: 1;
  min-width: 220px;
  max-width: 340px;
  margin-left: auto;
}
.search-input { width: 100%; }

.categories-list {
  display: grid;
  gap: 14px;
}

.cat-card {
  background: var(--paperHi);
  border: 1px solid var(--ink12);
  border-radius: 14px;
  padding: 14px 16px;
}
.cat-head {
  display: grid;
  grid-template-columns: auto minmax(0, 1fr) auto auto;
  align-items: center;
  gap: 10px;
  padding-bottom: 10px;
  border-bottom: 1px solid var(--ink12);
}
.cat-head-warning {
  border-bottom: 0;
  padding-bottom: 0;
}

.caret-btn {
  width: 28px;
  height: 28px;
  border-radius: 8px;
  border: 1px solid transparent;
  background: transparent;
  color: var(--ink70);
  cursor: pointer;
  display: grid;
  place-items: center;
  font-size: 11px;
  transition: background 0.12s ease, color 0.12s ease;
}
.caret-btn:hover {
  background: var(--ink06);
  color: var(--ink);
}

/* ── Inline edit pattern: visible span overlays the input; input takes
   over on focus. Both elements live in the DOM so `wrapper.text()` finds
   the visible name AND `wrapper.find('input').element.value` works too. ── */
.cat-name-wrap,
.sub-name-wrap {
  position: relative;
  display: inline-block;
  min-width: 0;
  flex: 1;
}
.cat-name-display,
.sub-name-display {
  display: inline-block;
  font-family: 'Inter Tight', sans-serif;
  font-weight: 600;
  letter-spacing: -0.02em;
  color: var(--ink);
  padding: 8px 12px;
  border: 1px solid transparent;
  border-radius: 10px;
  cursor: text;
  pointer-events: none;
}
.cat-name-display { font-size: 18px; }
.sub-name-display { font-size: 14px; }
.cat-name-input,
.sub-name-input {
  position: absolute;
  inset: 0;
  width: 100%;
  height: 100%;
  background: transparent;
  border: 1px solid transparent;
  border-radius: 10px;
  font-family: 'Inter Tight', sans-serif;
  font-weight: 600;
  letter-spacing: -0.02em;
  color: transparent;
  caret-color: var(--ink);
  padding: 8px 12px;
}
.cat-name-input { font-size: 18px; }
.sub-name-input { font-size: 14px; }
.cat-name-input:hover,
.sub-name-input:hover { border-color: var(--ink12); }
.cat-name-input:focus,
.sub-name-input:focus {
  color: var(--ink);
  background: var(--paper);
  border-color: var(--ink);
  outline: none;
}
.cat-name-input:focus + .cat-name-display,
.sub-name-input:focus + .sub-name-display { visibility: hidden; }

.cat-count {
  font-variant-numeric: tabular-nums;
}

.cat-actions {
  display: flex;
  align-items: center;
  gap: 6px;
}
.btn-ghost-sm {
  padding: 6px 12px;
  min-height: 34px;
  font-size: 12px;
}

.cat-body {
  display: grid;
  gap: 14px;
  padding-top: 12px;
}

.sub-block, .direct-block { display: grid; gap: 6px; }

.sub-head {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto auto;
  align-items: center;
  gap: 8px;
  padding: 4px 0 4px 4px;
}
.sub-head-direct {
  grid-template-columns: minmax(0, 1fr) auto;
  padding-top: 8px;
}
.sub-count {
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 11px;
  letter-spacing: 0.14em;
  color: var(--ink40);
}

.item-list {
  list-style: none;
  margin: 0;
  padding: 0;
  display: grid;
  gap: 4px;
}
.item-row {
  display: grid;
  grid-template-columns: 14px 44px auto minmax(0, 1fr) 110px auto auto;
  gap: 8px;
  align-items: center;
  padding: 4px 6px;
  border-radius: 10px;
  transition: background 0.12s ease;
}

/* Dish-photo crop. The column collapses to 0 for items without a photo. */
.hier-thumb-slot {
  position: relative;
  display: inline-flex;
  line-height: 0;
}
.hier-thumb {
  width: 40px;
  height: 40px;
  border-radius: 8px;
  object-fit: cover;
  background: var(--ink06);
  border: 1px solid var(--ink12);
}
.hier-thumb-remove {
  position: absolute;
  top: -6px;
  right: -6px;
  width: 18px;
  height: 18px;
  border-radius: 50%;
  border: none;
  background: rgba(21, 20, 15, 0.78);
  color: #fff;
  font-size: 12px;
  line-height: 1;
  cursor: pointer;
  display: grid;
  place-items: center;
}
.item-row:hover { background: var(--ink06); }
.item-row.is-low-conf { background: var(--accentSoft); }
.item-row.is-skipped { opacity: 0.45; }
.item-row-skipped {
  grid-template-columns: 14px 44px minmax(0, 1fr) auto auto;
  opacity: 0.7;
}
.confidence-dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: var(--accent);
  justify-self: center;
}
.confidence-dot-spacer {
  display: inline-block;
  width: 8px;
  height: 8px;
}
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
.row-name-input {
  padding: 8px 12px;
  font-size: 14px;
  border-radius: 8px;
  background: var(--card);
}
.row-price-input {
  padding: 8px 12px;
  font-size: 14px;
  border-radius: 8px;
  background: var(--card);
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  text-align: right;
}
.row-move-select {
  padding: 6px 8px;
  font-size: 12px;
  border-radius: 8px;
  background: var(--card);
  max-width: 160px;
}

.empty-cat-hint {
  font-size: 12px;
  color: var(--ink40);
  margin: 0;
  padding: 8px;
  text-align: center;
  border: 1px dashed var(--ink12);
  border-radius: 8px;
}

.add-cat-row {
  display: flex;
  justify-content: center;
}

.uncategorized-card {
  border-color: var(--accentBorder);
  background: var(--accentSoft);
}
.uncategorized-blocker {
  color: var(--accent);
  margin-left: auto;
}

.skipped-card {
  background: var(--paperHi);
}
.skipped-name {
  font-size: 14px;
  color: var(--ink70);
  text-decoration: line-through;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.skipped-price {
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 12px;
  color: var(--ink40);
  text-align: right;
}

.sr-only {
  position: absolute !important;
  width: 1px; height: 1px;
  padding: 0; margin: -1px;
  overflow: hidden;
  clip: rect(0, 0, 0, 0);
  white-space: nowrap;
  border: 0;
}

@media (max-width: 720px) {
  .cat-head {
    grid-template-columns: auto minmax(0, 1fr) auto;
    grid-template-areas:
      'caret name count'
      'actions actions actions';
    row-gap: 8px;
  }
  .caret-btn { grid-area: caret; }
  .cat-name-wrap { grid-area: name; }
  .cat-count { grid-area: count; }
  .cat-actions { grid-area: actions; justify-content: flex-end; }

  .item-row {
    grid-template-columns: 14px 44px auto minmax(0, 1fr) auto;
    grid-template-areas:
      'conf badge thumb name skip'
      '. . . price price'
      '. . . move move';
  }
  .item-row .confidence-dot,
  .item-row .confidence-dot-spacer { grid-area: conf; }
  .item-row .page-badge { grid-area: badge; }
  .item-row .hier-thumb-slot { grid-area: thumb; }
  .item-row .row-name-input { grid-area: name; }
  .item-row .row-price-input { grid-area: price; }
  .item-row .row-move-select { grid-area: move; max-width: unset; }
  .item-row > .icon-btn-danger:last-child { grid-area: skip; }
}
</style>
