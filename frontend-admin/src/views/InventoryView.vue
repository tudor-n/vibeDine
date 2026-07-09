<script setup lang="ts">
import { onMounted, ref, reactive, computed } from 'vue'
import BrandSelect from '@/components/BrandSelect.vue'
import { useInventoryStore } from '@/stores/useInventoryStore'
import { useToast } from '@/composables/useToast'
import type { AdminIngredient, StockUnitCode } from '@shared/types/models'

const store = useInventoryStore()
const toast = useToast()

onMounted(() => store.fetchIngredients())

// ---------- Units ----------
const UNIT_LABELS: Record<StockUnitCode, string> = {
    piece: 'buc',
    gram: 'g',
    kilogram: 'kg',
    milliliter: 'ml',
    liter: 'l',
}
const UNIT_OPTIONS = [
    { value: 'piece', label: 'buc · bucăți' },
    { value: 'gram', label: 'g · grame' },
    { value: 'kilogram', label: 'kg · kilograme' },
    { value: 'milliliter', label: 'ml · mililitri' },
    { value: 'liter', label: 'l · litri' },
]
function unitLabel(u: StockUnitCode) {
    return UNIT_LABELS[u] ?? u
}

const qtyFormatter = new Intl.NumberFormat('ro-RO', { maximumFractionDigits: 3 })
function formatQty(n: number) {
    return qtyFormatter.format(n)
}

// ---------- Toolbar ----------
const searchQuery = ref('')
const filter = ref<'all' | 'low'>('all')

const visibleIngredients = computed(() => {
    const q = searchQuery.value.trim().toLowerCase()
    return store.ingredients.filter((i) => {
        if (filter.value === 'low' && !i.isLow) return false
        if (q && !i.name.toLowerCase().includes(q)) return false
        return true
    })
})

const isFiltering = computed(() => searchQuery.value.trim().length > 0 || filter.value !== 'all')
function clearFilters() {
    searchQuery.value = ''
    filter.value = 'all'
}

// ---------- Create / edit modal ----------
type FormMode = 'create' | 'edit'
const form = reactive({
    open: false,
    mode: 'create' as FormMode,
    id: '',
    name: '',
    unit: 'piece' as StockUnitCode,
    quantityOnHand: 0 as number,
    lowStockThreshold: 0 as number,
    submitting: false,
})

function openCreate() {
    form.open = true
    form.mode = 'create'
    form.id = ''
    form.name = ''
    form.unit = 'piece'
    form.quantityOnHand = 0
    form.lowStockThreshold = 0
}

function openEdit(i: AdminIngredient) {
    if (i.linkedProductId) return
    form.open = true
    form.mode = 'edit'
    form.id = i.id
    form.name = i.name
    form.unit = i.unit
    form.quantityOnHand = i.quantityOnHand
    form.lowStockThreshold = i.lowStockThreshold
}

function closeForm() {
    form.open = false
}

async function submitForm() {
    const name = form.name.trim()
    if (!name) {
        toast.error('Numele ingredientului este obligatoriu.')
        return
    }
    if (form.lowStockThreshold < 0) {
        toast.error('Pragul de alertă nu poate fi negativ.')
        return
    }
    form.submitting = true
    try {
        if (form.mode === 'create') {
            if (form.quantityOnHand < 0) {
                toast.error('Cantitatea nu poate fi negativă.')
                return
            }
            await store.createIngredient({
                name,
                unit: form.unit,
                quantityOnHand: form.quantityOnHand,
                lowStockThreshold: form.lowStockThreshold,
            })
            toast.success('Ingredientul a fost adăugat.')
        } else {
            await store.updateIngredient(form.id, {
                name,
                unit: form.unit,
                lowStockThreshold: form.lowStockThreshold,
            })
            toast.success('Ingredientul a fost actualizat.')
        }
        closeForm()
    } catch (e: any) {
        toast.error(e?.message ?? 'Eroare la salvarea ingredientului.')
    } finally {
        form.submitting = false
    }
}

