<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import { apiClient } from '@/services/api-client'
import { formatMoney } from '@/utils/money'

interface OrderItem {
  id: string
  productName: string
  quantity: number
  unitPrice: number
  subtotal: number
  wasAiRecommended: boolean
  isFlashDealItem: boolean
}

interface Order {
  id: string
  orderNumber: number | null
  status: string
  paymentStatus: string
  totalAmount: number
  createdAt: string
  deletedAt: string | null
  tableNumber: string | null
  items: OrderItem[]
}

const orders = ref<Order[]>([])
const isLoading = ref(false)
const error = ref<string | null>(null)
const expandedOrderId = ref<string | null>(null)
const filterStatus = ref<string>('all')

const STATUS_LABELS: Record<string, { label: string; icon: string; bg: string; color: string; border: string }> = {
  draft:      { label: 'Ciornă',      icon: '✎', bg: 'var(--ink06)',     color: 'var(--ink70)', border: 'var(--ink12)' },
  submitted:  { label: 'Trimisă',     icon: '↗', bg: 'var(--lime)',      color: 'var(--onLime)', border: 'var(--ink12)' },
  preparing:  { label: 'Pregătire',   icon: '◔', bg: 'var(--accentSoft)', color: 'var(--accentDk)', border: 'var(--accentBorder)' },
  ready:      { label: 'Gată',        icon: '✓', bg: 'var(--ink)',       color: 'var(--lime)',  border: 'var(--ink)' },
  completed:  { label: 'Finalizată',  icon: '●', bg: 'var(--ink)',       color: 'var(--paperHi)', border: 'var(--ink)' },
  cancelled:  { label: 'Anulată',     icon: '✕', bg: 'var(--accentSoft)', color: 'var(--accentDk)', border: 'var(--accentBorder)' },
}

const FILTERS: Array<{ key: string; label: string }> = [
  { key: 'all', label: 'Toate' },
  { key: 'completed', label: 'Finalizate' },
  { key: 'cancelled', label: 'Anulate' },
]

const filteredOrders = computed(() => {
  if (filterStatus.value === 'all') return orders.value
  return orders.value.filter(o => o.status === filterStatus.value)
})

function formatTime(iso: string) {
  const d = new Date(iso)
  return d.toLocaleDateString('ro-RO', { day: '2-digit', month: '2-digit', year: 'numeric' })
    + ' · '
    + d.toLocaleTimeString('ro-RO', { hour: '2-digit', minute: '2-digit' })
}

function itemLabel(item: OrderItem): { icon: string; text: string; bg: string; color: string; border: string } | null {
  if (item.isFlashDealItem) {
    return { icon: '⚡', text: 'Flash deal', bg: 'var(--lime)', color: 'var(--onLime)', border: 'var(--ink12)' }
  }
  if (item.wasAiRecommended) {
    return { icon: '✦', text: 'AI · upsell', bg: 'var(--ink)', color: 'var(--lime)', border: 'var(--ink)' }
  }
  return null
}

function orderHasAiItems(order: Order) {
  return order.items.some(i => i.wasAiRecommended || i.isFlashDealItem)
}

function toggleExpand(id: string) {
  expandedOrderId.value = expandedOrderId.value === id ? null : id
}

async function fetchOrders() {
  try {
    isLoading.value = true
    error.value = null
    orders.value = await apiClient.getOrders(100)
  } catch (err) {
    error.value = err instanceof Error ? err.message : 'Eroare la încărcarea comenzilor.'
  } finally {
    isLoading.value = false
  }
}

onMounted(fetchOrders)
</script>

