<script setup lang="ts">
import { ref, computed, onMounted, onBeforeUnmount } from 'vue'
import { useRoute } from 'vue-router'
import { apiClient } from '@/services/api-client'

type ActiveStatus = 'submitted' | 'preparing' | 'ready'

interface ActiveOrder {
  id: string
  orderNumber: number | null
  status: ActiveStatus
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

const route = useRoute()
const orders = ref<ActiveOrder[]>([])
const isLoading = ref(false)
const error = ref<string | null>(null)
const now = ref(Date.now())
const dragOrderId = ref<string | null>(null)
const dragOverColumn = ref<ActiveStatus | null>(null)
const focusOrderId = computed(() => (route.query.focus as string | undefined) ?? null)

let pollTimer: number | null = null
let tickTimer: number | null = null

// Mirrors backend OrderFinalizationService.GracePeriod.
const GRACE_SECONDS = 45

const COLUMNS: Array<{ key: ActiveStatus; label: string; hint: string }> = [
  { key: 'submitted', label: 'Comandă', hint: 'Tocmai sosite de la masă' },
  { key: 'preparing', label: 'În pregătire', hint: 'Sunt în lucru acum' },
  { key: 'ready', label: 'Finalizată', hint: `Se arhivează automat în ${GRACE_SECONDS}s` },
]

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

const byColumn = computed<Record<ActiveStatus, ActiveOrder[]>>(() => {
  const buckets: Record<ActiveStatus, ActiveOrder[]> = { submitted: [], preparing: [], ready: [] }
  for (const o of orders.value) buckets[o.status]?.push(o)
  // Stable ordering per column: oldest first so the kitchen works FIFO.
  for (const k of Object.keys(buckets) as ActiveStatus[]) {
    buckets[k].sort((a, b) => new Date(a.createdAt).getTime() - new Date(b.createdAt).getTime())
  }
  return buckets
})

function secondsLeft(order: ActiveOrder): number | null {
  if (order.status !== 'ready' || !order.readyAt) return null
  const elapsed = (now.value - new Date(order.readyAt).getTime()) / 1000
  return Math.max(0, Math.ceil(GRACE_SECONDS - elapsed))
}

function onDragStart(e: DragEvent, orderId: string) {
  dragOrderId.value = orderId
  if (e.dataTransfer) {
    e.dataTransfer.setData('text/plain', orderId)
    e.dataTransfer.effectAllowed = 'move'
  }
}

function onDragEnd() {
  dragOrderId.value = null
  dragOverColumn.value = null
}

function onDragOver(e: DragEvent, col: ActiveStatus) {
  e.preventDefault()
  if (e.dataTransfer) e.dataTransfer.dropEffect = 'move'
  dragOverColumn.value = col
}

function onDragLeave(col: ActiveStatus) {
  if (dragOverColumn.value === col) dragOverColumn.value = null
}

async function onDrop(e: DragEvent, target: ActiveStatus) {
  e.preventDefault()
  const orderId = e.dataTransfer?.getData('text/plain') || dragOrderId.value
  dragOrderId.value = null
  dragOverColumn.value = null
  if (!orderId) return

  const order = orders.value.find(o => o.id === orderId)
  if (!order || order.status === target) return

  // Optimistic update so the card snaps to the new column instantly.
  const prevStatus = order.status
  const prevReadyAt = order.readyAt
  order.status = target
  order.readyAt = target === 'ready' ? new Date().toISOString() : null

  try {
    const updated = await apiClient.updateOrderStatus(orderId, target)
    // Trust the server timestamp (avoids drift with the grace-period countdown).
    order.readyAt = updated.readyAt ?? order.readyAt
  } catch (err) {
    order.status = prevStatus
    order.readyAt = prevReadyAt
    error.value = err instanceof Error ? err.message : 'Mutarea comenzii a eșuat.'
  }
}

onMounted(() => {
  fetchActive()
  pollTimer = window.setInterval(fetchActive, 5000)
  tickTimer = window.setInterval(() => (now.value = Date.now()), 1000)
})
onBeforeUnmount(() => {
  if (pollTimer !== null) window.clearInterval(pollTimer)
  if (tickTimer !== null) window.clearInterval(tickTimer)
})
</script>

<template>
  <div class="max-w-7xl mx-auto relative">
    <div class="flex items-center justify-between mb-6 flex-wrap gap-3">
      <div class="tag-pill" role="status">
        <span class="live-dot" aria-hidden="true"></span>
        <span>Live · auto-arhivare {{ GRACE_SECONDS }}s</span>
      </div>
      <button class="pill-btn-ink" :disabled="isLoading" @click="fetchActive">
        <span :class="{ 'animate-spin': isLoading }" aria-hidden="true">↻</span>
        Reîncarcă
      </button>
    </div>