async function remove(i: AdminIngredient) {
    if (i.linkedProductId) return
    if (!window.confirm(`Sigur ștergi ingredientul „${i.name}”?`)) return
    try {
        await store.deleteIngredient(i.id)
        toast.success('Ingredientul a fost șters.')
    } catch (e: any) {
        toast.error(e?.message ?? 'Eroare la ștergerea ingredientului.')
    }
}

// ---------- Restock / adjust modal ----------
const adjust = reactive({
    open: false,
    id: '',
    name: '',
    unit: 'piece' as StockUnitCode,
    currentQty: 0,
    mode: 'set' as 'set' | 'add',
    value: 0 as number,
    submitting: false,
})

function openAdjust(i: AdminIngredient) {
    adjust.open = true
    adjust.id = i.id
    adjust.name = i.name
    adjust.unit = i.unit
    adjust.currentQty = i.quantityOnHand
    adjust.mode = 'set'
    adjust.value = i.quantityOnHand
}

function closeAdjust() {
    adjust.open = false
}

function setAdjustMode(mode: 'set' | 'add') {
    adjust.mode = mode
    adjust.value = mode === 'set' ? adjust.currentQty : 0
}

async function submitAdjust() {
    adjust.submitting = true
    try {
        const payload = adjust.mode === 'set' ? { setTo: adjust.value } : { delta: adjust.value }
        if (adjust.mode === 'set' && adjust.value < 0) {
            toast.error('Cantitatea nu poate fi negativă.')
            return
        }
        await store.adjustIngredient(adjust.id, payload)
        toast.success('Stocul a fost actualizat.')
        closeAdjust()
    } catch (e: any) {
        toast.error(e?.message ?? 'Eroare la actualizarea stocului.')
    } finally {
        adjust.submitting = false
    }
}
</script>

