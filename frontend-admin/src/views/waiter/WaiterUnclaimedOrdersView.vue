<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import type { WaiterUnclaimedOrderDto } from '@shared/types/waiter'
import { waiterApi } from '@/services/waiterApi'
import { useAuthStore } from '@/stores/useAuthStore'
import { useWaiterRealtimeStore } from '@/stores/useWaiterRealtimeStore'
import { useToast } from '@/composables/useToast'
import { formatMoney } from '@/utils/money'

/**
 * "Comenzi nepreluate": QR rounds submitted at tables no waiter has claimed,
 * grouped by table. "Preia masa" claims the table — the backend adopts every
 * orphan round onto the new bill in the same motion, so the group disappears
 * from here and the table shows up under "Mesele mele".
 *
 * Refresh model: the feed refetches whenever the realtime store lands a new
 * floor snapshot (every relevant StaffHub event, reconnects, and the 15s poll
 * all funnel through it), so this view needs no timer of its own.
 */
const router = useRouter()
const auth = useAuthStore()
const store = useWaiterRealtimeStore()
const toast = useToast()

const feed = ref<WaiterUnclaimedOrderDto[]>([])
const isLoading = ref(false)
const claimingTableId = ref<string | null>(null)
const now = ref(Date.now())
let tickTimer: number | null = null

const STATUS_LABELS: Record<string, string> = {
  pending_dispatch: 'Se trimite',
  submitted: 'La bucătărie',
  preparing: 'În pregătire',
  ready: 'Gata de servit',
}

interface TableGroup {
  tableId: string
  tableNumber: string | null
  orders: WaiterUnclaimedOrderDto[]
  total: number
}

const groups = computed<TableGroup[]>(() => {
  const byTable = new Map<string, TableGroup>()
  for (const order of feed.value) {
    let group = byTable.get(order.tableId)
    if (!group) {
      group = { tableId: order.tableId, tableNumber: order.tableNumber, orders: [], total: 0 }
      byTable.set(order.tableId, group)
    }
    group.orders.push(order)
    group.total += order.totalAmount
  }
  // Numeric-aware sort, same rule as the floor ("2" before "10").
  return [...byTable.values()].sort((a, b) =>
    (a.tableNumber ?? '').localeCompare(b.tableNumber ?? '', 'ro', { numeric: true }),
  )
})

function statusLabel(status: string): string {
  return STATUS_LABELS[status] ?? status
}

function ago(createdAt: string): string {
  const secs = Math.max(0, Math.floor((now.value - new Date(createdAt).getTime()) / 1000))
  if (secs < 60) return 'chiar acum'
  const mins = Math.floor(secs / 60)
  if (mins < 60) return `acum ${mins} min`
  return `acum ${Math.floor(mins / 60)} h`
}

let fetching = false
async function loadFeed() {
  if (fetching) return
  fetching = true
  isLoading.value = true
  try {
    feed.value = await waiterApi.getUnclaimedOrders()
  } catch {
    // Keep the last good list; the next floor refresh retriggers us.
  } finally {
    fetching = false
    isLoading.value = false
  }
}

/**
 * Claim the table: the round(s) leave this list and land on the caller's bill.
 * On the 409 the message already names the colleague who won the race.
 */
async function claimTable(group: TableGroup) {
  if (claimingTableId.value) return
  claimingTableId.value = group.tableId
  try {
    const session = await waiterApi.claimTable(group.tableId)
    toast.success(
      group.tableNumber
        ? `Ai preluat masa ${group.tableNumber} — comenzile ei sunt acum pe nota ta.`
        : 'Ai preluat masa — comenzile ei sunt acum pe nota ta.',
    )
    void store.refreshFloor()
    void loadFeed()
    router.push({ name: 'waiter-table', params: { tableId: session.tableId } })
  } catch (err) {
    toast.error(err instanceof Error ? err.message : 'Masa nu a putut fi preluată.')
    void store.refreshFloor()
    void loadFeed()
  } finally {
    claimingTableId.value = null
  }
}

// Every floor snapshot (event-triggered, reconnect, or the 15s poll) refreshes
// the feed too — the store is this view's clock.
watch(
  () => store.floor,
  () => void loadFeed(),
)

async function load() {
  const rid = auth.activeRestaurantId
  if (rid) await store.start(rid)
  await loadFeed()
}

onMounted(() => {
  void load()
  tickTimer = window.setInterval(() => (now.value = Date.now()), 15000)
})
onBeforeUnmount(() => {
  if (tickTimer !== null) window.clearInterval(tickTimer)
})
</script>

