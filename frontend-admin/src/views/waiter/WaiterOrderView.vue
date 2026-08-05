<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import type { MenuCategory } from '@shared/types/api-responses'
import type { Product } from '@shared/types/models'
import { WAITER_CAPS, waiterApi, type WaiterOrderItemInput } from '@/services/waiterApi'
import { useAuthStore } from '@/stores/useAuthStore'
import { useWaiterRealtimeStore } from '@/stores/useWaiterRealtimeStore'
import { useWaiterCartStore, type CartLimit } from '@/stores/useWaiterCartStore'
import { useToast } from '@/composables/useToast'
import { resolveMediaUrl } from '@/services/mediaUrl'
import { formatMoney } from '@/utils/money'

/**
 * Order taking: the menu on the left of the waiter's thumb, a per-table cart at
 * the bottom. The menu itself is the existing anonymous customer endpoint (no
 * waiter-specific menu exists by contract); live availability arrives through the
 * realtime store's `unavailableProductIds`, folded from MenuItemAvailabilityChanged.
 *
 * Sending is a three-call chain — create the draft, replace its lines, submit —
 * and the backend dispatches to the kitchen on submit. The cart is only cleared
 * once the submit returns, so any failure leaves the waiter's work untouched.
 */
const route = useRoute()
const router = useRouter()
const auth = useAuthStore()
const store = useWaiterRealtimeStore()
const cart = useWaiterCartStore()
const toast = useToast()

const tableId = computed(() => String(route.params.tableId))

const categories = ref<MenuCategory[]>([])
const isLoading = ref(false)
const error = ref<string | null>(null)
const search = ref('')
const selectedCategoryId = ref<string | null>(null)
const cartOpen = ref(false)
const isSending = ref(false)

const tableNumber = computed(() => store.tableById(tableId.value)?.tableNumber ?? null)

// ── Menu filtering (client-side; the menu is a single payload) ──────────────
const visibleCategories = computed<MenuCategory[]>(() => {
  const term = search.value.trim().toLocaleLowerCase('ro')
  return categories.value
    .filter((c) => !selectedCategoryId.value || c.id === selectedCategoryId.value)
    .map((c) => ({
      ...c,
      products: term
        ? c.products.filter((p) => p.name.toLocaleLowerCase('ro').includes(term))
        : c.products,
    }))
    .filter((c) => c.products.length > 0)
})

function isUnavailable(product: Product): boolean {
  return (
    product.isAvailable === false ||
    product.outOfStock === true ||
    store.unavailableProductIds.has(product.id)
  )
}

// ── Cart interactions ───────────────────────────────────────────────────────
function reportLimit(result: CartLimit): boolean {
  if (result === 'max_items') {
    toast.error(`O comandă poate avea maximum ${WAITER_CAPS.MaxOrderItems} produse diferite.`)
    return false
  }
  if (result === 'max_qty') {
    toast.error(`Maximum ${WAITER_CAPS.MaxLineQuantity} bucăți dintr-un produs.`)
    return false
  }
  return true
}

function addProduct(product: Product) {
  if (isUnavailable(product)) {
    toast.error(`„${product.name}” este indisponibil.`)
    return
  }
  reportLimit(cart.add({ id: product.id, name: product.name, price: product.price }))
}

function incrementProduct(productId: string) {
  reportLimit(cart.increment(productId))
}

// ── Send to the kitchen ─────────────────────────────────────────────────────
async function sendToKitchen() {
  if (isSending.value || cart.isEmpty) return
  const items: WaiterOrderItemInput[] = cart.lines.map((line) => ({
    productId: line.productId,
    quantity: line.quantity,
    notes: line.notes.trim() ? line.notes.trim() : null,
  }))
  if (items.length > WAITER_CAPS.MaxOrderItems) {
    toast.error(`O comandă poate avea maximum ${WAITER_CAPS.MaxOrderItems} produse diferite.`)
    return
  }

  isSending.value = true
  let draftId: string | null = null
  try {
    draftId = (await waiterApi.createOrder(tableId.value)).id
    await waiterApi.updateOrderItems(draftId, items)
  } catch (err) {
    // Nothing was submitted — bin the half-built draft so it does not linger on
    // the session (a draft is hard-deleted by the cancel endpoint).
    if (draftId) await waiterApi.cancelOrder(draftId).catch(() => undefined)
    toast.error(err instanceof Error ? err.message : 'Comanda nu a putut fi pregătită.')
    isSending.value = false
    return
  }

  try {
    const result = await waiterApi.submitOrder(draftId)
    cart.clear(tableId.value)
    cartOpen.value = false
    toast.success(
      result.orderNumber ? `Comanda #${result.orderNumber} trimisă.` : 'Comanda a fost trimisă.',
    )
    void store.refreshFloor()
    router.replace({ name: 'waiter-table', params: { tableId: tableId.value } })
  } catch (err) {
    // The draft may or may not have dispatched — leave it alone (it is detached
    // at close) and keep the cart so the waiter can retry without re-typing.
    toast.error(err instanceof Error ? err.message : 'Trimiterea comenzii a eșuat.')
  } finally {
    isSending.value = false
  }
}