<template>
    <div class="max-w-5xl mx-auto relative">
        <header class="page-head">
            <div>
                <div class="page-head-eyebrow">Bucătărie · stoc</div>
                <h1 class="page-head-title">
                    Inventarul <span class="accent">curții.</span>
                </h1>
                <div class="stats-inline">
                    <span class="stat-inline">
                        <strong>{{ store.ingredients.length }}</strong> ingrediente
                    </span>
                    <template v-if="store.lowStockCount > 0">
                        <span class="stats-inline-sep" aria-hidden="true">·</span>
                        <span class="stat-inline stat-inline-warn">
                            <strong>{{ store.lowStockCount }}</strong> stoc redus
                        </span>
                    </template>
                </div>
            </div>

            <div class="page-head-actions">
                <button class="pill-btn whitespace-nowrap" @click="openCreate">
                    <span aria-hidden="true">+</span>
                    <span>Adaugă ingredient</span>
                </button>
            </div>
        </header>

        <!-- Toolbar -->
        <div class="toolbar card-paper">
            <div class="toolbar-search">
                <span class="toolbar-search-icon" aria-hidden="true">⌕</span>
                <input
                    v-model="searchQuery"
                    type="text"
                    class="toolbar-search-input"
                    placeholder="Caută ingredient…"
                />
                <button
                    v-if="searchQuery"
                    class="toolbar-search-clear"
                    type="button"
                    @click="searchQuery = ''"
                    aria-label="Șterge căutarea"
                >×</button>
            </div>

            <div class="toolbar-filters" role="tablist" aria-label="Filtrează stocul">
                <button
                    class="filter-chip"
                    :class="{ 'filter-chip-active': filter === 'all' }"
                    @click="filter = 'all'"
                    type="button"
                >Toate</button>
                <button
                    class="filter-chip"
                    :class="{ 'filter-chip-active': filter === 'low' }"
                    @click="filter = 'low'"
                    type="button"
                >Stoc redus</button>
            </div>
        </div>

        <div v-if="store.error" class="alert alert-error mb-4">
            <span class="alert-icon" aria-hidden="true">⚠</span>
            <div class="alert-body">{{ store.error }}</div>
        </div>

        <!-- Loading skeleton -->
        <div
            v-if="store.isLoading && store.ingredients.length === 0"
            class="card-paper p-6 space-y-3"
        >
            <div class="h-4 bg-ink-12 rounded-full w-2/3 animate-pulse"></div>
            <div class="h-4 bg-ink-12 rounded-full w-1/2 animate-pulse"></div>
            <div class="h-4 bg-ink-12 rounded-full w-3/5 animate-pulse"></div>
        </div>

        <!-- Empty -->
        <div
            v-else-if="store.ingredients.length === 0"
            class="card-paper p-8 text-ink-70 italic"
        >
            Niciun ingredient încă. Apasă „Adaugă ingredient" pentru a începe să urmărești stocul.
        </div>

        <!-- No results -->
        <div v-else-if="visibleIngredients.length === 0" class="empty-state">
            <div class="empty-state-icon" aria-hidden="true">∅</div>
            <div class="text-ink text-base mb-1">Niciun ingredient pentru filtrele curente</div>
            <p class="text-ink-70 text-sm mb-3">Schimbă căutarea sau resetează filtrele.</p>
            <button class="btn-ghost" type="button" @click="clearFilters">Resetează filtrele</button>
        </div>

        <!-- List -->
        <div v-else class="grid gap-2">
            <article
                v-for="i in visibleIngredients"
                :key="i.id"
                class="card-white p-3 ing-row"
                :class="{ 'ing-row-low': i.isLow }"
            >
                <div class="flex-1 min-w-0">
                    <div class="flex items-center gap-2 flex-wrap mb-1">
                        <h3 class="font-semibold text-ink text-base truncate">{{ i.name }}</h3>
                        <span v-if="i.linkedProductId" class="linked-tag" title="Stoc gestionat prin produsul din meniu">produs</span>
                        <span v-if="i.isLow" class="low-pill">stoc redus</span>
                    </div>
                    <div class="ing-meta font-mono">
                        prag alertă {{ formatQty(i.lowStockThreshold) }} {{ unitLabel(i.unit) }}
                    </div>
                </div>

                <div class="ing-qty">
                    <span class="ing-qty-num" :class="{ 'ing-qty-low': i.isLow }">{{ formatQty(i.quantityOnHand) }}</span>
                    <span class="ing-qty-unit">{{ unitLabel(i.unit) }}</span>
                </div>

                <div class="flex items-center gap-1 shrink-0">
                    <button
                        class="icon-btn"
                        @click="openAdjust(i)"
                        title="Reaprovizionează / ajustează"
                        aria-label="Reaprovizionează"
                    >⇅</button>
                    <button
                        class="icon-btn"
                        :disabled="!!i.linkedProductId"
                        :title="i.linkedProductId ? 'Gestionat din meniu' : 'Editează ingredientul'"
                        aria-label="Editează ingredientul"
                        @click="openEdit(i)"
                    >✎</button>
                    <button
                        class="icon-btn icon-btn-danger"
                        :disabled="!!i.linkedProductId"
                        :title="i.linkedProductId ? 'Gestionat din meniu' : 'Șterge ingredientul'"
                        aria-label="Șterge ingredientul"
                        @click="remove(i)"
                    >🗑</button>
                </div>
            </article>
        </div>

        <!-- Create / edit modal -->
        <Teleport to="body">
            <div v-if="form.open" class="modal-backdrop" @click.self="closeForm">
                <div class="modal-card">
                    <div class="flex items-center justify-between mb-5">
                        <div>
                            <div class="eyebrow mb-1">
                                {{ form.mode === 'create' ? 'ingredient nou' : 'editare ingredient' }}
                            </div>
                            <h3 class="display-title text-2xl">
                                {{ form.mode === 'create' ? 'Adaugă ingredient' : 'Editează ingredientul' }}
                            </h3>
                        </div>
                        <button class="modal-close" @click="closeForm" aria-label="Închide">×</button>
                    </div>

                    <form @submit.prevent="submitForm" class="space-y-4">
                        <label class="field">
                            <span class="field-label">Nume</span>
                            <input
                                v-model="form.name"
                                type="text"
                                class="field-input"
                                placeholder="ex. Mozzarella"
                                maxlength="120"
                                required
                            />
                        </label>

                        <div class="grid grid-cols-2 gap-3">
                            <div class="field">
                                <span class="field-label">Unitate</span>
                                <BrandSelect
                                    :model-value="form.unit"
                                    :options="UNIT_OPTIONS"
                                    aria-label="Unitate de măsură"
                                    @update:model-value="(v) => (form.unit = v as StockUnitCode)"
                                />
                            </div>
                            <label v-if="form.mode === 'create'" class="field">
                                <span class="field-label">Cantitate inițială</span>
                                <input
                                    v-model.number="form.quantityOnHand"
                                    type="number"
                                    step="any"
                                    min="0"
                                    class="field-input"
                                    placeholder="ex. 1000"
                                />
                            </label>
                        </div>

                        <label class="field">
                            <span class="field-label">Prag de alertă „stoc redus"</span>
                            <input
                                v-model.number="form.lowStockThreshold"
                                type="number"
                                step="any"
                                min="0"
                                class="field-input"
                                placeholder="ex. 200"
                            />
                            <span class="field-hint">
                                Sub această cantitate, ingredientul apare ca „stoc redus". Lasă 0 pentru a fi avertizat doar la epuizare.
                            </span>
                        </label>

                        <div class="flex items-center justify-end gap-2 pt-2">
                            <button type="button" class="pill-btn pill-btn-ghost" @click="closeForm">Anulează</button>
                            <button type="submit" class="pill-btn" :disabled="form.submitting">
                                {{ form.submitting ? 'Salvez...' : 'Salvează' }}
                            </button>
                        </div>
                    </form>
                </div>
            </div>
        </Teleport>

        <!-- Adjust / restock modal -->
        <Teleport to="body">
            <div v-if="adjust.open" class="modal-backdrop" @click.self="closeAdjust">
                <div class="modal-card">
                    <div class="flex items-center justify-between mb-5">
                        <div>
                            <div class="eyebrow mb-1">stoc</div>
                            <h3 class="display-title text-2xl">{{ adjust.name }}</h3>
                            <div class="ing-meta font-mono mt-1">
                                în stoc acum: {{ formatQty(adjust.currentQty) }} {{ unitLabel(adjust.unit) }}
                            </div>
                        </div>
                        <button class="modal-close" @click="closeAdjust" aria-label="Închide">×</button>
                    </div>

                    <form @submit.prevent="submitAdjust" class="space-y-4">
                        <div class="seg">
                            <button
                                type="button"
                                class="seg-btn"
                                :class="{ 'seg-btn-active': adjust.mode === 'set' }"
                                @click="setAdjustMode('set')"
                            >Setează la</button>
                            <button
                                type="button"
                                class="seg-btn"
                                :class="{ 'seg-btn-active': adjust.mode === 'add' }"
                                @click="setAdjustMode('add')"
                            >Adaugă / scade</button>
                        </div>

                        <label class="field">
                            <span class="field-label">
                                {{ adjust.mode === 'set' ? 'Cantitate nouă în stoc' : 'Cantitate de adăugat (negativ = scade)' }}
                            </span>
                            <input
                                v-model.number="adjust.value"
                                type="number"
                                step="any"
                                class="field-input"
                                :placeholder="adjust.mode === 'set' ? 'ex. 50' : 'ex. 20'"
                            />
                            <span class="field-hint">
                                {{ adjust.mode === 'add'
                                    ? `Rezultat: ${formatQty(adjust.currentQty + (adjust.value || 0))} ${unitLabel(adjust.unit)}`
                                    : 'Declară numărul real din stoc.' }}
                            </span>
                        </label>

                        <div class="flex items-center justify-end gap-2 pt-2">
                            <button type="button" class="pill-btn pill-btn-ghost" @click="closeAdjust">Anulează</button>
                            <button type="submit" class="pill-btn" :disabled="adjust.submitting">
                                {{ adjust.submitting ? 'Salvez...' : 'Salvează' }}
                            </button>
                        </div>
                    </form>
                </div>
            </div>
        </Teleport>
    </div>