<template>
  <div class="max-w-5xl mx-auto relative">

    <!-- Status row -->
    <div class="flex items-center justify-between mb-8">
      <div class="tag-pill" role="status" aria-label="Jurnalul comenzilor este activ">
        <span class="live-dot" aria-hidden="true"></span>
        <span>Jurnal activ</span>
      </div>
      <div class="tag-pill" aria-label="Total înregistrări">{{ orders.length }} comenzi</div>
    </div>

    <header class="mb-10">
      <div class="eyebrow mb-4">Comenzile zilei</div>
      <h1 class="display-title text-[44px] md:text-[56px]">
        Jurnal<br />
        <span class="text-accent">comenzi finalizate.</span>
      </h1>
      <p class="text-ink text-lg mt-5 leading-relaxed max-w-2xl">
        Istoricul comenzilor încheiate. Comenzile încă în lucru apar în
        <span class="font-semibold">Comenzi active</span>; după ce sunt mutate în
        „Finalizată” și expiră perioada de grație de 45s, ajung aici automat.
      </p>
    </header>

    <!-- Filter chips + reload -->
    <div class="flex items-center justify-between gap-3 mb-6 flex-wrap">
      <div
        class="flex gap-2 overflow-x-auto hide-scrollbar -mx-1 px-1 py-1 flex-1"
        role="group"
        aria-label="Filtrează comenzile după stare"
      >
        <button
          v-for="f in FILTERS"
          :key="f.key"
          type="button"
          class="filter-chip"
          :class="{ 'filter-chip-active': filterStatus === f.key }"
          :aria-pressed="filterStatus === f.key"
          @click="filterStatus = f.key"
        >{{ f.label }}</button>
      </div>

      <button
        @click="fetchOrders"
        :disabled="isLoading"
        class="pill-btn-ink whitespace-nowrap"
        :aria-label="isLoading ? 'Se reîncarcă comenzile' : 'Reîncarcă comenzile'"
      >
        <span :class="{ 'animate-spin': isLoading }" aria-hidden="true">↻</span>
        Reîncarcă
      </button>
    </div>

    <!-- Legend -->
    <div class="flex items-center gap-3 mb-7 flex-wrap" role="group" aria-label="Legendă etichete">
      <span class="eyebrow" aria-hidden="true">Legendă</span>
      <span class="legend-pill" style="background: var(--ink); color: var(--lime); border-color: var(--ink);">
        <span aria-hidden="true">✦</span> AI · upsell
      </span>
      <span class="legend-pill" style="background: var(--lime); color: var(--onLime); border-color: var(--ink12);">
        <span aria-hidden="true">⚡</span> Flash deal
      </span>
    </div>

    <!-- Error -->
    <div
      v-if="error"
      class="card-paper p-5 mb-6"
      style="border-color: var(--accentBorder); background: var(--accentSoft);"
      role="alert"
    >
      <span class="font-mono text-sm uppercase tracking-[0.06em] font-bold" style="color: var(--accentDk);">
        <span aria-hidden="true">⚠</span> {{ error }}
      </span>
    </div>

    <!-- Loading skeleton -->
    <div v-if="isLoading && orders.length === 0" class="space-y-3">
      <div v-for="i in 5" :key="i" class="card-paper h-24 animate-pulse" />
    </div>

    <!-- Empty -->
    <div v-else-if="!isLoading && filteredOrders.length === 0" class="card-paper p-16 text-center relative overflow-hidden">
      <div class="bloom bloom-lime" style="top: -40px; right: -40px; width: 220px; height: 220px; opacity: 0.25;" />
      <div class="relative z-10">
        <div class="eyebrow mb-3">Nimic aici</div>
        <h3 class="display-title text-3xl mb-2">Nicio comandă</h3>
        <p class="text-ink-70">Nu există comenzi pentru filtrul selectat.</p>
      </div>
    </div>

    <!-- Orders list -->
    <div v-else class="space-y-3">
      <article
        v-for="order in filteredOrders"
        :key="order.id"
        class="order-card"
      >
        <button
          class="order-row"
          type="button"
          :aria-expanded="expandedOrderId === order.id"
          :aria-controls="`order-detail-${order.id}`"
          :aria-label="`Comandă masa ${order.tableNumber ?? 'necunoscută'}, ${STATUS_LABELS[order.status]?.label ?? order.status}, ${order.items.length} preparate, total ${formatMoney(order.totalAmount)} RON`"
          @click="toggleExpand(order.id)"
        >
          <span class="order-arrow" :class="{ 'rotate-90': expandedOrderId === order.id }" aria-hidden="true">›</span>

          <div class="order-table" aria-hidden="true">
            <span class="font-mono text-[10px] uppercase tracking-[0.06em] leading-none" style="color: rgba(245,240,232,0.75);">Masa</span>
            <span class="font-display text-lg font-bold leading-tight mt-0.5">{{ order.tableNumber ?? '?' }}</span>
          </div>

          <div class="flex-1 min-w-0">
            <div class="flex items-center gap-2 flex-wrap mb-1">
              <span v-if="order.orderNumber != null" class="font-mono text-[11px] uppercase tracking-[0.06em] text-ink-70 font-bold">
                #{{ order.orderNumber }}
              </span>
              <span class="font-bold text-ink text-base">
                {{ order.items.length }} {{ order.items.length === 1 ? 'preparat' : 'preparate' }}
              </span>
              <span
                class="status-chip"
                :style="{
                  background: STATUS_LABELS[order.status]?.bg,
                  color: STATUS_LABELS[order.status]?.color,
                  borderColor: STATUS_LABELS[order.status]?.border,
                }"
              >
                <span aria-hidden="true">{{ STATUS_LABELS[order.status]?.icon }}</span>
                {{ STATUS_LABELS[order.status]?.label ?? order.status }}
              </span>
              <span v-if="orderHasAiItems(order)" class="text-base" aria-hidden="true">
                <span v-if="order.items.some(i => i.isFlashDealItem)" class="ml-1">⚡</span>
                <span v-if="order.items.some(i => i.wasAiRecommended)" class="ml-0.5">✦</span>
              </span>
            </div>
            <div class="font-mono text-[13px] uppercase tracking-[0.04em] text-ink font-medium">
              {{ formatTime(order.createdAt) }}
            </div>
          </div>

          <div class="text-right flex-shrink-0" aria-hidden="true">
            <div class="font-mono text-[11px] uppercase tracking-[0.06em] text-ink font-semibold">total</div>
            <div class="font-display text-xl font-bold text-ink leading-tight">
              {{ formatMoney(order.totalAmount) }}
              <span class="text-sm font-semibold text-ink">RON</span>
            </div>
          </div>
        </button>

        <Transition name="expand">
          <div v-if="expandedOrderId === order.id" class="order-expand" :id="`order-detail-${order.id}`">
            <div class="expand-head px-5 py-3 text-[12.5px] uppercase tracking-[0.06em] text-ink font-mono font-semibold">
              <span>Produs</span>
              <span class="text-center">Cant.</span>
              <span class="text-right">Preț/buc</span>
              <span class="text-right">Subtotal</span>
            </div>
            <div
              v-for="item in order.items"
              :key="item.id"
              class="expand-item px-5 py-3 text-[15px]"
            >
              <div class="oi-name flex items-center gap-2 flex-wrap">
                <span class="font-medium text-ink">{{ item.productName }}</span>
                <span
                  v-if="itemLabel(item)"
                  class="legend-pill"
                  :style="{
                    background: itemLabel(item)!.bg,
                    color: itemLabel(item)!.color,
                    borderColor: itemLabel(item)!.border,
                  }"
                >{{ itemLabel(item)!.icon }} {{ itemLabel(item)!.text }}</span>
              </div>
              <div class="oi-qty text-center text-ink font-mono text-[15px]">× {{ item.quantity }}</div>
              <div class="oi-price text-right text-ink">{{ formatMoney(item.unitPrice) }} RON</div>
              <div class="oi-sub text-right font-semibold text-ink">{{ formatMoney(item.subtotal) }} RON</div>
            </div>
            <div class="order-total-row">
              <span class="eyebrow">Total comandă</span>
              <span class="font-display text-2xl font-bold text-accent">
                {{ formatMoney(order.totalAmount) }}
                <span class="text-base text-ink font-semibold">RON</span>
              </span>
            </div>
          </div>
        </Transition>
      </article>
    </div>
  </div>
