<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import {
  apiClient,
  type ActiveOrder,
  type ActiveOrderStatus,
} from '@/services/api-client'
import { useAuthStore } from '@/stores/useAuthStore'
import { useToast } from '@/composables/useToast'
import { formatMoney } from '@/utils/money'

/**
 * "Comenzi active" inside Waiter Mode: the same feed as the admin kanban
 * (`/orders/active`), rendered phone-first in the waiter shell so a waiter never
 * has to jump back to the admin console mid-service. Same data source, same 5s
 * poll, same permission rules: managers act on anything, a waiter only on the
 * orders they personally claimed — claiming is offered right on the card here,
 * since "Sala" (the admin floor plan) is out of reach on this screen.
 */
const auth = useAuthStore()
const toast = useToast()

const orders = ref<ActiveOrder[]>([])
const isLoading = ref(false)
const now = ref(Date.now())
const activeStatus = ref<ActiveOrderStatus>('submitted')
const busyOrderIds = ref<Set<string>>(new Set())

let pollTimer: number | null = null
let tickTimer: number | null = null

// Mirrors backend OrderFinalizationService.GracePeriod (same as the admin board).
const GRACE_SECONDS = 45

const SEGMENTS: Array<{ key: ActiveOrderStatus; label: string }> = [
  { key: 'submitted', label: 'Comandă' },
  { key: 'preparing', label: 'În pregătire' },
  { key: 'ready', label: 'Finalizată' },
]

const NEXT_STATUS: Partial<Record<ActiveOrderStatus, ActiveOrderStatus>> = {
  submitted: 'preparing',
  preparing: 'ready',
}

// Gating place ③ — cancel stays manager-only (the admin cancel endpoint is not
// on the waiter allow-list, so showing the button to a waiter would only 403).
const isManager = computed(() => auth.canManageWaitersOfActive)
const myUserId = computed(() => auth.user?.id ?? null)

function canInteract(order: ActiveOrder): boolean {
  if (isManager.value) return true
  return !!order.claimedByWaiterId && order.claimedByWaiterId === myUserId.value
}

const byStatus = computed<Record<ActiveOrderStatus, ActiveOrder[]>>(() => {
  const buckets: Record<ActiveOrderStatus, ActiveOrder[]> = {
    submitted: [],
    preparing: [],
    ready: [],
  }
  for (const o of orders.value) buckets[o.status]?.push(o)
  // Oldest first — the kitchen works FIFO.
  for (const k of Object.keys(buckets) as ActiveOrderStatus[]) {
    buckets[k].sort((a, b) => new Date(a.createdAt).getTime() - new Date(b.createdAt).getTime())
  }
  return buckets
})

const visibleOrders = computed(() => byStatus.value[activeStatus.value])

function nextLabel(status: ActiveOrderStatus): string | null {
  const next = NEXT_STATUS[status]
  if (!next) return null
  return SEGMENTS.find((s) => s.key === next)?.label ?? null
}

function secondsLeft(order: ActiveOrder): number | null {
  if (order.status !== 'ready' || !order.readyAt) return null
  const elapsed = (now.value - new Date(order.readyAt).getTime()) / 1000
  return Math.max(0, Math.ceil(GRACE_SECONDS - elapsed))
}

function ago(createdAt: string): string {
  const secs = Math.max(0, Math.floor((now.value - new Date(createdAt).getTime()) / 1000))
  if (secs < 60) return 'chiar acum'
  const mins = Math.floor(secs / 60)
  if (mins < 60) return `acum ${mins} min`
  return `acum ${Math.floor(mins / 60)} h`
}

function withBusy(orderId: string, on: boolean) {
  const next = new Set(busyOrderIds.value)
  if (on) next.add(orderId)
  else next.delete(orderId)
  busyOrderIds.value = next
}

async function fetchActive() {
  isLoading.value = true
  try {
    orders.value = await apiClient.getActiveOrders()
  } catch {
    // Keep the last good list; the next poll reconciles it.
  } finally {
    isLoading.value = false
  }
}

