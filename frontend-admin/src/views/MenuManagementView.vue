<script setup lang="ts">
import { onMounted, ref, computed, reactive, watch, nextTick } from 'vue'
import { RouterLink } from 'vue-router'
import BrandSelect from '@/components/BrandSelect.vue'
import { useMenuManagementStore } from '@/stores/useMenuManagementStore'
import { useInventoryStore } from '@/stores/useInventoryStore'
import { useToast } from '@/composables/useToast'
import { resolveMediaUrl } from '@/services/mediaUrl'
import { apiClient, type SetProductStockPayload } from '@/services/api-client'
import type { AdminMenuCategoryNode, AdminProduct, StockTrackingMode, StockUnitCode } from '@shared/types/models'

const store = useMenuManagementStore()
const inventoryStore = useInventoryStore()
const toast = useToast()

onMounted(() => {
    store.fetchMenu()
    inventoryStore.fetchIngredients()
})

// ---------- Stock / recipe helpers ----------
const STOCK_MODE_OPTIONS = [
    { value: 'untracked', label: 'Fără urmărire stoc' },
    { value: 'recipe', label: 'Rețetă · din ingrediente' },
    { value: 'simple', label: 'Stoc simplu · la bucată' },
]
const UNIT_OPTIONS = [
    { value: 'piece', label: 'buc · bucăți' },
    { value: 'gram', label: 'g · grame' },
    { value: 'kilogram', label: 'kg · kilograme' },
    { value: 'milliliter', label: 'ml · mililitri' },
    { value: 'liter', label: 'l · litri' },
]
const UNIT_LABELS: Record<string, string> = {
    piece: 'buc', gram: 'g', kilogram: 'kg', milliliter: 'ml', liter: 'l',
}
const ingredientOptions = computed(() =>
    inventoryStore.standaloneIngredients.map((i) => ({
        value: i.id,
        label: `${i.name} (${UNIT_LABELS[i.unit] ?? i.unit})`,
    })),
)
function ingredientUnit(id: string): string {
    const ing = inventoryStore.standaloneIngredients.find((i) => i.id === id)
    return ing ? (UNIT_LABELS[ing.unit] ?? ing.unit) : ''
}

// ---------- Category form ----------
type CategoryFormMode = 'create' | 'edit'
const categoryForm = reactive({
    open: false,
    mode: 'create' as CategoryFormMode,
    id: '' as string,
    name: '',
    sortOrder: null as number | null,
    imageUrl: '',
    submitting: false,
})

function openCreateCategory() {
    categoryForm.open = true
    categoryForm.mode = 'create'
    categoryForm.id = ''
    categoryForm.name = ''
    categoryForm.sortOrder = null
    categoryForm.imageUrl = ''
}

function openEditCategory(cat: AdminMenuCategoryNode) {
    categoryForm.open = true
    categoryForm.mode = 'edit'
    categoryForm.id = cat.id
    categoryForm.name = cat.name
    categoryForm.sortOrder = cat.sortOrder
    categoryForm.imageUrl = cat.imageUrl ?? ''
}

function closeCategoryForm() {
    categoryForm.open = false
}

async function submitCategoryForm() {
    const name = categoryForm.name.trim()
    if (!name) {
        toast.error('Numele categoriei este obligatoriu.')
        return
    }
    categoryForm.submitting = true
    try {
        const payload = {
            name,
            sortOrder: categoryForm.sortOrder,
            imageUrl: categoryForm.imageUrl.trim() || null,
        }
        if (categoryForm.mode === 'create') {
            await store.createCategory(payload)
            toast.success('Categoria a fost adăugată.')
        } else {
            await store.updateCategory(categoryForm.id, payload)
            toast.success('Categoria a fost actualizată.')
        }
        closeCategoryForm()
    } catch (e: any) {
        toast.error(e?.message ?? 'Eroare la salvarea categoriei.')
    } finally {
        categoryForm.submitting = false
    }
}

async function deleteCategory(cat: AdminMenuCategoryNode) {
    if (cat.products.length > 0) {
        toast.error('Categoria încă are produse. Mută-le sau șterge-le mai întâi.')
        return
    }
    if (!window.confirm(`Sigur ștergi categoria „${cat.name}”?`)) return
    try {
        await store.deleteCategory(cat.id)
        toast.success('Categoria a fost ștearsă.')
    } catch (e: any) {
        toast.error(e?.message ?? 'Eroare la ștergerea categoriei.')
    }
}

// ---------- Product form ----------
type ProductFormMode = 'create' | 'edit'
const productForm = reactive({
    open: false,
    mode: 'create' as ProductFormMode,
    id: '' as string,
    categoryId: '' as string,
    name: '',
    description: '',
    price: 0 as number,
    imageUrl: '',
    isAvailable: true,
    submitting: false,
    // Stock tracking
    stockMode: 'untracked' as StockTrackingMode,
    stockLoading: false,
    recipeLines: [] as { ingredientId: string; quantityPerPortion: number }[],
    simpleQuantity: 0 as number,
    simpleUnit: 'piece' as StockUnitCode,
    simpleThreshold: 0 as number,
})

function resetStockFields() {
    productForm.stockMode = 'untracked'
    productForm.stockLoading = false
    productForm.recipeLines = []
    productForm.simpleQuantity = 0
    productForm.simpleUnit = 'piece'
    productForm.simpleThreshold = 0
}

function addRecipeLine() {
    productForm.recipeLines.push({ ingredientId: '', quantityPerPortion: 1 })
}
function removeRecipeLine(idx: number) {
    productForm.recipeLines.splice(idx, 1)
}

function openCreateProduct(categoryId: string) {
    productForm.open = true
    productForm.mode = 'create'
    productForm.id = ''
    productForm.categoryId = categoryId
    productForm.name = ''
    productForm.description = ''
    productForm.price = 0
    productForm.imageUrl = ''
    productForm.isAvailable = true
    resetStockFields()
    if (inventoryStore.ingredients.length === 0) inventoryStore.fetchIngredients()
}

