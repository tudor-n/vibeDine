<script setup lang="ts">
import { ref, computed, onMounted, onBeforeUnmount } from 'vue'
import { useRouter } from 'vue-router'
import { apiClient } from '@/services/api-client'

interface ActiveOrder {
  id: string
  orderNumber: number | null
  status: 'submitted' | 'preparing' | 'ready'
  totalAmount: number
  createdAt: string
  readyAt: string | null
  tableId: string
  tableNumber: string | null
  items: Array<{
    id: string
    productName: string
    quantity: number
    unitPrice: number
    subtotal: number
  }>
}

const router = useRouter()
const orders = ref<ActiveOrder[]>([])
const isLoading = ref(false)
const error = ref<string | null>(null)
let pollTimer: number | null = null

const STATUS_BADGES: Record<string, { label: string; bg: string; color: string; border: string; icon: string }> = {
  submitted: { label: 'Trimisă', icon: '↗', bg: 'var(--lime)', color: 'var(--onLime)', border: 'var(--ink12)' },
  preparing: { label: 'În pregătire', icon: '◔', bg: 'var(--accentSoft)', color: 'var(--accentDk)', border: 'var(--accentBorder)' },
  ready: { label: 'Finalizată', icon: '✓', bg: 'var(--ink)', color: 'var(--lime)', border: 'var(--ink)' },
}

async function fetchActive() {
  try {
    isLoading.value = true
    error.value = null
    orders.value = await apiClient.getActiveOrders()
  } catch (err) {
    error.value = err instanceof Error ? err.message : 'Eroare la încărcarea comenzilor.'
  } finally {
    isLoading.value = false
  }
}

function openBoard(orderId: string) {
  router.push({ name: 'orders-active-board', query: { focus: orderId } })
}

onMounted(() => {
  fetchActive()
  pollTimer = window.setInterval(fetchActive, 5000)
})
onBeforeUnmount(() => {
  if (pollTimer !== null) window.clearInterval(pollTimer)
})

const sortedOrders = computed(() =>
  [...orders.value].sort((a, b) => new Date(a.createdAt).getTime() - new Date(b.createdAt).getTime()),
)
</script>

<template>
  <div class="max-w-6xl mx-auto relative">
    <div class="flex items-center justify-between mb-8">
      <div class="tag-pill" role="status">
        <span class="live-dot" aria-hidden="true"></span>
        <span>Bucătărie · live</span>
      </div>
      <div class="tag-pill">{{ sortedOrders.length }} active</div>
    </div>

    <header class="mb-8">
      <div class="eyebrow mb-4">Comenzile zilei</div>
      <h1 class="display-title text-[44px] md:text-[56px]">
        Comenzi<br />
        <span class="text-accent">active.</span>
      </h1>
      <p class="text-ink text-lg mt-5 leading-relaxed max-w-2xl">
        Fiecare comandă trimisă de la masă apare aici. Apasă pe ea pentru a deschide tabla
        kanban — drag &amp; drop între Comandă · În pregătire · Finalizată.
      </p>
    </header>

    <div class="flex items-center justify-between gap-3 mb-6 flex-wrap">
      <button
        type="button"
        class="pill-btn-ink"
        @click="router.push({ name: 'orders-active-board' })"
      >
        <span aria-hidden="true">▦</span>
        Deschide tabla
      </button>
      <button
        @click="fetchActive"
        :disabled="isLoading"
        class="pill-btn-ink whitespace-nowrap"
      >
        <span :class="{ 'animate-spin': isLoading }" aria-hidden="true">↻</span>
        Reîncarcă
      </button>
    </div>

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

    <div v-if="isLoading && orders.length === 0" class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
      <div v-for="i in 6" :key="i" class="card-paper h-40 animate-pulse" />
    </div>

    <div v-else-if="!isLoading && sortedOrders.length === 0" class="card-paper p-16 text-center">
      <div class="eyebrow mb-3">Nimic în curs</div>
      <h3 class="display-title text-3xl mb-2">Nicio comandă activă</h3>
      <p class="text-ink-70">Comenzile trimise de la mese apar automat aici.</p>
    </div>

    <div v-else class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
      <button
        v-for="order in sortedOrders"
        :key="order.id"
        type="button"
        class="active-order-card"
        @click="openBoard(order.id)"
      >
        <div class="card-top">
          <div class="order-number-tag">
            <span class="font-mono text-[11px] uppercase tracking-[0.06em]" style="color: rgba(245,240,232,0.7);">Comanda</span>
            <span class="font-display text-2xl font-bold leading-none mt-0.5">
              #{{ order.orderNumber ?? '—' }}
            </span>
          </div>
          <div class="table-tag" aria-hidden="true">
            <span class="font-mono text-[11px] uppercase tracking-[0.06em]" style="color: var(--ink70);">Masa</span>
            <span class="font-display text-xl font-bold leading-none mt-0.5">{{ order.tableNumber ?? '?' }}</span>
          </div>
        </div>

        <div class="card-mid">
          <span
            class="status-chip"
            :style="{
              background: STATUS_BADGES[order.status]?.bg,
              color: STATUS_BADGES[order.status]?.color,
              borderColor: STATUS_BADGES[order.status]?.border,
            }"
          >
            <span aria-hidden="true">{{ STATUS_BADGES[order.status]?.icon }}</span>
            {{ STATUS_BADGES[order.status]?.label }}
          </span>
          <span class="text-ink-70 font-mono text-[13px] uppercase tracking-[0.04em]">
            {{ order.items.length }} {{ order.items.length === 1 ? 'preparat' : 'preparate' }}
          </span>
        </div>

        <div class="card-bot">
          <span class="font-mono text-[12px] uppercase tracking-[0.06em] text-ink-70">Total</span>
          <span class="font-display text-xl font-bold text-ink">
            {{ order.totalAmount.toFixed(2) }}
            <span class="text-base font-semibold text-ink-70">RON</span>
          </span>
        </div>
      </button>
    </div>
  </div>
</template>

<style scoped>
.active-order-card {
  background: var(--paperHi);
  border: 1px solid var(--ink12);
  border-radius: 22px;
  padding: 18px;
  display: flex;
  flex-direction: column;
  gap: 14px;
  cursor: pointer;
  text-align: left;
  font-family: inherit;
  transition: transform 0.15s ease, box-shadow 0.18s ease, border-color 0.18s ease;
}
.active-order-card:hover {
  transform: translateY(-2px);
  box-shadow: var(--shadow-soft);
  border-color: var(--ink);
}
.card-top { display: flex; align-items: center; justify-content: space-between; gap: 10px; }
.order-number-tag {
  background: var(--ink);
  color: var(--paperHi);
  padding: 8px 14px;
  border-radius: 14px;
  display: inline-flex;
  flex-direction: column;
  align-items: flex-start;
}
.order-number-tag span:last-child { color: var(--lime); }
.table-tag {
  background: var(--ink06);
  color: var(--ink);
  padding: 8px 14px;
  border-radius: 14px;
  display: inline-flex;
  flex-direction: column;
  align-items: flex-end;
}
.status-chip {
  display: inline-flex;
  align-items: center;
  gap: 5px;
  padding: 5px 12px;
  border-radius: 999px;
  font-size: 12.5px;
  font-weight: 700;
  border: 1px solid;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  font-family: 'JetBrains Mono', monospace;
}
.card-mid { display: flex; align-items: center; justify-content: space-between; gap: 10px; }
.card-bot {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  padding-top: 10px;
  border-top: 1px solid var(--ink06);
}
</style>