</template>

<style scoped>
.field {
    display: flex;
    flex-direction: column;
    gap: 6px;
}

.pill-btn-ghost {
    background: transparent;
    color: var(--ink);
    border: 1px solid var(--ink12);
}

/* ── Inline header stats ──────────────────────────────────────────────── */
.stats-inline {
    display: flex;
    align-items: baseline;
    gap: 8px;
    flex-wrap: wrap;
    margin-top: 10px;
    font-family: 'JetBrains Mono', ui-monospace, monospace;
    font-size: 12px;
    color: var(--ink70);
    text-transform: uppercase;
    letter-spacing: 0.12em;
}
.stat-inline strong { color: var(--ink); font-weight: 700; margin-right: 4px; }
.stat-inline-warn strong { color: var(--accent); }
.stats-inline-sep { color: var(--ink40); }

/* ── Toolbar ──────────────────────────────────────────────────────────── */
.toolbar {
    position: sticky;
    top: 12px;
    z-index: 20;
    display: grid;
    grid-template-columns: 1fr auto;
    align-items: center;
    gap: 10px;
    padding: 8px 12px;
    margin-bottom: 12px;
    background: color-mix(in srgb, var(--paperHi) 92%, transparent);
    backdrop-filter: blur(10px);
    -webkit-backdrop-filter: blur(10px);
    border-radius: 14px;
}
@media (max-width: 720px) {
    .toolbar { grid-template-columns: 1fr; position: static; }
}
.toolbar-search { position: relative; display: flex; align-items: center; }
.toolbar-search-icon {
    position: absolute;
    left: 14px;
    color: var(--ink40);
    font-size: 16px;
    pointer-events: none;
}
.toolbar-search-input {
    width: 100%;
    background: var(--paper);
    color: var(--ink);
    border: 1px solid var(--ink12);
    border-radius: 999px;
    padding: 9px 36px 9px 36px;
    font-size: 14px;
    font-family: inherit;
    transition: border-color 0.15s ease, background 0.15s ease;
}
.toolbar-search-input:focus { outline: none; border-color: var(--ink); background: var(--paperHi); }
.toolbar-search-clear {
    position: absolute;
    right: 10px;
    width: 22px;
    height: 22px;
    border: none;
    border-radius: 50%;
    background: var(--ink06);
    color: var(--ink);
    cursor: pointer;
    display: grid;
    place-items: center;
    font-size: 16px;
    line-height: 1;
}
.toolbar-search-clear:hover { background: var(--ink12); }
.toolbar-filters {
    display: inline-flex;
    padding: 3px;
    background: var(--paper);
    border: 1px solid var(--ink12);
    border-radius: 999px;
    gap: 2px;
}
.filter-chip {
    border: none;
    background: transparent;
    padding: 6px 14px;
    border-radius: 999px;
    font-family: 'JetBrains Mono', ui-monospace, monospace;
    font-size: 11px;
    text-transform: uppercase;
    letter-spacing: 0.14em;
    color: var(--ink70);
    cursor: pointer;
    transition: background 0.15s ease, color 0.15s ease;
}
.filter-chip:hover { color: var(--ink); }
.filter-chip-active { background: var(--ink); color: var(--paperHi); }