<template>
  <div>
    <header class="uo-head">
      <div class="min-w-0">
        <div class="eyebrow">Sala</div>
        <h1 class="uo-title">Comenzi nepreluate</h1>
      </div>
      <button
        type="button"
        class="icon-btn"
        title="Reîncarcă"
        aria-label="Reîncarcă"
        :disabled="isLoading"
        @click="loadFeed"
      >
        <span :class="{ 'animate-spin': isLoading }" aria-hidden="true">↻</span>
      </button>
    </header>

    <p class="uo-hint">
      Comenzi trimise de la codul QR al unor mese fără ospătar. Preia masa și comenzile
      ei intră pe nota ta, la „Mesele mele”.
    </p>

    <div v-if="!groups.length" class="uo-empty">
      <div class="uo-empty-mark" aria-hidden="true">✓</div>
      <p class="uo-empty-title">Nicio comandă nepreluată</p>
      <p class="uo-empty-text">Când o masă liberă comandă de la QR, apare aici.</p>
    </div>

    <div v-else class="uo-groups">
      <section v-for="group in groups" :key="group.tableId" class="card-white uo-group">
        <header class="uo-group-head">
          <div class="uo-table">
            <span class="uo-table-eyebrow">Masa</span>
            <span class="uo-table-value">{{ group.tableNumber ?? '—' }}</span>
          </div>
          <div class="uo-group-meta">
            <span class="uo-count">
              {{ group.orders.length }}
              {{ group.orders.length === 1 ? 'comandă' : 'comenzi' }}
            </span>
            <span class="uo-total">{{ formatMoney(group.total) }} RON</span>
          </div>
        </header>

        <article v-for="order in group.orders" :key="order.id" class="uo-order">
          <div class="uo-order-head">
            <span class="uo-order-num">#{{ order.orderNumber ?? '—' }}</span>
            <span class="uo-status">{{ statusLabel(order.status) }}</span>
            <span class="uo-ago">{{ ago(order.createdAt) }}</span>
          </div>
          <ul class="uo-items">
            <li v-for="item in order.items" :key="item.id" class="uo-item">
              <span class="uo-qty">×{{ item.quantity }}</span>
              <span class="uo-body">
                <span class="uo-name">{{ item.productName }}</span>
                <span v-if="item.notes" class="uo-note">Obs: {{ item.notes }}</span>
              </span>
              <span class="uo-price">{{ formatMoney(item.quantity * item.unitPriceAtPurchase) }}</span>
            </li>
          </ul>
        </article>

        <button
          type="button"
          class="uo-claim"
          :disabled="claimingTableId !== null"
          @click="claimTable(group)"
        >
          {{ claimingTableId === group.tableId ? 'Se preia…' : `Preia masa ${group.tableNumber ?? ''}` }}
        </button>
      </section>
    </div>
  </div>
</template>

<style scoped>
.uo-head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 12px;
  margin-bottom: 8px;
}
.uo-title {
  font-size: 22px;
  font-weight: 800;
  line-height: 1.15;
  color: var(--ink);
}
.uo-hint {
  font-size: 13px;
  color: var(--ink70);
  margin-bottom: 14px;
}

/* ── Empty state ─────────────────────────────────────────────────────────── */
.uo-empty {
  margin-top: 8px;
  padding: 42px 24px;
  border-radius: 22px;
  background: var(--card, var(--paperHi));
  border: 1px dashed var(--ink12);
  display: grid;
  place-items: center;
  text-align: center;
  gap: 4px;
}
.uo-empty-mark {
  width: 52px;
  height: 52px;
  border-radius: 50%;
  display: grid;
  place-items: center;
  background: var(--lime);
  color: var(--onLime, #15140f);
  font-size: 24px;
  font-weight: 800;
  margin-bottom: 8px;
}
.uo-empty-title {
  font-size: 18px;
  font-weight: 700;
  color: var(--ink);
}
.uo-empty-text {
  font-size: 13.5px;
  color: var(--ink70);
  max-width: 30ch;
}

/* ── Table groups ────────────────────────────────────────────────────────── */
.uo-groups {
  display: flex;
  flex-direction: column;
  gap: 12px;
}
.uo-group {
  padding: 14px;
  border: 1px solid var(--accentBorder);
}
.uo-group-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  padding-bottom: 10px;
  border-bottom: 1px solid var(--ink06);
}
.uo-table {
  display: inline-flex;
  flex-direction: column;
}
.uo-table-eyebrow {
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 10px;
  text-transform: uppercase;
  letter-spacing: 0.09em;
  color: var(--ink70);
}
.uo-table-value {
  font-size: 30px;
  font-weight: 800;
  line-height: 1;
  color: var(--ink);
}
.uo-group-meta {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  gap: 4px;
}
.uo-count {
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 10.5px;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  padding: 3px 8px;
  border-radius: 999px;
  background: var(--ink06);
  color: var(--ink70);
}
.uo-total {
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 11px;
  font-weight: 700;
  padding: 3px 8px;
  border-radius: 999px;
  background: var(--lime);
  color: var(--onLime);
}

/* ── One round ───────────────────────────────────────────────────────────── */
.uo-order {
  padding: 10px 0;
  border-bottom: 1px solid var(--ink06);
}
.uo-order-head {
  display: flex;
  align-items: baseline;
  gap: 8px;
}
.uo-order-num {
  font-size: 15px;
  font-weight: 800;
  color: var(--ink);
}
.uo-status {
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 10px;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  padding: 2px 8px;
  border-radius: 999px;
  background: var(--accentSoft);
  color: var(--accentDk);
  border: 1px solid var(--accentBorder);
}
.uo-ago {
  margin-left: auto;
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 10.5px;
  color: var(--ink40);
}
.uo-items {
  margin: 8px 0 0;
  padding: 0;
  list-style: none;
  display: flex;
  flex-direction: column;
  gap: 4px;
}
.uo-item {
  display: flex;
  gap: 8px;
  align-items: baseline;
  font-size: 14px;
  color: var(--ink);
}
.uo-qty {
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-weight: 700;
  font-size: 12.5px;
  color: var(--ink70);
  min-width: 26px;
}
.uo-body {
  display: flex;
  flex-direction: column;
  min-width: 0;
  flex: 1;
}
.uo-name { font-weight: 500; }
.uo-note {
  font-size: 12px;
  font-style: italic;
  color: var(--ink40);
  line-height: 1.3;
}
.uo-price {
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 12px;
  color: var(--ink70);
  flex-shrink: 0;
}

/* ── Claim ───────────────────────────────────────────────────────────────── */
.uo-claim {
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
  background: var(--accent);
  color: var(--onAccent);
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 13px;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  cursor: pointer;
  transition: filter 0.16s ease;
}
.uo-claim:active { filter: brightness(1.1); }
.uo-claim:disabled { opacity: 0.6; cursor: default; }
</style>