// ── Loading ─────────────────────────────────────────────────────────────────
async function load() {
  const rid = auth.activeRestaurantId
  if (!rid) return
  cart.open(tableId.value)
  isLoading.value = true
  try {
    await store.start(rid)
    if (!store.floor) await store.refreshFloor()

    const table = store.tableById(tableId.value)
    if (!table?.session) {
      toast.info('Masa nu mai are o sesiune deschisă.')
      router.replace({ name: 'waiter-floor' })
      return
    }

    const menu = await waiterApi.getMenu()
    categories.value = (menu.categories ?? []).map((c) => ({ ...c, products: c.products ?? [] }))
    error.value = null
  } catch (err) {
    error.value = err instanceof Error ? err.message : 'Meniul nu a putut fi încărcat.'
  } finally {
    isLoading.value = false
  }
}
onMounted(load)
</script>

<template>
  <div class="order-view">
    <header class="order-head">
      <button
        type="button"
        class="icon-btn"
        title="Înapoi la masă"
        aria-label="Înapoi la masă"
        @click="router.push({ name: 'waiter-table', params: { tableId } })"
      >
        <span aria-hidden="true">←</span>
      </button>
      <div class="min-w-0">
        <div class="eyebrow">Comandă nouă</div>
        <h1 class="order-title">Masa {{ tableNumber ?? '—' }}</h1>
      </div>
    </header>

    <input
      v-model="search"
      class="field-input search-input"
      type="search"
      placeholder="Caută în meniu…"
      aria-label="Caută în meniu"
    />

    <div v-if="categories.length" class="cat-chips" role="tablist" aria-label="Filtrează după categorie">
      <button
        type="button"
        class="chip"
        :class="{ 'chip-active': selectedCategoryId === null }"
        role="tab"
        :aria-selected="selectedCategoryId === null"
        @click="selectedCategoryId = null"
      >
        Toate
      </button>
      <button
        v-for="category in categories"
        :key="category.id"
        type="button"
        class="chip"
        :class="{ 'chip-active': selectedCategoryId === category.id }"
        role="tab"
        :aria-selected="selectedCategoryId === category.id"
        @click="selectedCategoryId = category.id"
      >
        {{ category.name }}
      </button>
    </div>

    <div v-if="error" class="alert-error" role="alert">
      <span class="alert-icon" aria-hidden="true">⚠</span>
      <div class="alert-body">
        <div class="alert-title">Eroare</div>
        <div>{{ error }}</div>
      </div>
    </div>

    <p v-if="isLoading" class="state-note">Se încarcă meniul…</p>
    <p v-else-if="!visibleCategories.length" class="state-note">Niciun produs găsit.</p>

    <section v-for="category in visibleCategories" :key="category.id" class="menu-section">
      <h2 class="section-title">{{ category.name }}</h2>

      <div
        v-for="product in category.products"
        :key="product.id"
        class="card-white product-row"
        :class="{ 'product-row-off': isUnavailable(product) }"
      >
        <img
          v-if="product.imageUrl"
          class="product-img"
          :src="resolveMediaUrl(product.imageUrl)"
          alt=""
          loading="lazy"
        />
        <div v-else class="product-img product-img-empty" aria-hidden="true">☰</div>

        <div class="product-meta">
          <div class="product-name">{{ product.name }}</div>
          <div class="product-price">
            {{ formatMoney(product.price) }} RON
            <span v-if="isUnavailable(product)" class="product-off">· Indisponibil</span>
          </div>
        </div>

        <div v-if="cart.quantityOf(product.id)" class="stepper">
          <button
            type="button"
            class="step-btn"
            :aria-label="`Scade ${product.name}`"
            @click="cart.decrement(product.id)"
          >
            −
          </button>
          <span class="step-count">{{ cart.quantityOf(product.id) }}</span>
          <button
            type="button"
            class="step-btn"
            :disabled="isUnavailable(product)"
            :aria-label="`Adaugă ${product.name}`"
            @click="incrementProduct(product.id)"
          >
            +
          </button>
        </div>
        <button
          v-else
          type="button"
          class="add-btn"
          :disabled="isUnavailable(product)"
          :aria-label="`Adaugă ${product.name}`"
          @click="addProduct(product)"
        >
          +
        </button>
      </div>
    </section>

    <!-- Sticky cart bar — sits directly above the shell's tab bar. -->
    <button v-if="cart.count" type="button" class="cart-bar" @click="cartOpen = true">
      <span class="cart-count">{{ cart.count }}</span>
      <span class="cart-label">Vezi comanda</span>
      <span class="cart-total">{{ formatMoney(cart.total) }} RON</span>
    </button>

    <!-- Cart sheet -->
    <div v-if="cartOpen" class="sheet-backdrop" @click="cartOpen = false">
      <div class="sheet" role="dialog" aria-modal="true" aria-label="Comanda curentă" @click.stop>
        <div class="sheet-grip" aria-hidden="true"></div>
        <div class="eyebrow">Masa {{ tableNumber ?? '—' }}</div>
        <h2 class="sheet-title">Comanda curentă</h2>

        <p v-if="cart.isEmpty" class="state-note">Coșul este gol.</p>

        <ul v-else class="cart-lines">
          <li v-for="line in cart.lines" :key="line.productId" class="cart-line">
            <div class="cl-top">
              <span class="cl-name">{{ line.name }}</span>
              <span class="cl-price">{{ formatMoney(line.price * line.quantity) }} RON</span>
            </div>
            <div class="cl-controls">
              <div class="stepper">
                <button
                  type="button"
                  class="step-btn"
                  :aria-label="`Scade ${line.name}`"
                  @click="cart.decrement(line.productId)"
                >
                  −
                </button>
                <span class="step-count">{{ line.quantity }}</span>
                <button
                  type="button"
                  class="step-btn"
                  :aria-label="`Adaugă ${line.name}`"
                  @click="incrementProduct(line.productId)"
                >
                  +
                </button>
              </div>
              <button
                type="button"
                class="btn-ghost cl-remove"
                @click="cart.remove(line.productId)"
              >
                Șterge
              </button>
            </div>
            <input
              class="field-input cl-notes"
              type="text"
              placeholder="Observații bucătărie"
              aria-label="Observații bucătărie"
              :maxlength="WAITER_CAPS.NotesMaxLength"
              :value="line.notes"
              @input="cart.setNotes(line.productId, ($event.target as HTMLInputElement).value)"
            />
          </li>
        </ul>

        <div class="cart-sum">
          <span>Total</span>
          <strong>{{ formatMoney(cart.total) }} RON</strong>
        </div>

        <div class="sheet-actions">
          <button type="button" class="btn-ghost" :disabled="isSending" @click="cartOpen = false">
            Înapoi la meniu
          </button>
          <button
            type="button"
            class="pill-btn"
            :disabled="isSending || cart.isEmpty"
            @click="sendToKitchen"
          >
            {{ isSending ? 'Se trimite…' : 'Trimite la bucătărie' }}
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.order-view {
  /* Room for the sticky cart bar so the last product is never trapped under it. */
  padding-bottom: 62px;
}
.order-head {
  display: flex;
  align-items: flex-start;
  gap: 10px;
  margin-bottom: 12px;
}
.order-title {
  font-size: 21px;
  font-weight: 800;
  line-height: 1.15;
  color: var(--ink);
}
.search-input {
  width: 100%;
  min-height: var(--tap-min);
  margin-bottom: 12px;
}