/* ── Ingredient row ───────────────────────────────────────────────────── */
.ing-row {
    display: flex;
    gap: 12px;
    align-items: center;
}
.ing-row-low { border-color: var(--accentBorder); }
.ing-meta { font-size: 11px; color: var(--ink40); }
.ing-qty {
    display: flex;
    align-items: baseline;
    gap: 4px;
    flex-shrink: 0;
    min-width: 84px;
    justify-content: flex-end;
}
.ing-qty-num {
    font-family: 'JetBrains Mono', ui-monospace, monospace;
    font-size: 18px;
    font-weight: 700;
    color: var(--ink);
}
.ing-qty-num.ing-qty-low { color: var(--accent); }
.ing-qty-unit {
    font-family: 'JetBrains Mono', ui-monospace, monospace;
    font-size: 11px;
    color: var(--ink40);
}
.low-pill {
    display: inline-flex;
    align-items: center;
    padding: 2px 8px;
    border-radius: 999px;
    background: var(--accentSoft);
    color: var(--accent);
    border: 1px solid var(--accentBorder);
    font-family: 'JetBrains Mono', ui-monospace, monospace;
    font-size: 10px;
    text-transform: uppercase;
    letter-spacing: 0.14em;
}
.linked-tag {
    display: inline-flex;
    align-items: center;
    padding: 2px 8px;
    border-radius: 999px;
    background: var(--ink06);
    color: var(--ink70);
    font-family: 'JetBrains Mono', ui-monospace, monospace;
    font-size: 10px;
    text-transform: uppercase;
    letter-spacing: 0.14em;
}