/** "Preia comanda" — same order-level claim the admin floor plan offers. */
async function claim(order: ActiveOrder) {
  if (busyOrderIds.value.has(order.id)) return
  withBusy(order.id, true)
  try {
    const result = await apiClient.claimOrder(order.id)
    order.claimedByWaiterId = result.claimedByWaiterId
    order.claimedByName = result.claimedByName ?? auth.user?.fullName ?? null
    toast.success(`Ai preluat comanda #${order.orderNumber ?? '—'}.`)
  } catch (err) {
    // 409: the API's message already names the colleague who claimed first.
    toast.error(err instanceof Error ? err.message : 'Comanda nu a putut fi preluată.')
    await fetchActive()
  } finally {
    withBusy(order.id, false)
  }
}

async function advance(order: ActiveOrder) {
  const target = NEXT_STATUS[order.status]
  if (!target || !canInteract(order) || busyOrderIds.value.has(order.id)) return

  // Optimistic, exactly like the admin board: snap now, roll back on failure.
  const prevStatus = order.status
  const prevReadyAt = order.readyAt
  order.status = target
  order.readyAt = target === 'ready' ? new Date().toISOString() : null
  withBusy(order.id, true)
  try {
    const updated = await apiClient.updateOrderStatus(order.id, target)
    order.readyAt = updated.readyAt ?? order.readyAt
  } catch (err) {
    order.status = prevStatus
    order.readyAt = prevReadyAt
    toast.error(err instanceof Error ? err.message : 'Mutarea comenzii a eșuat.')
  } finally {
    withBusy(order.id, false)
  }
}