async function openEditProduct(p: AdminProduct) {
    productForm.open = true
    productForm.mode = 'edit'
    productForm.id = p.id
    productForm.categoryId = p.categoryId
    productForm.name = p.name
    productForm.description = p.description ?? ''
    productForm.price = p.price
    productForm.imageUrl = p.imageUrl ?? ''
    productForm.isAvailable = p.isAvailable
    resetStockFields()
    if (inventoryStore.ingredients.length === 0) inventoryStore.fetchIngredients()

    // Load the product's current stock configuration.
    productForm.stockLoading = true
    try {
        const stock = await apiClient.getProductStock(p.id)
        productForm.stockMode = stock.mode
        if (stock.mode === 'recipe') {
            productForm.recipeLines = stock.recipe.map((r) => ({
                ingredientId: r.ingredientId,
                quantityPerPortion: r.quantityPerPortion,
            }))
        } else if (stock.mode === 'simple') {
            productForm.simpleQuantity = stock.simpleQuantity ?? 0
            productForm.simpleUnit = stock.simpleUnit ?? 'piece'
            productForm.simpleThreshold = stock.simpleThreshold ?? 0
        }
    } catch (e: any) {
        toast.error(e?.message ?? 'Nu am putut încărca stocul produsului.')
    } finally {
        productForm.stockLoading = false
    }
}

function closeProductForm() {
    productForm.open = false
}

function buildStockPayload(): SetProductStockPayload {
    if (productForm.stockMode === 'recipe') {
        return {
            mode: 'recipe',
            recipe: productForm.recipeLines.map((l) => ({
                ingredientId: l.ingredientId,
                quantityPerPortion: l.quantityPerPortion,
            })),
        }
    }
    if (productForm.stockMode === 'simple') {
        return {
            mode: 'simple',
            simpleQuantity: productForm.simpleQuantity,
            simpleUnit: productForm.simpleUnit,
            simpleThreshold: productForm.simpleThreshold,
        }
    }
    return { mode: 'untracked' }
}

function validateStock(): string | null {
    if (productForm.stockMode === 'recipe') {
        for (const line of productForm.recipeLines) {
            if (!line.ingredientId) return 'Alege un ingredient pentru fiecare rând din rețetă.'
            if (!(line.quantityPerPortion > 0)) return 'Cantitatea pe porție trebuie să fie mai mare decât 0.'
        }
        const ids = productForm.recipeLines.map((l) => l.ingredientId)
        if (new Set(ids).size !== ids.length) return 'Un ingredient apare de două ori în rețetă.'
    }
    if (productForm.stockMode === 'simple' && productForm.simpleQuantity < 0) {
        return 'Cantitatea nu poate fi negativă.'
    }
    return null
}

async function submitProductForm() {
    const name = productForm.name.trim()
    if (!name) {
        toast.error('Numele produsului este obligatoriu.')
        return
    }
    if (!(productForm.price > 0)) {
        toast.error('Prețul trebuie să fie mai mare decât 0.')
        return
    }
    if (!productForm.categoryId) {
        toast.error('Selectează o categorie.')
        return
    }
    const stockError = validateStock()
    if (stockError) {
        toast.error(stockError)
        return
    }

    productForm.submitting = true
    let productId = productForm.id
    let productSaved = false
    try {
        const description = productForm.description.trim()
        const imageUrl = productForm.imageUrl.trim()
        if (productForm.mode === 'create') {
            const created = await store.createProduct({
                categoryId: productForm.categoryId,
                name,
                description: description || null,
                price: productForm.price,
                imageUrl: imageUrl || null,
                isAvailable: productForm.isAvailable,
            })
            productId = created.id
        } else {
            await store.updateProduct(productForm.id, {
                categoryId: productForm.categoryId,
                name,
                description,
                price: productForm.price,
                imageUrl,
                isAvailable: productForm.isAvailable,
            })
        }
        productSaved = true

        // Persist stock mode + recipe / simple stock. Refresh the inventory store so a newly
        // created simple-product stock row shows up in Inventar without a manual reload.
        await apiClient.setProductStock(productId, buildStockPayload())
        inventoryStore.fetchIngredients()

        toast.success(productForm.mode === 'create' ? 'Produsul a fost adăugat.' : 'Produsul a fost actualizat.')
        closeProductForm()
    } catch (e: any) {
        if (productSaved) {
            // The product itself saved; only the stock step failed. Don't trap the user (re-submitting
            // a create would duplicate the product) — surface the issue and close.
            toast.error('Produsul a fost salvat, dar stocul nu a putut fi actualizat: ' + (e?.message ?? ''))
            closeProductForm()
        } else {
            toast.error(e?.message ?? 'Eroare la salvarea produsului.')
        }
    } finally {
        productForm.submitting = false
    }
}

async function toggleAvailability(p: AdminProduct) {
    try {
        if (p.isAvailable) {
            await store.softDeleteProduct(p.id)
            toast.success(`„${p.name}” a fost ascuns din meniu.`)
        } else {
            await store.restoreProduct(p.id)
            toast.success(`„${p.name}” a fost reactivat.`)
        }
    } catch (e: any) {
        toast.error(e?.message ?? 'Eroare la modificarea disponibilității.')
    }
}

// ---------- UI helpers ----------
const collapsed = ref<Record<string, boolean>>({})
function toggleCollapsed(id: string) {
    collapsed.value[id] = !collapsed.value[id]
}

// ---------- Toolbar: search, filter, collapse-all ----------
const searchQuery = ref('')
type ProductFilter = 'all' | 'active' | 'hidden'
const productFilter = ref<ProductFilter>('all')

const hasInitialisedCollapse = ref(false)
watch(
    () => store.categories,
    (cats) => {
        if (hasInitialisedCollapse.value || cats.length === 0) return
        const shouldCollapseByDefault = cats.length >= 4
        for (const c of cats) {
            if (!(c.id in collapsed.value)) collapsed.value[c.id] = shouldCollapseByDefault
        }
        hasInitialisedCollapse.value = true
    },
    { immediate: true },
)

function matchesSearch(p: AdminProduct, q: string): boolean {
    if (!q) return true
    const needle = q.toLowerCase()
    return (
        p.name.toLowerCase().includes(needle) ||
        (p.description?.toLowerCase().includes(needle) ?? false)
    )
}

function matchesFilter(p: AdminProduct): boolean {
    if (productFilter.value === 'active') return p.isAvailable
    if (productFilter.value === 'hidden') return !p.isAvailable
    return true
}