/* ── Segmented toggle (adjust modal) ──────────────────────────────────── */
.seg {
    display: inline-flex;
    width: 100%;
    padding: 3px;
    background: var(--paper);
    border: 1px solid var(--ink12);
    border-radius: 12px;
    gap: 2px;
}
.seg-btn {
    flex: 1;
    border: none;
    background: transparent;
    padding: 8px 12px;
    border-radius: 10px;
    font-size: 13px;
    color: var(--ink70);
    cursor: pointer;
    transition: background 0.15s ease, color 0.15s ease;
}
.seg-btn:hover { color: var(--ink); }
.seg-btn-active { background: var(--ink); color: var(--paperHi); }

/* ── Modal (scoped copy of the shared dialog styling) ─────────────────── */
.modal-backdrop {
    position: fixed;
    inset: 0;
    background: rgba(0, 0, 0, 0.45);
    backdrop-filter: blur(6px);
    -webkit-backdrop-filter: blur(6px);
    z-index: 70;
    display: grid;
    place-items: center;
    padding: 20px;
    overflow-y: auto;
}
.modal-card {
    background: var(--paperHi);
    color: var(--ink);
    border: 1px solid var(--ink12);
    border-radius: 18px;
    padding: 22px;
    width: 100%;
    max-width: 520px;
    box-shadow: var(--shadow-soft);
    max-height: calc(100vh - 40px);
    overflow-y: auto;
}
.modal-close {
    width: 36px;
    height: 36px;
    border-radius: 50%;
    background: var(--ink06);
    color: var(--ink);
    border: none;
    font-size: 22px;
    line-height: 1;
    cursor: pointer;
    display: grid;
    place-items: center;
}
.modal-close:hover { background: var(--ink12); }

/* ── Empty state ──────────────────────────────────────────────────────── */
.empty-state {
    padding: 40px 20px;
    text-align: center;
    border: 1px dashed var(--ink12);
    border-radius: 16px;
    background: var(--paperHi);
}
.empty-state-icon {
    width: 48px;
    height: 48px;
    border-radius: 50%;
    background: var(--ink06);
    color: var(--ink40);
    display: grid;
    place-items: center;
    font-size: 22px;
    margin: 0 auto 12px;
}
</style>