async function cancelOrder(order: ActiveOrder) {
  if (!window.confirm(`Anulezi comanda #${order.orderNumber ?? '—'}? Stocul consumat va fi pus la loc.`)) return

  const idx = orders.value.findIndex((o) => o.id === order.id)
  const removed = idx !== -1 ? orders.value.splice(idx, 1)[0] : null
  try {
    await apiClient.cancelOrder(order.id)
  } catch (err) {
    if (removed) orders.value.push(removed)
    toast.error(err instanceof Error ? err.message : 'Anularea comenzii a eșuat.')
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
  <div>
    <header class="orders-head">
      <div class="min-w-0">
        <div class="eyebrow">Bucătărie</div>
        <h1 class="orders-title">Comenzi active</h1>
      </div>
      <button
        type="button"
        class="icon-btn"
        title="Reîncarcă"
        aria-label="Reîncarcă"
        :disabled="isLoading"
        @click="fetchActive"
      >
        <span :class="{ 'animate-spin': isLoading }" aria-hidden="true">↻</span>
      </button>
    </header>

    <!-- Status segments — the phone version of the kanban's three columns. -->
    <div class="seg-bar" role="tablist" aria-label="Filtrează comenzile după stare">
      <button
        v-for="seg in SEGMENTS"
        :key="seg.key"
        type="button"
        class="seg"
        :class="[`seg--${seg.key}`, { 'seg--active': activeStatus === seg.key }]"
        role="tab"
        :aria-selected="activeStatus === seg.key"
        @click="activeStatus = seg.key"
      >
        <span class="seg-label">{{ seg.label }}</span>
        <span class="seg-count">{{ byStatus[seg.key].length }}</span>
      </button>
    </div>

    <p v-if="!visibleOrders.length" class="orders-empty">
      Nicio comandă în această stare.
    </p>

    <div v-else class="order-list">
      <article
        v-for="order in visibleOrders"
        :key="order.id"
        class="card-white order-card"
        :class="{ 'order-card--locked': !canInteract(order) && order.claimedByWaiterId }"
      >
        <div class="oc-top">
          <div class="oc-num">
            <span class="oc-num-eyebrow">Comanda</span>
            <span class="oc-num-value">#{{ order.orderNumber ?? '—' }}</span>
          </div>
          <div class="oc-table">
            <span class="oc-table-eyebrow">Masa</span>
            <span class="oc-table-value">{{ order.tableNumber ?? '?' }}</span>
          </div>
        </div>

        <div class="oc-meta">
          <span class="oc-ago">{{ ago(order.createdAt) }}</span>
          <span v-if="order.source === 'waiter'" class="tag-pill oc-source">Ospătar</span>
        </div>

        <ul class="oc-items">
          <li v-for="item in order.items" :key="item.id" class="oc-item">
            <span class="oc-qty">×{{ item.quantity }}</span>
            <span class="oc-body">
              <span class="oc-name">{{ item.productName }}</span>
              <span v-if="item.notes" class="oc-note">Obs: {{ item.notes }}</span>
            </span>
          </li>
        </ul>

        <div class="oc-claim">
          <span v-if="order.claimedByWaiterId" class="claim-chip" :title="`Preluat de ${order.claimedByName}`">
            <span aria-hidden="true">✓</span>
            {{ order.claimedByWaiterId === myUserId ? 'Preluată de tine' : `Preluat · ${order.claimedByName || 'coleg'}` }}
          </span>
          <span v-else class="claim-chip claim-chip--open">Nepreluată</span>
        </div>

        <footer class="oc-bot">
          <span class="oc-total">{{ formatMoney(order.totalAmount) }} RON</span>
          <div class="oc-bot-right">
            <span
              v-if="secondsLeft(order) !== null"
              class="oc-countdown"
              :class="{ 'oc-countdown--urgent': (secondsLeft(order) ?? 99) <= 10 }"
            >
              arhivare {{ secondsLeft(order) }}s
            </span>
            <button
              v-if="isManager"
              type="button"
              class="oc-cancel"
              @click="cancelOrder(order)"
            >Anulează</button>
          </div>
        </footer>

        <!-- Primary action: claim it, or move it one step forward. -->
        <button
          v-if="!order.claimedByWaiterId"
          type="button"
          class="oc-action oc-action--claim"
          :disabled="busyOrderIds.has(order.id)"
          @click="claim(order)"
        >
          {{ busyOrderIds.has(order.id) ? 'Se preia…' : 'Preia comanda' }}
        </button>
        <button
          v-else-if="canInteract(order) && nextLabel(order.status)"
          type="button"
          class="oc-action"
          :disabled="busyOrderIds.has(order.id)"
          @click="advance(order)"
        >
          <span aria-hidden="true">→</span> {{ nextLabel(order.status) }}
        </button>
      </article>
    </div>
  </div>
</template>

<style scoped>
.orders-head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 12px;
  margin-bottom: 14px;
}
.orders-title {
  font-size: 22px;
  font-weight: 800;
  line-height: 1.15;
  color: var(--ink);
}

/* ── Status segments ─────────────────────────────────────────────────────── */
.seg-bar {
  display: flex;
  gap: 8px;
  margin-bottom: 14px;
}
.seg {
  flex: 1;
  min-height: var(--tap-min);
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 3px;
  padding: 8px 4px;
  border: 1px solid var(--ink12);
  border-radius: 14px;
  background: var(--card);
  color: var(--ink70);
  cursor: pointer;
  transition: background 0.16s ease, color 0.16s ease, border-color 0.16s ease;
}
.seg-label {
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 10.5px;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.03em;
  text-align: center;
  line-height: 1.15;
}
.seg-count {
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-weight: 700;
  font-size: 11.5px;
  padding: 1px 8px;
  border-radius: 999px;
  background: rgba(0, 0, 0, 0.1);
  color: inherit;
}
/* Active segment borrows the kanban's per-column colour language. */
.seg--active.seg--submitted {
  background: var(--lime);
  color: var(--onLime);
  border-color: transparent;
}
.seg--active.seg--preparing {
  background: var(--accentSoft);
  color: var(--accentDk);
  border-color: var(--accentBorder);
}
.seg--active.seg--ready {
  background: var(--ink);
  color: var(--lime);
  border-color: transparent;
}
.seg--active .seg-count { background: rgba(0, 0, 0, 0.18); }
[data-theme='dark'] .seg--active.seg--preparing { color: #ffffff; }
[data-theme='dark'] .seg--active.seg--ready { color: var(--accent); }

.orders-empty {
  padding: 40px 12px;
  text-align: center;
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 12.5px;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  color: var(--ink40);
  border: 1px dashed var(--ink12);
  border-radius: 18px;
}

/* ── Order cards ─────────────────────────────────────────────────────────── */
.order-list {
  display: flex;
  flex-direction: column;
  gap: 10px;
}
.order-card {
  padding: 12px;
  border: 1px solid var(--ink12);
}
.order-card--locked {
  opacity: 0.55;
  border-style: dashed;
}
.oc-top {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 10px;
}
.oc-num {
  background: var(--ink);
  color: var(--paperHi);
  padding: 6px 10px;
  border-radius: 12px;
  display: inline-flex;
  flex-direction: column;
  align-items: flex-start;
}
.oc-num-eyebrow {
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 10px;
  text-transform: uppercase;
  letter-spacing: 0.06em;
  color: rgba(245, 240, 232, 0.7);
}
.oc-num-value {
  font-size: 18px;
  font-weight: 700;
  line-height: 1;
  margin-top: 2px;
  color: var(--lime);
}
.oc-table {
  display: inline-flex;
  flex-direction: column;
  align-items: flex-end;
}
.oc-table-eyebrow {
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 10px;
  text-transform: uppercase;
  letter-spacing: 0.06em;
  color: var(--ink70);
}
.oc-table-value {
  font-size: 20px;
  font-weight: 800;
  line-height: 1;
  color: var(--ink);
}
.oc-meta {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-top: 8px;
}
.oc-ago {
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 11px;
  color: var(--ink40);
}
.oc-source {
  padding: 3px 9px;
  font-size: 10.5px;
  letter-spacing: 0.05em;
}
.oc-items {
  margin: 10px 0 0;
  padding: 0;
  list-style: none;
  display: flex;
  flex-direction: column;
  gap: 4px;
}
.oc-item {
  display: flex;
  gap: 8px;
  align-items: baseline;
  font-size: 14px;
  color: var(--ink);
}
.oc-qty {
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-weight: 700;
  font-size: 12.5px;
  color: var(--ink70);
  min-width: 26px;
}
.oc-body {
  display: flex;
  flex-direction: column;
  min-width: 0;
}
.oc-name { font-weight: 500; }
.oc-note {
  font-size: 12px;
  font-style: italic;
  color: var(--ink40);
  line-height: 1.3;
}
.oc-claim { margin-top: 10px; }
.claim-chip {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 11px;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  padding: 4px 10px;
  border-radius: 999px;
  background: var(--lime);
  color: var(--onLime);
  max-width: 100%;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.claim-chip--open {
  background: var(--accentSoft);
  color: var(--accentDk);
  border: 1px solid var(--accentBorder);
}
.oc-bot {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-top: 12px;
  padding-top: 10px;
  border-top: 1px solid var(--ink06);
}
.oc-total {
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 12px;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  color: var(--ink70);
}
.oc-bot-right {
  display: flex;
  align-items: center;
  gap: 8px;
}
.oc-countdown {
  font-family: 'JetBrains Mono', ui-monospace, monospace;
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
.oc-countdown--urgent {
  background: var(--accentSoft);
  color: var(--accentDk);
  border-color: var(--accentBorder);
}
.oc-cancel {
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 11px;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  padding: 4px 10px;
  border-radius: 999px;
  background: transparent;
  color: var(--accent);
  border: 1px solid var(--accentBorder);
  cursor: pointer;
  transition: background 0.15s ease;
}
.oc-cancel:active { background: var(--accentSoft); }

/* Primary per-card action — claim, or one step forward. */
.oc-action {
  width: 100%;
  margin-top: 12px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  min-height: var(--tap-min);
  padding: 11px 12px;
  border: none;
  border-radius: 12px;
  background: var(--ink);
  color: var(--lime);
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 13px;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  cursor: pointer;
  transition: filter 0.16s ease;
}
.oc-action:active { filter: brightness(1.15); }
.oc-action:disabled { opacity: 0.6; cursor: default; }
.oc-action--claim {
  background: var(--accent);
  color: var(--onAccent);
}
[data-theme='dark'] .oc-action { color: var(--accent); }
[data-theme='dark'] .oc-action--claim { color: var(--onAccent); }
</style>