    <div
      v-if="error"
      class="card-paper p-4 mb-5"
      style="border-color: var(--accentBorder); background: var(--accentSoft);"
      role="alert"
    >
      <span class="font-mono text-sm uppercase tracking-[0.06em] font-bold" style="color: var(--accentDk);">
        <span aria-hidden="true">⚠</span> {{ error }}
      </span>
    </div>

    <div class="kanban">
      <section
        v-for="col in COLUMNS"
        :key="col.key"
        class="column"
        :class="[
          `column--${col.key}`,
          { 'column--drop': dragOverColumn === col.key },
        ]"
        @dragover="onDragOver($event, col.key)"
        @dragleave="onDragLeave(col.key)"
        @drop="onDrop($event, col.key)"
      >
        <header class="column-head">
          <div>
            <div class="eyebrow">{{ col.label }}</div>
            <div class="text-xs font-mono text-ink-70 uppercase tracking-[0.05em] mt-0.5">{{ col.hint }}</div>
          </div>
          <span class="count-pill">{{ byColumn[col.key].length }}</span>
        </header>

        <div class="column-body">
          <article
            v-for="order in byColumn[col.key]"
            :key="order.id"
            class="kanban-card"
            :class="{
              'kanban-card--focus': order.id === focusOrderId,
              'kanban-card--dragging': dragOrderId === order.id,
            }"
            draggable="true"
            @dragstart="onDragStart($event, order.id)"
            @dragend="onDragEnd"
          >
            <div class="kc-top">
              <div class="kc-num">
                <span class="font-mono text-[10px] uppercase tracking-[0.06em]" style="color: rgba(245,240,232,0.7);">Comanda</span>
                <span class="font-display text-lg font-bold leading-none mt-0.5">#{{ order.orderNumber ?? '—' }}</span>
              </div>
              <div class="kc-table">
                <span class="font-mono text-[10px] uppercase tracking-[0.06em] text-ink-70">Masa</span>
                <span class="font-display text-base font-bold leading-none">{{ order.tableNumber ?? '?' }}</span>
              </div>
            </div>

            <ul class="kc-items">
              <li v-for="item in order.items" :key="item.id" class="kc-item">
                <span class="kc-qty">×{{ item.quantity }}</span>
                <span class="kc-name">{{ item.productName }}</span>
              </li>
            </ul>

            <footer class="kc-bot">
              <span class="text-[12px] font-mono uppercase tracking-[0.05em] text-ink-70">
                {{ order.totalAmount.toFixed(2) }} RON
              </span>
              <span
                v-if="secondsLeft(order) !== null"
                class="countdown"
                :class="{ 'countdown--urgent': (secondsLeft(order) ?? 99) <= 10 }"
              >
                arhivare {{ secondsLeft(order) }}s
              </span>
            </footer>
          </article>

          <div v-if="byColumn[col.key].length === 0" class="empty-slot">
            Trage o comandă aici
          </div>
        </div>
      </section>
    </div>
  </div>
</template>

<style scoped>
.kanban {
  display: grid;
  grid-template-columns: 1fr;
  gap: 16px;
}
@media (min-width: 900px) {
  .kanban { grid-template-columns: repeat(3, 1fr); }
}
.column {
  background: var(--paperHi);
  border: 1px solid var(--ink12);
  border-radius: 22px;
  padding: 0 14px 14px;
  display: flex;
  flex-direction: column;
  /* Comfortably holds ~10 standard cards before the column has to grow.
     min-height is a floor, not a ceiling — extra cards just push the
     column taller, no inner scroll, no clipping. */
  min-height: 1500px;
  transition: background 0.18s ease, border-color 0.18s ease, box-shadow 0.18s ease;
  overflow: hidden;
}
.column--drop {
  background: color-mix(in srgb, var(--lime) 18%, var(--paperHi));
  border-color: var(--ink);
  box-shadow: var(--shadow-soft);
}
.column-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  padding: 14px 16px;
  margin: 0 -14px 12px;
  border-bottom: 1px solid var(--ink12);
  transition: background 0.18s ease, color 0.18s ease;
}
.column-head .eyebrow {
  font-size: 14px;
  letter-spacing: 0.04em;
  color: inherit;
  opacity: 0.95;
}
.column-head .text-ink-70 { color: currentColor !important; opacity: 0.75; }
.count-pill {
  background: rgba(0, 0, 0, 0.18);
  color: inherit;
  font-family: 'JetBrains Mono', monospace;
  font-weight: 700;
  font-size: 12px;
  padding: 5px 11px;
  border-radius: 999px;
  border: 1px solid rgba(0, 0, 0, 0.12);
}