</template>

<style scoped>
.filter-chip {
  font-size: 15.5px;
  font-weight: 600;
  padding: 10px 18px;
  min-height: var(--tap-min);
  border-radius: 999px;
  background: var(--paperHi);
  color: var(--ink);
  border: 1px solid var(--ink12);
  flex-shrink: 0;
  cursor: pointer;
  font-family: inherit;
  transition: background 0.18s ease, color 0.18s ease, border-color 0.18s ease;
}
.filter-chip:hover {
  background: var(--card);
}
.filter-chip-active {
  background: var(--ink);
  color: var(--paperHi);
  border-color: var(--ink);
}
.legend-pill {
  display: inline-flex;
  align-items: center;
  gap: 5px;
  padding: 5px 12px;
  border-radius: 999px;
  font-size: 12.5px;
  font-weight: 700;
  border: 1px solid;
  font-family: 'JetBrains Mono', ui-monospace, Menlo, monospace;
  letter-spacing: 0.04em;
  text-transform: uppercase;
  white-space: nowrap;
}
.order-card {
  background: var(--paperHi);
  border: 1px solid var(--ink12);
  border-radius: 22px;
  overflow: hidden;
  transition: box-shadow 0.18s ease;
}
.order-card:hover {
  box-shadow: var(--shadow-soft);
}
.order-row {
  width: 100%;
  text-align: left;
  padding: 12px 14px;
  display: flex;
  align-items: center;
  gap: 12px;
  background: transparent;
  border: none;
  cursor: pointer;
  font-family: inherit;
  transition: background 0.18s ease;
}
.order-row:hover {
  background: var(--ink06);
}
.order-arrow {
  color: var(--ink40);
  font-size: 18px;
  font-family: 'JetBrains Mono', monospace;
  width: 14px;
  display: inline-block;
  transition: transform 0.18s ease;
  line-height: 1;
}
.rotate-90 {
  transform: rotate(90deg);
}
.order-table {
  width: 46px;
  height: 46px;
  border-radius: 14px;
  background: var(--ink);
  color: var(--paperHi);
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
  padding-top: 3px;
}
.order-table span:first-child {
  color: var(--paperHi);
  opacity: 0.55;
}
.order-table span:last-child { color: var(--lime); }
.status-chip {
  display: inline-flex;
  align-items: center;
  gap: 5px;
  padding: 4px 12px;
  border-radius: 999px;
  font-size: 12.5px;
  font-weight: 700;
  border: 1px solid;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  font-family: 'JetBrains Mono', monospace;
}
.order-expand {
  border-top: 1px solid var(--ink12);
  background: var(--card);
}
/* Line-item table: fixed numeric columns on desktop. */
.expand-head,
.expand-item {
  display: grid;
  grid-template-columns: 1fr 60px 100px 110px;
  align-items: center;
}
.expand-item { border-top: 1px solid var(--ink06); }

/* Below 640px the four fixed columns overflow the card, so each line item
   restacks: product name full-width, then quantity, then a price/subtotal row. */
@media (max-width: 640px) {
  .expand-head { display: none; }
  .expand-item {
    grid-template-columns: 1fr auto;
    grid-template-areas:
      "name name"
      "qty  qty"
      "price sub";
    gap: 2px 12px;
  }
  .oi-name  { grid-area: name; margin-bottom: 4px; }
  .oi-qty   { grid-area: qty;   text-align: left; }
  .oi-price { grid-area: price; text-align: left; }
  .oi-sub   { grid-area: sub; }
}
.order-total-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 14px 20px 18px;
  border-top: 1px solid var(--ink12);
  background: color-mix(in srgb, var(--lime) 30%, transparent);
}

.expand-enter-active,
.expand-leave-active {
  transition: opacity 0.22s ease, max-height 0.28s ease;
  max-height: 800px;
  overflow: hidden;
}
.expand-enter-from,
.expand-leave-to {
  opacity: 0;
  max-height: 0;
}
</style>