.cat-chips {
  display: flex;
  gap: 8px;
  overflow-x: auto;
  padding-bottom: 4px;
  margin-bottom: 14px;
  scrollbar-width: none;
}
.cat-chips::-webkit-scrollbar { display: none; }
.chip {
  flex-shrink: 0;
  min-height: 38px;
  padding: 8px 16px;
  border-radius: 999px;
  border: 1px solid var(--ink12);
  background: var(--card);
  color: var(--ink70);
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 12px;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  cursor: pointer;
  transition: background 0.15s ease, color 0.15s ease, border-color 0.15s ease;
}
.chip-active {
  background: var(--ink);
  color: var(--lime);
  border-color: var(--ink);
}

.state-note {
  padding: 20px 12px;
  text-align: center;
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 12px;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  color: var(--ink40);
}

.menu-section { margin-bottom: 18px; }

/* ── Product rows ────────────────────────────────────────────────────────── */
.product-row {
  display: flex;
  align-items: center;
  gap: 11px;
  padding: 9px 11px;
  margin-bottom: 8px;
  min-height: calc(var(--tap-min) + 12px);
}
.product-row-off { opacity: 0.45; }
.product-img {
  width: 46px;
  height: 46px;
  border-radius: 13px;
  object-fit: cover;
  flex-shrink: 0;
  background: var(--ink06);
}
.product-img-empty {
  display: grid;
  place-items: center;
  color: var(--ink40);
  font-size: 17px;
}
.product-meta { flex: 1; min-width: 0; }
.product-name {
  font-size: 15px;
  font-weight: 600;
  color: var(--ink);
  line-height: 1.25;
}
.product-price {
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 11.5px;
  color: var(--ink40);
  margin-top: 2px;
}
.product-off {
  color: var(--accent);
  font-weight: 700;
}