/* ── Per-column color treatments — they ride on the existing theme tokens
   so light/dark switching keeps working. ─────────────────────────────── */
.column--submitted .column-head {
  background: var(--lime);
  color: var(--onLime);
  border-bottom-color: color-mix(in srgb, var(--onLime) 15%, transparent);
}
.column--preparing .column-head {
  background: var(--accentSoft);
  color: var(--accentDk);
  border-bottom-color: var(--accentBorder);
}
.column--ready .column-head {
  background: var(--ink);
  color: var(--lime);
  border-bottom-color: color-mix(in srgb, var(--paperHi) 18%, transparent);
}
.column--ready .count-pill {
  background: var(--lime);
  color: var(--onLime);
  border-color: transparent;
}

/* Dark-mode header overrides: white text for "În pregătire", pink for "Finalizată". */
[data-theme="dark"] .column--preparing .column-head { color: #FFFFFF; }
[data-theme="dark"] .column--ready .column-head { color: var(--accent); }
[data-theme="dark"] .column--ready .count-pill {
  background: var(--accent);
  color: #FFFFFF;
}
.column-body {
  flex: 1;
  display: flex;
  flex-direction: column;
  gap: 10px;
}
.kanban-card {
  background: var(--card);
  border: 1px solid var(--ink12);
  border-radius: 16px;
  padding: 12px;
  cursor: grab;
  transition: transform 0.12s ease, box-shadow 0.18s ease, border-color 0.18s ease;
}
.kanban-card:hover { box-shadow: var(--shadow-soft); border-color: var(--ink); }
.kanban-card:active { cursor: grabbing; }
.kanban-card--dragging { opacity: 0.5; }
.kanban-card--focus {
  border-color: var(--accent);
  box-shadow: 0 0 0 3px color-mix(in srgb, var(--accent) 30%, transparent);
}
.kc-top { display: flex; justify-content: space-between; gap: 10px; align-items: center; }
.kc-num {
  background: var(--ink);
  color: var(--paperHi);
  padding: 6px 10px;
  border-radius: 12px;
  display: inline-flex;
  flex-direction: column;
  align-items: flex-start;
}
.kc-num span:last-child { color: var(--lime); }
.kc-table {
  display: inline-flex;
  flex-direction: column;
  align-items: flex-end;
}
.kc-items {
  margin: 10px 0 0;
  padding: 0;
  list-style: none;
  display: flex;
  flex-direction: column;
  gap: 4px;
}
.kc-item {
  display: flex;
  gap: 8px;
  align-items: baseline;
  font-size: 14px;
  color: var(--ink);
}
.kc-qty {
  font-family: 'JetBrains Mono', monospace;
  font-weight: 700;
  font-size: 12.5px;
  color: var(--ink70);
  min-width: 26px;
}
.kc-name { font-weight: 500; }
.kc-bot {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-top: 12px;
  padding-top: 10px;
  border-top: 1px solid var(--ink06);
}
.countdown {
  font-family: 'JetBrains Mono', monospace;
  font-size: 11.5px;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  padding: 4px 10px;
  border-radius: 999px;
  background: var(--lime);
  color: var(--onLime);
  border: 1px solid var(--ink12);
}
.countdown--urgent {
  background: var(--accentSoft);
  color: var(--accentDk);
  border-color: var(--accentBorder);
}
.empty-slot {
  flex: 1;
  display: grid;
  place-items: center;
  text-align: center;
  padding: 30px 12px;
  font-family: 'JetBrains Mono', monospace;
  font-size: 12.5px;
  color: var(--ink40);
  text-transform: uppercase;
  letter-spacing: 0.05em;
  border: 1px dashed var(--ink12);
  border-radius: 14px;
}
</style>
