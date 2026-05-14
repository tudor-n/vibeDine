<script setup lang="ts">
import { onMounted, ref, computed, reactive } from 'vue'
import { useMenuManagementStore } from '@/stores/useMenuManagementStore'
import { useToast } from '@/composables/useToast'
import type { AdminMenuCategoryNode, AdminProduct } from '@shared/types/models'

const store = useMenuManagementStore()
const toast = useToast()

onMounted(() => store.fetchMenu())

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
})

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
}

function openEditProduct(p: AdminProduct) {
    productForm.open = true
    productForm.mode = 'edit'
    productForm.id = p.id
    productForm.categoryId = p.categoryId
    productForm.name = p.name
    productForm.description = p.description ?? ''
    productForm.price = p.price
    productForm.imageUrl = p.imageUrl ?? ''
    productForm.isAvailable = p.isAvailable
}

function closeProductForm() {
    productForm.open = false
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
    productForm.submitting = true
    try {
        const description = productForm.description.trim()
        const imageUrl = productForm.imageUrl.trim()
        if (productForm.mode === 'create') {
            await store.createProduct({
                categoryId: productForm.categoryId,
                name,
                description: description || null,
                price: productForm.price,
                imageUrl: imageUrl || null,
                isAvailable: productForm.isAvailable,
            })
            toast.success('Produsul a fost adăugat.')
        } else {
            await store.updateProduct(productForm.id, {
                categoryId: productForm.categoryId,
                name,
                description,
                price: productForm.price,
                imageUrl,
                isAvailable: productForm.isAvailable,
            })
            toast.success('Produsul a fost actualizat.')
        }
        closeProductForm()
    } catch (e: any) {
        toast.error(e?.message ?? 'Eroare la salvarea produsului.')
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

const priceFormatter = computed(
    () => new Intl.NumberFormat('ro-RO', { style: 'currency', currency: 'RON' })
)
function formatPrice(n: number) {
    return priceFormatter.value.format(n)
}
</script>

<template>
    <div class="max-w-6xl mx-auto relative">
        <!-- Status row -->
        <div class="flex items-center justify-between mb-8">
            <div class="tag-pill">
                <span
                    class="live-dot"
                    style="background: var(--lime); box-shadow: 0 0 10px var(--lime);"
                ></span>
                <span>meniu · editor</span>
            </div>
            <div class="tag-pill">
                {{ store.totalCategories }} categorii ·
                {{ store.availableProducts }}/{{ store.totalProducts }} produse active
            </div>
        </div>

        <header
            class="mb-10 flex flex-col md:flex-row md:items-end md:justify-between gap-6"
        >
            <div class="max-w-2xl">
                <div class="eyebrow mb-4">Bucătărie · catalog</div>
                <h1 class="display-title text-[56px] md:text-[68px]">
                    Meniul<br />
                    <span class="text-accent">curții.</span>
                </h1>
                <p class="text-ink-70 text-lg mt-5 leading-relaxed">
                    Adaugă, editează sau ascunde categorii și produse. Descrierea
                    fiecărui fel include modul de preparare și gramajul vizibil în sală.
                </p>
            </div>

            <button
                class="pill-btn whitespace-nowrap"
                @click="openCreateCategory"
            >
                <span>+</span>
                <span>Adaugă categorie</span>
            </button>
        </header>

        <div
            v-if="store.error"
            class="card-paper p-5 mb-6"
            style="border-color: var(--accentBorder); background: var(--accentSoft);"
        >
            <span class="font-mono text-xs uppercase tracking-[0.18em] text-accent font-bold">
                ⚠ {{ store.error }}
            </span>
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

        <!-- Category list -->
        <div v-else class="space-y-6">
            <section
                v-for="cat in store.categories"
                :key="cat.id"
                class="card-paper p-6 relative"
            >
                <!-- Category header -->
                <div class="flex items-start justify-between gap-4 flex-wrap mb-4">
                    <div class="flex items-center gap-4 min-w-0 flex-1">
                        <div
                            class="cat-thumb"
                            :style="cat.imageUrl ? `background-image: url('${cat.imageUrl}');` : ''"
                            :aria-label="cat.imageUrl ? 'imagine categorie' : 'fără imagine'"
                        >
                            <span v-if="!cat.imageUrl" class="font-mono text-xs text-ink-40">·</span>
                        </div>
                        <div class="min-w-0">
                            <div class="eyebrow mb-1">
                                categorie · ordine
                                {{ cat.sortOrder ?? '—' }}
                            </div>
                            <h2 class="display-title text-2xl md:text-3xl truncate">
                                {{ cat.name }}
                            </h2>
                            <div class="text-ink-70 text-sm mt-1">
                                {{ cat.products.length }} produse
                            </div>
                        </div>
                    </div>

                    <div class="flex items-center gap-2 flex-wrap">
                        <button
                            class="pill-btn pill-btn-ghost"
                            @click="toggleCollapsed(cat.id)"
                        >
                            {{ collapsed[cat.id] ? 'Arată' : 'Restrânge' }}
                        </button>
                        <button
                            class="pill-btn pill-btn-ghost"
                            @click="openEditCategory(cat)"
                        >
                            Editează
                        </button>
                        <button
                            class="pill-btn pill-btn-danger"
                            :disabled="cat.products.length > 0"
                            :title="cat.products.length > 0 ? 'Categoria conține produse' : ''"
                            @click="deleteCategory(cat)"
                        >
                            Șterge
                        </button>
                        <button
                            class="pill-btn"
                            @click="openCreateProduct(cat.id)"
                        >
                            + Produs
                        </button>
                    </div>
                </div>

                <!-- Product table -->
                <div v-if="!collapsed[cat.id]">
                    <div
                        v-if="cat.products.length === 0"
                        class="card-white p-5 text-ink-70 italic text-sm"
                    >
                        Nicio variantă încă. Apasă „+ Produs" pentru a adăuga.
                    </div>

                    <div v-else class="grid gap-3">
                        <article
                            v-for="(p, pIdx) in cat.products"
                            :key="p.id"
                            class="card-white p-4 flex gap-3 items-start prod-row"
                            :class="{
                                'prod-row-dragging': dragState?.productId === p.id,
                                'prod-row-drop-target': dragOverProductId === p.id && dragState && dragState.productId !== p.id,
                            }"
                            :style="p.isAvailable ? '' : 'opacity: 0.55;'"
                            @dragover="onDragOver($event, cat.id, p.id)"
                            @dragleave="onDragLeave(p.id)"
                            @drop="onDrop($event, cat.id, p.id)"
                        >
                            <div
                                class="drag-handle shrink-0"
                                draggable="true"
                                title="Trage pentru a reordona"
                                @dragstart="onDragStart($event, cat.id, p.id)"
                                @dragend="onDragEnd"
                            >
                                <span aria-hidden="true">⋮⋮</span>
                            </div>

                            <div
                                class="prod-thumb"
                                :style="p.imageUrl ? `background-image: url('${p.imageUrl}');` : ''"
                            >
                                <span v-if="!p.imageUrl" class="font-mono text-[10px] text-ink-40">no img</span>
                            </div>

                            <div class="flex-1 min-w-0">
                                <div class="flex items-center gap-2 flex-wrap mb-1">
                                    <h3 class="font-semibold text-ink text-base truncate">
                                        {{ p.name }}
                                    </h3>
                                    <span
                                        class="tag-pill text-[10px]"
                                        :style="p.isAvailable ? 'background: var(--lime); color: var(--onLime); border-color: var(--ink12);' : ''"
                                    >
                                        {{ p.isAvailable ? 'activ' : 'ascuns' }}
                                    </span>
                                    <span class="font-mono text-[10px] text-ink-40">
                                        #{{ pIdx + 1 }}
                                    </span>
                                </div>
                                <p
                                    v-if="p.description"
                                    class="text-ink-70 text-sm leading-relaxed line-clamp-3"
                                >
                                    {{ p.description }}
                                </p>
                                <p v-else class="text-ink-40 text-sm italic">
                                    fără descriere — adaugă modul de preparare și gramajul
                                </p>
                            </div>

                            <div class="flex flex-col items-end gap-2 shrink-0">
                                <div class="font-mono text-base font-bold text-ink">
                                    {{ formatPrice(p.price) }}
                                </div>
                                <div class="flex items-center gap-2">
                                    <button
                                        class="pill-btn pill-btn-ghost text-sm"
                                        @click="openEditProduct(p)"
                                    >
                                        Editează
                                    </button>
                                    <button
                                        class="pill-btn pill-btn-ghost text-sm"
                                        @click="toggleAvailability(p)"
                                    >
                                        {{ p.isAvailable ? 'Ascunde' : 'Reactivează' }}
                                    </button>
                                </div>
                            </div>
                        </article>
                    </div>
                </div>
            </section>
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
                                type="url"
                                class="field-input"
                                placeholder="https://..."
                                maxlength="500"
                            />
                            <span class="field-hint">URL absolut (https://). Lasă gol pentru a elimina.</span>
                        </label>

                        <div v-if="categoryForm.imageUrl" class="preview-box">
                            <img :src="categoryForm.imageUrl" alt="previzualizare" />
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
                        <label class="field">
                            <span class="field-label">Categorie</span>
                            <select v-model="productForm.categoryId" class="field-input" required>
                                <option value="" disabled>— alege categoria —</option>
                                <option v-for="c in store.categories" :key="c.id" :value="c.id">
                                    {{ c.name }}
                                </option>
                            </select>
                        </label>

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

                        <div class="grid grid-cols-2 gap-3">
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
                            <label class="field">
                                <span class="field-label">Disponibil</span>
                                <select v-model="productForm.isAvailable" class="field-input">
                                    <option :value="true">Da · vizibil în meniu</option>
                                    <option :value="false">Nu · ascuns</option>
                                </select>
                            </label>
                        </div>

                        <label class="field">
                            <span class="field-label">URL imagine</span>
                            <input
                                v-model="productForm.imageUrl"
                                type="url"
                                class="field-input"
                                placeholder="https://..."
                                maxlength="500"
                            />
                            <span class="field-hint">URL absolut (https://). Lasă gol pentru a elimina.</span>
                        </label>

                        <div v-if="productForm.imageUrl" class="preview-box">
                            <img :src="productForm.imageUrl" alt="previzualizare" />
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
    border-radius: 24px;
    padding: 28px;
    width: 100%;
    max-width: 560px;
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

.drag-handle {
    width: 26px;
    align-self: stretch;
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
</style>