.add-btn,
.step-btn {
  width: var(--tap-min);
  height: var(--tap-min);
  border-radius: 14px;
  display: grid;
  place-items: center;
  border: 1px solid var(--ink12);
  background: var(--card);
  color: var(--ink);
  font-size: 20px;
  font-weight: 700;
  line-height: 1;
  cursor: pointer;
  flex-shrink: 0;
  transition: background 0.15s ease, border-color 0.15s ease, transform 0.12s ease;
}
.add-btn { background: var(--ink); color: var(--lime); border-color: var(--ink); }
.add-btn:active,
.step-btn:active { transform: scale(0.94); }
.add-btn:disabled,
.step-btn:disabled { opacity: 0.4; cursor: not-allowed; }
.stepper {
  display: flex;
  align-items: center;
  gap: 4px;
  flex-shrink: 0;
}
.step-count {
  min-width: 26px;
  text-align: center;
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 15px;
  font-weight: 800;
  color: var(--ink);
}

/* ── Sticky cart bar ─────────────────────────────────────────────────────── */
.cart-bar {
  position: fixed;
  left: 50%;
  transform: translateX(-50%);
  /* --w-tabbar-h is set by WaiterShell and inherits down to this view. */
  bottom: calc(var(--w-tabbar-h, 68px) + env(safe-area-inset-bottom, 0px) + 8px);
  width: min(100% - 28px, 28rem - 28px);
  z-index: 45;
  min-height: var(--tap-min);
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 10px 16px;
  border: none;
  border-radius: 999px;
  background: var(--accent);
  color: var(--onAccent);
  cursor: pointer;
  box-shadow: var(--shadow-pop);
}
.cart-count {
  min-width: 26px;
  height: 26px;
  padding: 0 8px;
  border-radius: 999px;
  background: rgba(0, 0, 0, 0.2);
  display: inline-grid;
  place-items: center;
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 13px;
  font-weight: 800;
}
.cart-label {
  flex: 1;
  text-align: left;
  font-weight: 700;
  font-size: 15px;
}
.cart-total {
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 13px;
  font-weight: 800;
}

/* ── Cart sheet ──────────────────────────────────────────────────────────── */
.sheet-backdrop {
  position: fixed;
  inset: 0;
  z-index: 80;
  background: rgba(0, 0, 0, 0.45);
  backdrop-filter: blur(2px);
  -webkit-backdrop-filter: blur(2px);
  display: flex;
  align-items: flex-end;
  justify-content: center;
}
.sheet {
  width: min(100%, 28rem);
  max-height: 88vh;
  overflow-y: auto;
  background: var(--paperHi);
  border: 1px solid var(--ink12);
  border-bottom: none;
  border-radius: 24px 24px 0 0;
  padding: 10px 18px calc(22px + env(safe-area-inset-bottom, 0px));
  box-shadow: var(--shadow-pop);
}
.sheet-grip {
  width: 42px;
  height: 4px;
  border-radius: 999px;
  background: var(--ink12);
  margin: 0 auto 14px;
}
.sheet-title {
  font-size: 22px;
  font-weight: 800;
  line-height: 1.2;
  color: var(--ink);
  margin-bottom: 12px;
}
.sheet-actions {
  display: flex;
  gap: 10px;
  margin-top: 16px;
}
.sheet-actions > * { flex: 1; min-height: var(--tap-min); }

.cart-lines {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 12px;
}
.cart-line {
  border: 1px solid var(--ink12);
  border-radius: 16px;
  padding: 11px 12px;
  background: var(--card);
}
.cl-top {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: 10px;
}
.cl-name { font-weight: 600; color: var(--ink); }
.cl-price {
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 12.5px;
  color: var(--ink70);
  flex-shrink: 0;
}
.cl-controls {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  margin-top: 8px;
}
.cl-remove {
  min-height: 36px;
  padding: 6px 14px;
}
.cl-notes {
  width: 100%;
  margin-top: 8px;
}

.cart-sum {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  margin-top: 14px;
  padding-top: 11px;
  border-top: 1px solid var(--ink12);
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 13px;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  color: var(--ink70);
}
.cart-sum strong {
  font-size: 19px;
  color: var(--ink);
}
</style>