/** Escape a URL for safe interpolation inside a single-quoted CSS `url('…')`. */
function cssEscape(value: string): string {
    return value.replace(/[\\'\n\r]/g, (ch) => `\\${ch}`)
}

function backgroundImageStyle(url: string | null | undefined) {
    const resolved = resolveMediaUrl(url)
    if (!resolved) return {}
    // Category/product image URLs can originate from the AI import pipeline, so
    // only allow http(s) absolute URLs or app-relative paths — reject anything
    // else (javascript:, data:, CSS-injection tokens) rather than emit it.
    if (!/^https?:\/\//i.test(resolved) && !resolved.startsWith('/')) return {}
    return { backgroundImage: `url('${cssEscape(resolved)}')` }
}

const visibleCategories = computed(() => {
    const q = searchQuery.value.trim()
    const hasQuery = q.length > 0
    return store.categories
        .map((cat) => {
            const filteredProducts = cat.products.filter(
                (p) => matchesSearch(p, q) && matchesFilter(p),
            )
            const matchesCategoryName = hasQuery && cat.name.toLowerCase().includes(q.toLowerCase())
            return {
                cat,
                products: filteredProducts,
                matchesCategoryName,
            }
        })
        .filter((entry) => {
            if (!hasQuery && productFilter.value === 'all') return true
            return entry.products.length > 0 || entry.matchesCategoryName
        })
})

const filteredProductCount = computed(() =>
    visibleCategories.value.reduce((sum, e) => sum + e.products.length, 0),
)

const hiddenProductCount = computed(() =>
    store.categories.reduce(
        (sum, c) => sum + c.products.filter((p) => !p.isAvailable).length,
        0,
    ),
)

function hiddenCountFor(cat: AdminMenuCategoryNode): number {
    return cat.products.filter((p) => !p.isAvailable).length
}

const isFiltering = computed(
    () => searchQuery.value.trim().length > 0 || productFilter.value !== 'all',
)

function clearFilters() {
    searchQuery.value = ''
    productFilter.value = 'all'
}

function collapseAll() {
    for (const c of store.categories) collapsed.value[c.id] = true
}
function expandAll() {
    for (const c of store.categories) collapsed.value[c.id] = false
}
const allCollapsed = computed(
    () =>
        store.categories.length > 0 &&
        store.categories.every((c) => collapsed.value[c.id]),
)

watch(isFiltering, (filtering) => {
    if (filtering) expandAll()
})

async function jumpToCategory(id: string) {
    if (collapsed.value[id]) collapsed.value[id] = false
    await nextTick()
    const el = document.getElementById(`cat-${id}`)
    if (el) el.scrollIntoView({ behavior: 'smooth', block: 'start' })
}

// ---------- Drag and drop ----------
type DragState = {
    categoryId: string
    productId: string
} | null
const dragState = ref<DragState>(null)
const dragOverProductId = ref<string | null>(null)

function onDragStart(event: DragEvent, categoryId: string, productId: string) {
    dragState.value = { categoryId, productId }
    if (event.dataTransfer) {
        event.dataTransfer.effectAllowed = 'move'
        event.dataTransfer.setData('text/plain', productId)
    }
}

function onDragOver(event: DragEvent, categoryId: string, productId: string) {
    if (!dragState.value || dragState.value.categoryId !== categoryId) return
    event.preventDefault()
    if (event.dataTransfer) event.dataTransfer.dropEffect = 'move'
    dragOverProductId.value = productId
}

function onDragLeave(productId: string) {
    if (dragOverProductId.value === productId) {
        dragOverProductId.value = null
    }
}

async function onDrop(event: DragEvent, categoryId: string, targetProductId: string) {
    event.preventDefault()
    const drag = dragState.value
    dragState.value = null
    dragOverProductId.value = null
    if (!drag || drag.categoryId !== categoryId) return
    if (drag.productId === targetProductId) return

    const cat = store.categories.find((c) => c.id === categoryId)
    if (!cat) return

    const ids = cat.products.map((p) => p.id)
    const fromIdx = ids.indexOf(drag.productId)
    const toIdx = ids.indexOf(targetProductId)
    if (fromIdx === -1 || toIdx === -1) return

    const [moved] = ids.splice(fromIdx, 1)
    ids.splice(toIdx, 0, moved)

    try {
        await store.reorderProducts(categoryId, ids)
    } catch (e: any) {
        toast.error(e?.message ?? 'Eroare la reordonarea produselor.')
    }
}

function onDragEnd() {
    dragState.value = null
    dragOverProductId.value = null
}

// Touch-friendly reorder: HTML5 drag-and-drop never fires on touchscreens, so
// the up/down buttons drive the same reorder endpoint. Only offered while the
// list is unfiltered, where the displayed order matches the stored order.
async function moveProduct(categoryId: string, productId: string, dir: -1 | 1) {
    const cat = store.categories.find((c) => c.id === categoryId)
    if (!cat) return
    const ids = cat.products.map((p) => p.id)
    const idx = ids.indexOf(productId)
    const target = idx + dir
    if (idx === -1 || target < 0 || target >= ids.length) return
    ;[ids[idx], ids[target]] = [ids[target], ids[idx]]
    try {
        await store.reorderProducts(categoryId, ids)
    } catch (e: any) {
        toast.error(e?.message ?? 'Eroare la reordonarea produselor.')
    }
}

const priceFormatter = computed(
    () => new Intl.NumberFormat('ro-RO', { style: 'currency', currency: 'RON' })
)
function formatPrice(n: number) {
    return priceFormatter.value.format(n)
}
</script>

<template>
    <div class="max-w-6xl mx-auto relative">
        <!-- Compact unified header -->
        <header class="page-head">
            <div>
                <div class="page-head-eyebrow">Bucătărie · catalog</div>
                <h1 class="page-head-title">
                    Meniul <span class="accent">curții.</span>
                </h1>
                <div class="stats-inline">
                    <span class="stat-inline">
                        <strong>{{ store.totalCategories }}</strong> categorii
                    </span>
                    <span class="stats-inline-sep" aria-hidden="true">·</span>
                    <span class="stat-inline">
                        <strong>{{ store.availableProducts }}</strong> active
                    </span>
                    <template v-if="hiddenProductCount > 0">
                        <span class="stats-inline-sep" aria-hidden="true">·</span>
                        <span class="stat-inline stat-inline-warn">
                            <strong>{{ hiddenProductCount }}</strong> ascunse
                        </span>
                    </template>
                </div>
            </div>

            <div class="page-head-actions">
                <RouterLink
                    to="/menu/import"
                    class="btn-ghost whitespace-nowrap"
                >
                    <span aria-hidden="true">⊕</span>
                    <span>Importă</span>
                </RouterLink>
                <button class="pill-btn whitespace-nowrap" @click="openCreateCategory">
                    <span aria-hidden="true">+</span>
                    <span>Adaugă categorie</span>
                </button>
            </div>
        </header>

        <!-- Sticky toolbar: search + filter + collapse -->
        <div class="toolbar card-paper">
            <div class="toolbar-search">
                <span class="toolbar-search-icon" aria-hidden="true">⌕</span>
                <input
                    v-model="searchQuery"
                    type="text"
                    class="toolbar-search-input"
                    placeholder="Caută produs sau categorie…"
                />
                <button
                    v-if="searchQuery"
                    class="toolbar-search-clear"
                    type="button"
                    @click="searchQuery = ''"
                    aria-label="Șterge căutarea"
                >
                    ×
                </button>
            </div>

            <div class="toolbar-filters" role="tablist" aria-label="Filtrează produsele">
                <button
                    class="filter-chip"
                    :class="{ 'filter-chip-active': productFilter === 'all' }"
                    @click="productFilter = 'all'"
                    type="button"
                >Toate</button>
                <button
                    class="filter-chip"
                    :class="{ 'filter-chip-active': productFilter === 'active' }"
                    @click="productFilter = 'active'"
                    type="button"
                >Active</button>
                <button
                    class="filter-chip"
                    :class="{ 'filter-chip-active': productFilter === 'hidden' }"
                    @click="productFilter = 'hidden'"
                    type="button"
                >Ascunse</button>
            </div>

            <div class="toolbar-actions">
                <button
                    class="pill-btn pill-btn-ghost text-sm"
                    type="button"
                    @click="allCollapsed ? expandAll() : collapseAll()"
                    :disabled="store.categories.length === 0"
                >
                    {{ allCollapsed ? 'Extinde tot' : 'Restrânge tot' }}
                </button>
            </div>
        </div>

        <div
            v-if="isFiltering"
            class="filter-status"
        >
            <span class="font-mono text-xs uppercase tracking-[0.18em] text-ink-70">
                {{ filteredProductCount }} produse găsite
            </span>
            <button class="pill-btn pill-btn-ghost text-sm" type="button" @click="clearFilters">
                Resetează filtrele
            </button>
        </div>

        <div v-if="store.error" class="alert alert-error mb-4">
            <span class="alert-icon" aria-hidden="true">⚠</span>
            <div class="alert-body">{{ store.error }}</div>
        </div>

        <!-- Skeleton state -->
        <div
            v-if="store.isLoading && store.categories.length === 0"
            class="card-paper p-6 space-y-3"
        >
            <div class="h-5 bg-ink-12 rounded-full w-1/3 animate-pulse"></div>
            <div class="h-4 bg-ink-12 rounded-full w-3/4 animate-pulse"></div>
            <div class="h-4 bg-ink-12 rounded-full w-1/2 animate-pulse"></div>
        </div>

        <!-- Empty state -->
        <div
            v-else-if="store.categories.length === 0"
            class="card-paper p-8 text-ink-70 italic"
        >
            Nicio categorie încă. Apasă „Adaugă categorie" pentru a începe.
        </div>

        <!-- No-results state -->
        <div
            v-else-if="visibleCategories.length === 0"
            class="empty-state"
        >
            <div class="empty-state-icon" aria-hidden="true">∅</div>
            <div class="text-ink text-base mb-1">Niciun rezultat pentru filtrele curente</div>
            <p class="text-ink-70 text-sm mb-3">Schimbă căutarea sau resetează filtrele pentru a vedea toate produsele.</p>
            <button class="btn-ghost" type="button" @click="clearFilters">
                Resetează filtrele
            </button>
        </div>

        <!-- Two-column layout: jump nav + categories -->
        <div v-else class="menu-layout">
            <!-- Category quick-jump (sticky on desktop) -->
            <aside class="cat-nav" aria-label="Navigare categorii">
                <div class="cat-nav-title eyebrow">categorii</div>
                <ul class="cat-nav-list">
                    <li v-for="entry in visibleCategories" :key="entry.cat.id">
                        <button
                            type="button"
                            class="cat-nav-item"
                            @click="jumpToCategory(entry.cat.id)"
                        >
                            <span class="cat-nav-name">{{ entry.cat.name }}</span>
                            <span class="cat-nav-count">{{ entry.products.length }}</span>
                        </button>
                    </li>
                </ul>
            </aside>

            <!-- Category list -->
            <div class="menu-main space-y-5">
                <section
                    v-for="entry in visibleCategories"
                    :key="entry.cat.id"
                    :id="`cat-${entry.cat.id}`"
                    class="card-paper p-5 relative scroll-mt-24"
                >
                    <!-- Category header -->
                    <div
                        class="cat-header"
                        :class="{ 'cat-header-collapsed': collapsed[entry.cat.id] }"
                    >
                        <button
                            class="cat-toggle"
                            type="button"
                            @click="toggleCollapsed(entry.cat.id)"
                            :aria-expanded="!collapsed[entry.cat.id]"
                            :aria-label="collapsed[entry.cat.id] ? 'Extinde categoria' : 'Restrânge categoria'"
                        >
                            <span class="cat-toggle-icon" aria-hidden="true">
                                {{ collapsed[entry.cat.id] ? '▸' : '▾' }}
                            </span>
                        </button>

                        <div
                            class="cat-thumb"
                            :style="backgroundImageStyle(entry.cat.imageUrl)"
                            :aria-label="entry.cat.imageUrl ? 'imagine categorie' : 'fără imagine'"
                        >
                            <span v-if="!entry.cat.imageUrl" class="font-mono text-xs text-ink-40">·</span>
                        </div>

                        <div class="cat-meta min-w-0 flex-1">
                            <h2 class="display-title text-xl md:text-2xl truncate">
                                {{ entry.cat.name }}
                            </h2>
                            <div class="cat-meta-line">
                                <span class="font-mono text-xs text-ink-70">
                                    {{ entry.cat.products.length }} produse
                                </span>
                                <span v-if="hiddenCountFor(entry.cat) > 0" class="cat-hidden-pill">
                                    {{ hiddenCountFor(entry.cat) }} ascunse
                                </span>
                                <span class="font-mono text-xs text-ink-40">
                                    · ordine {{ entry.cat.sortOrder ?? '—' }}
                                </span>
                            </div>
                        </div>

                        <div class="cat-actions">
                            <button
                                class="btn-ghost btn-add-product"
                                @click="openCreateProduct(entry.cat.id)"
                                title="Adaugă produs"
                            >
                                + Produs
                            </button>
                            <button
                                class="icon-btn"
                                @click="openEditCategory(entry.cat)"
                                title="Editează categoria"
                                aria-label="Editează categoria"
                            >✎</button>
                            <button
                                class="icon-btn icon-btn-danger"
                                :disabled="entry.cat.products.length > 0"
                                :title="entry.cat.products.length > 0 ? 'Categoria conține produse' : 'Șterge categoria'"
                                :aria-label="entry.cat.products.length > 0 ? 'Nu se poate șterge' : 'Șterge categoria'"
                                @click="deleteCategory(entry.cat)"
                            >🗑</button>
                        </div>
                    </div>

                    <!-- Product list -->
                    <div v-if="!collapsed[entry.cat.id]" class="mt-4">
                        <div
                            v-if="entry.cat.products.length === 0"
                            class="card-white p-5 text-ink-70 italic text-sm"
                        >
                            Nicio variantă încă. Apasă „+ Produs" pentru a adăuga.
                        </div>
                        <div
                            v-else-if="entry.products.length === 0"
                            class="card-white p-5 text-ink-70 italic text-sm"
                        >
                            Niciun produs nu corespunde filtrelor curente.
                        </div>

                        <div v-else class="grid gap-2">
                            <article
                                v-for="(p, pIdx) in entry.products"
                                :key="p.id"
                                class="card-white p-3 flex gap-3 items-start prod-row"
                                :class="{
                                    'prod-row-dragging': dragState?.productId === p.id,
                                    'prod-row-drop-target': dragOverProductId === p.id && dragState && dragState.productId !== p.id,
                                    'prod-row-hidden': !p.isAvailable,
                                }"
                                @dragover="onDragOver($event, entry.cat.id, p.id)"
                                @dragleave="onDragLeave(p.id)"
                                @drop="onDrop($event, entry.cat.id, p.id)"
                            >
                                <div v-if="!isFiltering" class="prod-reorder shrink-0">
                                    <button
                                        type="button"
                                        class="reorder-btn"
                                        :disabled="pIdx === 0"
                                        title="Mută mai sus"
                                        aria-label="Mută mai sus"
                                        @click="moveProduct(entry.cat.id, p.id, -1)"
                                    >▲</button>
                                    <div
                                        class="drag-handle"
                                        draggable="true"
                                        title="Trage pentru a reordona"
                                        @dragstart="onDragStart($event, entry.cat.id, p.id)"
                                        @dragend="onDragEnd"
                                    >
                                        <span aria-hidden="true">⋮⋮</span>
                                    </div>
                                    <button
                                        type="button"
                                        class="reorder-btn"
                                        :disabled="pIdx === entry.products.length - 1"
                                        title="Mută mai jos"
                                        aria-label="Mută mai jos"
                                        @click="moveProduct(entry.cat.id, p.id, 1)"
                                    >▼</button>
                                </div>

                                <div
                                    class="prod-thumb"
                                    :style="backgroundImageStyle(p.imageUrl)"
                                >
                                    <span v-if="!p.imageUrl" class="font-mono text-[10px] text-ink-40">no img</span>
                                </div>

                                <div class="flex-1 min-w-0">
                                    <div class="flex items-center gap-2 flex-wrap mb-1">
                                        <h3 class="font-semibold text-ink text-base truncate">
                                            {{ p.name }}
                                        </h3>
                                        <span
                                            v-if="!p.isAvailable"
                                            class="tag-pill text-[10px]"
                                        >
                                            ascuns
                                        </span>
                                        <span class="font-mono text-[10px] text-ink-40">
                                            #{{ pIdx + 1 }}
                                        </span>
                                    </div>
                                    <p
                                        v-if="p.description"
                                        class="text-ink-70 text-sm leading-snug line-clamp-2"
                                    >
                                        {{ p.description }}
                                    </p>
                                    <p v-else class="text-ink-40 text-sm italic">
                                        fără descriere
                                    </p>
                                </div>

                                <div class="prod-actions flex flex-col items-end gap-2 shrink-0">
                                    <div class="font-mono text-base font-bold text-ink">
                                        {{ formatPrice(p.price) }}
                                    </div>
                                    <div class="flex items-center gap-1">
                                        <button
                                            class="icon-btn"
                                            @click="openEditProduct(p)"
                                            title="Editează produsul"
                                            aria-label="Editează produsul"
                                        >✎</button>
                                        <button
                                            class="icon-btn"
                                            @click="toggleAvailability(p)"
                                            :title="p.isAvailable ? 'Ascunde produsul' : 'Reactivează produsul'"
                                            :aria-label="p.isAvailable ? 'Ascunde produsul' : 'Reactivează produsul'"
                                        >{{ p.isAvailable ? '◐' : '●' }}</button>
                                    </div>
                                </div>
                            </article>
                        </div>
                    </div>
                </section>
            </div>
        </div>

        <!-- Category modal -->
        <Teleport to="body">
            <div v-if="categoryForm.open" class="modal-backdrop" @click.self="closeCategoryForm">
                <div class="modal-card">
                    <div class="flex items-center justify-between mb-5">
                        <div>
                            <div class="eyebrow mb-1">
                                {{ categoryForm.mode === 'create' ? 'categorie nouă' : 'editare categorie' }}
                            </div>
                            <h3 class="display-title text-2xl">
                                {{ categoryForm.mode === 'create' ? 'Adaugă categorie' : 'Editează categoria' }}
                            </h3>
                        </div>
                        <button class="modal-close" @click="closeCategoryForm" aria-label="Închide">×</button>
                    </div>

                    <form @submit.prevent="submitCategoryForm" class="space-y-4">
                        <label class="field">
                            <span class="field-label">Nume</span>
                            <input
                                v-model="categoryForm.name"
                                type="text"
                                class="field-input"
                                placeholder="ex. Aperitive"
                                maxlength="120"
                                required
                            />
                        </label>

                        <label class="field">
                            <span class="field-label">Ordine afișare</span>
                            <input
                                v-model.number="categoryForm.sortOrder"
                                type="number"
                                class="field-input"
                                placeholder="ex. 1"
                            />
                        </label>

                        <label class="field">
                            <span class="field-label">URL imagine</span>
                            <input
                                v-model="categoryForm.imageUrl"
                                type="text"
                                class="field-input"
                                placeholder="https://... sau /uploads/..."
                                maxlength="500"
                            />
                            <span class="field-hint">URL absolut sau cale /uploads. Lasă gol pentru a elimina.</span>
                        </label>

                        <div v-if="categoryForm.imageUrl" class="preview-box">
                            <img :src="resolveMediaUrl(categoryForm.imageUrl)" alt="previzualizare" />
                        </div>

                        <div class="flex items-center justify-end gap-2 pt-2">
                            <button type="button" class="pill-btn pill-btn-ghost" @click="closeCategoryForm">
                                Anulează
                            </button>
                            <button type="submit" class="pill-btn" :disabled="categoryForm.submitting">
                                {{ categoryForm.submitting ? 'Salvez...' : 'Salvează' }}
                            </button>
                        </div>
                    </form>
                </div>
            </div>
        </Teleport>

        <!-- Product modal -->
        <Teleport to="body">
            <div v-if="productForm.open" class="modal-backdrop" @click.self="closeProductForm">
                <div class="modal-card">
                    <div class="flex items-center justify-between mb-5">
                        <div>
                            <div class="eyebrow mb-1">
                                {{ productForm.mode === 'create' ? 'produs nou' : 'editare produs' }}
                            </div>
                            <h3 class="display-title text-2xl">
                                {{ productForm.mode === 'create' ? 'Adaugă produs' : 'Editează produsul' }}
                            </h3>
                        </div>
                        <button class="modal-close" @click="closeProductForm" aria-label="Închide">×</button>
                    </div>

                    <form @submit.prevent="submitProductForm" class="space-y-4">
                        <div class="field">
                            <span class="field-label">Categorie</span>
                            <BrandSelect
                                v-model="productForm.categoryId"
                                :options="store.categories.map(c => ({ value: c.id, label: c.name }))"
                                placeholder="— alege categoria —"
                                aria-label="Categorie"
                            />
                        </div>

                        <label class="field">
                            <span class="field-label">Nume</span>
                            <input
                                v-model="productForm.name"
                                type="text"
                                class="field-input"
                                placeholder="ex. Ciorbă de burtă"
                                maxlength="120"
                                required
                            />
                        </label>

                        <label class="field">
                            <span class="field-label">Descriere · preparare + gramaj</span>
                            <textarea
                                v-model="productForm.description"
                                class="field-input"
                                rows="5"
                                maxlength="1000"
                                placeholder="ex. Burtă fiartă încet în zeamă cu rădăcinoase, smântână și ardei iute, servită caldă. 350 g."
                            ></textarea>
                            <span class="field-hint">
                                {{ productForm.description.length }}/1000 · scrie modul de preparare și gramajul (ex. „350 g")
                            </span>
                        </label>

                        <div class="grid grid-cols-1 sm:grid-cols-2 gap-3">
                            <label class="field">
                                <span class="field-label">Preț (RON)</span>
                                <input
                                    v-model.number="productForm.price"
                                    type="number"
                                    step="0.01"
                                    min="0.01"
                                    class="field-input"
                                    placeholder="ex. 24.90"
                                    required
                                />
                            </label>
                            <div class="field">
                                <span class="field-label">Disponibil</span>
                                <BrandSelect
                                    :model-value="productForm.isAvailable ? 'true' : 'false'"
                                    :options="[
                                        { value: 'true', label: 'Da · vizibil în meniu' },
                                        { value: 'false', label: 'Nu · ascuns' },
                                    ]"
                                    aria-label="Disponibilitate"
                                    @update:model-value="(v) => productForm.isAvailable = v === 'true'"
                                />
                            </div>
                        </div>

                        <!-- Stock management -->
                        <div class="stock-section">
                            <div class="field">
                                <span class="field-label">Gestionare stoc</span>
                                <BrandSelect
                                    :model-value="productForm.stockMode"
                                    :options="STOCK_MODE_OPTIONS"
                                    aria-label="Mod urmărire stoc"
                                    @update:model-value="(v) => (productForm.stockMode = v as any)"
                                />
                                <span class="field-hint">Cum se scade stocul când produsul e trimis la bucătărie.</span>
                            </div>

                            <div v-if="productForm.stockLoading" class="stock-loading font-mono">
                                se încarcă stocul…
                            </div>

                            <!-- Recipe builder -->
                            <div v-else-if="productForm.stockMode === 'recipe'" class="field">
                                <span class="field-label">Rețetă · cantitate pe porție</span>
                                <div
                                    v-if="inventoryStore.standaloneIngredients.length === 0"
                                    class="recipe-empty"
                                >
                                    Niciun ingredient disponibil. Adaugă întâi ingrediente în
                                    <RouterLink to="/inventory" class="recipe-link">Inventar</RouterLink>.
                                </div>
                                <template v-else>
                                    <div
                                        v-for="(line, idx) in productForm.recipeLines"
                                        :key="idx"
                                        class="recipe-line"
                                    >
                                        <div class="recipe-line-ing">
                                            <BrandSelect
                                                v-model="line.ingredientId"
                                                :options="ingredientOptions"
                                                placeholder="— ingredient —"
                                                aria-label="Ingredient"
                                            />
                                        </div>
                                        <input
                                            v-model.number="line.quantityPerPortion"
                                            type="number"
                                            step="any"
                                            min="0"
                                            class="field-input recipe-line-qty"
                                            placeholder="cant."
                                        />
                                        <span class="recipe-line-unit">{{ ingredientUnit(line.ingredientId) }}</span>
                                        <button
                                            type="button"
                                            class="icon-btn icon-btn-danger"
                                            @click="removeRecipeLine(idx)"
                                            aria-label="Elimină ingredientul"
                                            title="Elimină"
                                        >×</button>
                                    </div>
                                    <button type="button" class="btn-ghost recipe-add" @click="addRecipeLine">
                                        + Adaugă ingredient
                                    </button>
                                </template>
                            </div>

                            <!-- Simple stock -->
                            <div v-else-if="productForm.stockMode === 'simple'" class="grid grid-cols-3 gap-3">
                                <label class="field">
                                    <span class="field-label">În stoc</span>
                                    <input
                                        v-model.number="productForm.simpleQuantity"
                                        type="number"
                                        step="any"
                                        min="0"
                                        class="field-input"
                                        placeholder="ex. 24"
                                    />
                                </label>
                                <div class="field">
                                    <span class="field-label">Unitate</span>
                                    <BrandSelect
                                        :model-value="productForm.simpleUnit"
                                        :options="UNIT_OPTIONS"
                                        aria-label="Unitate"
                                        @update:model-value="(v) => (productForm.simpleUnit = v as any)"
                                    />
                                </div>
                                <label class="field">
                                    <span class="field-label">Prag alertă</span>
                                    <input
                                        v-model.number="productForm.simpleThreshold"
                                        type="number"
                                        step="any"
                                        min="0"
                                        class="field-input"
                                        placeholder="ex. 5"
                                    />
                                </label>
                            </div>
                        </div>

                        <label class="field">
                            <span class="field-label">URL imagine</span>
                            <input
                                v-model="productForm.imageUrl"
                                type="text"
                                class="field-input"
                                placeholder="https://... sau /uploads/..."
                                maxlength="500"
                            />
                            <span class="field-hint">URL absolut sau cale /uploads. Lasă gol pentru a elimina.</span>
                        </label>

                        <div v-if="productForm.imageUrl" class="preview-box">
                            <img :src="resolveMediaUrl(productForm.imageUrl)" alt="previzualizare" />
                        </div>

                        <div class="flex items-center justify-end gap-2 pt-2">
                            <button type="button" class="pill-btn pill-btn-ghost" @click="closeProductForm">
                                Anulează
                            </button>
                            <button type="submit" class="pill-btn" :disabled="productForm.submitting">
                                {{ productForm.submitting ? 'Salvez...' : 'Salvează' }}
                            </button>
                        </div>
                    </form>
                </div>
            </div>
        </Teleport>
    </div>
</template>

<style scoped>
.pill-btn-ghost {
    background: transparent;
    color: var(--ink);
    border: 1px solid var(--ink12);
}
.pill-btn-danger {
    background: transparent;
    color: var(--accent);
    border: 1px solid var(--accentBorder);
}
.pill-btn:disabled {
    opacity: 0.5;
    cursor: not-allowed;
}

.cat-thumb {
    width: 64px;
    height: 64px;
    border-radius: 14px;
    background-color: var(--ink06);
    background-size: cover;
    background-position: center;
    border: 1px solid var(--ink12);
    display: grid;
    place-items: center;
    flex-shrink: 0;
}

.prod-thumb {
    width: 72px;
    height: 72px;
    border-radius: 12px;
    background-color: var(--ink06);
    background-size: cover;
    background-position: center;
    border: 1px solid var(--ink12);
    display: grid;
    place-items: center;
    flex-shrink: 0;
}

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
.modal-close:hover {
    background: var(--ink12);
}

.field {
    display: flex;
    flex-direction: column;
    gap: 6px;
}
.field-label {
    font-family: 'JetBrains Mono', ui-monospace, monospace;
    font-size: 11px;
    text-transform: uppercase;
    letter-spacing: 0.18em;
    color: var(--ink70);
}
.field-hint {
    font-size: 11px;
    color: var(--ink40);
}
.field-input {
    background: var(--paper);
    color: var(--ink);
    border: 1px solid var(--ink12);
    border-radius: 12px;
    padding: 10px 14px;
    font-size: 14px;
    font-family: inherit;
    transition: border-color 0.15s ease, background 0.15s ease;
    width: 100%;
}
.field-input:focus {
    outline: none;
    border-color: var(--ink);
    background: var(--paperHi);
}
textarea.field-input {
    resize: vertical;
    min-height: 90px;
    line-height: 1.5;
}

.preview-box {
    border: 1px solid var(--ink12);
    border-radius: 12px;
    padding: 6px;
    background: var(--paper);
    display: grid;
    place-items: center;
}
.preview-box img {
    max-width: 100%;
    max-height: 180px;
    border-radius: 8px;
    object-fit: cover;
}

/* ── Stock / recipe section ────────────────────────────────────────────── */
.stock-section {
    border: 1px solid var(--ink12);
    border-radius: 14px;
    padding: 14px;
    background: var(--paper);
    display: flex;
    flex-direction: column;
    gap: 12px;
}
.stock-loading {
    font-size: 12px;
    color: var(--ink40);
}
.recipe-line {
    display: flex;
    align-items: center;
    gap: 8px;
}
.recipe-line-ing {
    flex: 1;
    min-width: 0;
}
.recipe-line-qty {
    width: 92px;
    flex-shrink: 0;
}
.recipe-line-unit {
    width: 32px;
    flex-shrink: 0;
    font-family: 'JetBrains Mono', ui-monospace, monospace;
    font-size: 12px;
    color: var(--ink40);
}
.recipe-add {
    align-self: flex-start;
    padding: 8px 14px;
    font-size: 13px;
}
.recipe-empty {
    font-size: 13px;
    color: var(--ink70);
    padding: 10px 12px;
    border: 1px dashed var(--ink12);
    border-radius: 10px;
}
.recipe-link {
    color: var(--accent);
    text-decoration: underline;
}

.line-clamp-3 {
    display: -webkit-box;
    -webkit-line-clamp: 3;
    -webkit-box-orient: vertical;
    overflow: hidden;
}

.prod-row {
    transition: transform 0.12s ease, box-shadow 0.12s ease, border-color 0.12s ease;
}
.prod-row-dragging {
    opacity: 0.4 !important;
}
.prod-row-drop-target {
    border-color: var(--ink) !important;
    box-shadow: 0 0 0 2px var(--lime) inset;
    transform: translateY(-1px);
}

/* Reorder control: drag handle (mouse) flanked by up/down buttons (touch). */
.prod-reorder {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 3px;
    width: 30px;
}
.reorder-btn {
    width: 30px;
    height: 26px;
    border: 1px solid var(--ink12);
    border-radius: 8px;
    background: var(--paper);
    color: var(--ink70);
    cursor: pointer;
    display: grid;
    place-items: center;
    font-size: 10px;
    line-height: 1;
    transition: background 0.12s ease, color 0.12s ease;
}
.reorder-btn:hover:not(:disabled) {
    background: var(--ink06);
    color: var(--ink);
}
.reorder-btn:disabled {
    opacity: 0.3;
    cursor: not-allowed;
}
.drag-handle {
    width: 30px;
    flex: 1;
    min-height: 30px;
    border-radius: 8px;
    background: var(--ink06);
    color: var(--ink40);
    display: grid;
    place-items: center;
    font-size: 14px;
    line-height: 1;
    cursor: grab;
    user-select: none;
    border: 1px solid transparent;
    transition: background 0.15s ease, color 0.15s ease, border-color 0.15s ease;
}
.drag-handle:hover {
    background: var(--ink12);
    color: var(--ink);
    border-color: var(--ink12);
}
.drag-handle:active {
    cursor: grabbing;
}
/* Touch devices can't drag the handle — hide it and lean on the buttons. */
@media (hover: none) {
    .drag-handle { display: none; }
    .reorder-btn { height: 36px; }
}

/* ── Inline header stats (replaces chip strip) ─────────────────────── */
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
.stat-inline strong {
    color: var(--ink);
    font-weight: 700;
    margin-right: 4px;
}
.stat-inline-warn strong { color: var(--accent); }
.stats-inline-sep { color: var(--ink40); }

/* ── Empty state ───────────────────────────────────────────────────── */
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

/* ── Sticky toolbar ────────────────────────────────────────────────── */
.toolbar {
    position: sticky;
    top: 12px;
    z-index: 20;
    display: grid;
    grid-template-columns: 1fr auto auto;
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
    .toolbar {
        grid-template-columns: 1fr;
        position: static;
    }
}
.toolbar-search {
    position: relative;
    display: flex;
    align-items: center;
}
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
.toolbar-search-input:focus {
    outline: none;
    border-color: var(--ink);
    background: var(--paperHi);
}
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
.toolbar-search-clear:hover {
    background: var(--ink12);
}
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
.filter-chip:hover {
    color: var(--ink);
}
.filter-chip-active {
    background: var(--ink);
    color: var(--paperHi);
}
.toolbar-actions {
    display: flex;
    gap: 8px;
}

.filter-status {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 12px;
    margin-bottom: 16px;
    padding: 0 4px;
}

/* ── Two-column layout (jump nav + content) ────────────────────────── */
.menu-layout {
    display: grid;
    grid-template-columns: 220px minmax(0, 1fr);
    gap: 24px;
    align-items: start;
}
@media (max-width: 960px) {
    .menu-layout {
        grid-template-columns: 1fr;
    }
}

.cat-nav {
    position: sticky;
    top: 80px;
    max-height: calc(100vh - 100px);
    overflow-y: auto;
    padding: 14px;
    border: 1px solid var(--ink12);
    border-radius: 16px;
    background: var(--paperHi);
}
@media (max-width: 960px) {
    .cat-nav {
        position: static;
        max-height: none;
        display: flex;
        gap: 8px;
        overflow-x: auto;
        overflow-y: visible;
        padding: 10px;
    }
    .cat-nav-title {
        display: none;
    }
    .cat-nav-list {
        display: flex !important;
        gap: 6px;
        flex-direction: row !important;
    }
}
.cat-nav-title {
    margin-bottom: 10px;
}
.cat-nav-list {
    display: flex;
    flex-direction: column;
    gap: 2px;
    list-style: none;
    padding: 0;
    margin: 0;
}
.cat-nav-item {
    width: 100%;
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 8px;
    padding: 8px 10px;
    border-radius: 10px;
    border: none;
    background: transparent;
    color: var(--ink);
    font-size: 13px;
    text-align: left;
    cursor: pointer;
    transition: background 0.12s ease, color 0.12s ease;
    white-space: nowrap;
}
@media (max-width: 960px) {
    .cat-nav-item {
        background: var(--paper);
        border: 1px solid var(--ink12);
    }
}
.cat-nav-item:hover {
    background: var(--ink06);
}
.cat-nav-name {
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
}
.cat-nav-count {
    font-family: 'JetBrains Mono', ui-monospace, monospace;
    font-size: 11px;
    color: var(--ink40);
    flex-shrink: 0;
}

/* ── Category header (denser) ──────────────────────────────────────── */
.cat-header {
    display: flex;
    align-items: center;
    gap: 12px;
    flex-wrap: wrap;
}
.cat-toggle {
    width: 28px;
    height: 28px;
    border-radius: 8px;
    background: var(--ink06);
    color: var(--ink);
    border: none;
    cursor: pointer;
    display: grid;
    place-items: center;
    font-size: 12px;
    flex-shrink: 0;
    transition: background 0.12s ease;
}
.cat-toggle:hover {
    background: var(--ink12);
}
.cat-toggle-icon {
    line-height: 1;
}
.cat-meta {
    display: flex;
    flex-direction: column;
    gap: 2px;
}
.cat-meta-line {
    display: flex;
    align-items: center;
    gap: 8px;
    flex-wrap: wrap;
}
.cat-hidden-pill {
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
.cat-actions {
    display: flex;
    align-items: center;
    gap: 6px;
    margin-left: auto;
}
.btn-add-product {
    padding: 8px 14px;
    font-size: 13px;
    min-height: 34px;
}
.icon-btn {
    width: 32px;
    height: 32px;
    border-radius: 10px;
    border: 1px solid var(--ink12);
    background: transparent;
    color: var(--ink);
    cursor: pointer;
    display: grid;
    place-items: center;
    font-size: 14px;
    transition: background 0.12s ease, border-color 0.12s ease;
}
.icon-btn:hover {
    background: var(--ink06);
}
.icon-btn:disabled {
    opacity: 0.4;
    cursor: not-allowed;
}
.icon-btn-danger {
    color: var(--accent);
    border-color: var(--accentBorder);
}
.icon-btn-danger:hover:not(:disabled) {
    background: var(--accentSoft);
}

.prod-row-hidden {
    opacity: 0.55;
}

.line-clamp-2 {
    display: -webkit-box;
    -webkit-line-clamp: 2;
    -webkit-box-orient: vertical;
    overflow: hidden;
}

.scroll-mt-24 {
    scroll-margin-top: 96px;
}

/* ── Mobile: let product rows breathe ──────────────────────────────────
   The reorder column + thumb + name stay on the first line; the price and
   edit/hide actions wrap to a full-width second line instead of squeezing
   the name to a few characters. */
@media (max-width: 640px) {
    .prod-row { flex-wrap: wrap; }
    .prod-thumb { width: 52px; height: 52px; }
    .prod-actions {
        flex-direction: row !important;
        align-items: center !important;
        justify-content: space-between;
        width: 100%;
        margin-top: 2px;
    }
}

/* ── Touch: meet the 44px tap-target floor for the dense icon controls ── */
@media (hover: none) {
    .icon-btn { width: 44px; height: 44px; }
    .cat-toggle { width: 44px; height: 44px; font-size: 14px; }
    .btn-add-product { min-height: var(--tap-min); }
    .filter-chip { padding: 11px 16px; }
    .toolbar-search-clear { width: 32px; height: 32px; }
}
</style>
