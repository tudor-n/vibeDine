<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import type { OrderStatus } from '@shared/types/models'
import type {
  SessionPaymentMethod,
  WaiterOrderDto,
  WaiterSessionDetailDto,
  WaiterStaffMemberDto,
} from '@shared/types/waiter'
import { waiterApi } from '@/services/waiterApi'
import { useAuthStore } from '@/stores/useAuthStore'
import { useWaiterRealtimeStore } from '@/stores/useWaiterRealtimeStore'
import { useWaiterCartStore } from '@/stores/useWaiterCartStore'
import { useToast } from '@/composables/useToast'
import { formatMoney } from '@/utils/money'

/**
 * One table's service session: its rounds, the running total, and the ways out
 * of it — a new round, a transfer to a colleague, or the bill.
 *
 * The session id is resolved from the realtime store by `:tableId` rather than
 * carried in the URL, so a stale link can never point at a closed session. A
 * table that is free (or belongs to someone else, unless the viewer is a
 * manager) bounces back to the floor with a Romanian toast.
 */
const route = useRoute()
const router = useRouter()
const auth = useAuthStore()
const store = useWaiterRealtimeStore()
const cart = useWaiterCartStore()
const toast = useToast()

const tableId = computed(() => String(route.params.tableId))

const detail = ref<WaiterSessionDetailDto | null>(null)
const isLoading = ref(false)
const error = ref<string | null>(null)
const cancellingId = ref<string | null>(null)

// Gating place ③ — force-release and foreign-table access are manager paths.
const isManager = computed(() => auth.canManageWaitersOfActive)

const session = computed(() => detail.value?.session ?? null)
const orders = computed<WaiterOrderDto[]>(() => detail.value?.orders ?? [])
const cumulativeTotal = computed(() => detail.value?.cumulativeTotal ?? 0)
const isOwner = computed(() => session.value?.waiterUserId === auth.user?.id)

// A round already on its way to the kitchen blocks the bill (B.3-6); surfacing
// it up front is friendlier than letting the close 409.
const hasDispatchingOrder = computed(() =>
  orders.value.some((o) => o.status === 'pending_dispatch'),
)

const openedAtLabel = computed(() => {
  const at = session.value?.openedAt
  if (!at) return ''
  return new Date(at).toLocaleTimeString('ro-RO', { hour: '2-digit', minute: '2-digit' })
})

// ── Status vocabulary (Romanian, exact-literal statuses) ────────────────────
const STATUS_LABELS: Partial<Record<OrderStatus, string>> = {
  draft: 'Ciornă',
  pending_dispatch: 'În așteptare',
  submitted: 'Trimisă',
  preparing: 'În pregătire',
  ready: 'Gata',
  completed: 'Finalizată',
  cancelled: 'Anulată',
  merged: 'Unificată',
}
const CANCELLABLE: OrderStatus[] = [
  'draft',
  'pending_dispatch',
  'submitted',
  'preparing',
  'ready',
]

function statusLabel(status: OrderStatus): string {
  return STATUS_LABELS[status] ?? status
}
function canCancel(order: WaiterOrderDto): boolean {
  return (isOwner.value || isManager.value) && CANCELLABLE.includes(order.status)
}
function orderTime(order: WaiterOrderDto): string {
  return new Date(order.createdAt).toLocaleTimeString('ro-RO', {
    hour: '2-digit',
    minute: '2-digit',
  })
}

// ── Loading ─────────────────────────────────────────────────────────────────
function leaveToFloor(message: string) {
  toast.info(message)
  router.replace({ name: 'waiter-floor' })
}

let fetchInFlight = false
async function fetchSession(sessionId: string) {
  if (fetchInFlight) return
  fetchInFlight = true
  try {
    detail.value = await waiterApi.getSession(sessionId)
    error.value = null
  } catch (err) {
    error.value = err instanceof Error ? err.message : 'Sesiunea nu a putut fi încărcată.'
  } finally {
    fetchInFlight = false
  }
}

async function load() {
  const rid = auth.activeRestaurantId
  if (!rid) return
  isLoading.value = true
  try {
    // App.vue owns the lifecycle; both calls are safe no-ops when already warm.
    await store.start(rid)
    if (!store.floor) await store.refreshFloor()

    let table = store.tableById(tableId.value)
    if (!table?.session) {
      // A warm floor snapshot can predate a claim made a moment ago (the claim
      // flows fire refreshFloor without awaiting it, then navigate here) — the
      // session exists server-side but not in this copy yet. Re-read once
      // before concluding the table really has no open session.
      await store.refreshFloor()
      table = store.tableById(tableId.value)
    }
    if (!table?.session) {
      leaveToFloor('Masa nu mai are o sesiune deschisă.')
      return
    }
    if (table.session.waiterUserId !== auth.user?.id && !isManager.value) {
      leaveToFloor(`Masa este deschisă de ${table.session.waiterName}.`)
      return
    }
    await fetchSession(table.session.id)
  } finally {
    isLoading.value = false
  }
}
onMounted(load)

// The realtime store replaces `floor` wholesale on every event-triggered refresh
// and on the 15 s poll — that is the signal to reconcile this session.
watch(
  () => store.floor,
  () => {
    const current = session.value
    if (!current) return
    const table = store.tableById(tableId.value)
    if (!table?.session) {
      leaveToFloor('Masa a fost închisă.')
      return
    }
    if (table.session.waiterUserId !== auth.user?.id && !isManager.value) {
      leaveToFloor(`Masa a fost transferată către ${table.session.waiterName}.`)
      return
    }
    void fetchSession(table.session.id)
  },
)

// ── Actions ─────────────────────────────────────────────────────────────────
function newOrder() {
  router.push({ name: 'waiter-order', params: { tableId: tableId.value } })
}

async function cancelRound(order: WaiterOrderDto) {
  const label = order.orderNumber ? `#${order.orderNumber}` : 'ciorna'
  if (!window.confirm(`Anulezi comanda ${label}? Stocul consumat va fi pus la loc.`)) return
  cancellingId.value = order.id
  try {
    await waiterApi.cancelOrder(order.id)
    toast.success('Comanda a fost anulată.')
    if (session.value) await fetchSession(session.value.id)
    void store.refreshFloor()
  } catch (err) {
    toast.error(err instanceof Error ? err.message : 'Anularea comenzii a eșuat.')
  } finally {
    cancellingId.value = null
  }
}

// ── Transfer ────────────────────────────────────────────────────────────────
const transferOpen = ref(false)
const staff = ref<WaiterStaffMemberDto[]>([])
const staffLoading = ref(false)
const transferringTo = ref<string | null>(null)

const transferTargets = computed(() =>
  staff.value.filter((s) => s.userId !== session.value?.waiterUserId),
)

async function openTransfer() {
  transferOpen.value = true
  staffLoading.value = true
  try {
    staff.value = await waiterApi.getStaff()
  } catch (err) {
    toast.error(err instanceof Error ? err.message : 'Lista de colegi nu a putut fi încărcată.')
  } finally {
    staffLoading.value = false
  }
}

async function doTransfer(target: WaiterStaffMemberDto) {
  const current = session.value
  if (!current || transferringTo.value) return
  transferringTo.value = target.userId
  try {
    await waiterApi.transferSession(current.id, target.userId)
    transferOpen.value = false
    toast.success(`Masa a fost transferată către ${target.fullName}.`)
    await fetchSession(current.id)
    void store.refreshFloor()
    // A waiter who just gave the table away has nothing left to do here.
    if (!isManager.value && target.userId !== auth.user?.id) {
      router.replace({ name: 'waiter-floor' })
    }
  } catch (err) {
    toast.error(err instanceof Error ? err.message : 'Transferul a eșuat.')
  } finally {
    transferringTo.value = null
  }
}

// ── Bill / close ────────────────────────────────────────────────────────────
const billOpen = ref(false)
const paymentMethod = ref<SessionPaymentMethod>('cash')
const cashInput = ref('')
const cardInput = ref('')
const isClosing = ref(false)

const PAYMENT_LABELS: Record<SessionPaymentMethod, string> = {
  cash: 'Numerar',
  card: 'Card',
  mixed: 'Mixt',
}

function toAmount(raw: string): number {
  const parsed = Number.parseFloat(raw.replace(',', '.'))
  return Number.isFinite(parsed) ? parsed : 0
}
/** Compare in integer bani so 2 dp equality never trips on float error. */
function bani(value: number): number {
  return Math.round(value * 100)
}

const mixedSum = computed(() => toAmount(cashInput.value) + toAmount(cardInput.value))
const mixedMatches = computed(() => bani(mixedSum.value) === bani(cumulativeTotal.value))
const canClose = computed(() => {
  if (isClosing.value) return false
  if (paymentMethod.value === 'mixed') return mixedMatches.value
  return true
})

function openBill() {
  paymentMethod.value = 'cash'
  cashInput.value = formatMoney(cumulativeTotal.value)
  cardInput.value = '0.00'
  billOpen.value = true
}

function selectPayment(method: SessionPaymentMethod) {
  paymentMethod.value = method
  if (method === 'mixed') {
    // Seed the split so the running validation starts from a valid state.
    cashInput.value = formatMoney(cumulativeTotal.value)
    cardInput.value = '0.00'
  }
}

async function closeTable() {
  const current = session.value
  if (!current || !canClose.value) return
  isClosing.value = true
  try {
    await waiterApi.closeSession(current.id, {
      paymentMethod: paymentMethod.value,
      cashAmount: paymentMethod.value === 'mixed' ? toAmount(cashInput.value) : undefined,
      cardAmount: paymentMethod.value === 'mixed' ? toAmount(cardInput.value) : undefined,
    })
    billOpen.value = false
    cart.clear(tableId.value)
    toast.success(`Masa a fost închisă · ${formatMoney(cumulativeTotal.value)} RON.`)
    void store.refreshFloor()
    router.replace({ name: 'waiter-floor' })
  } catch (err) {
    // 409 while a round is still dispatching arrives as the API's Romanian message.
    toast.error(err instanceof Error ? err.message : 'Masa nu a putut fi închisă.')
  } finally {
    isClosing.value = false
  }
}

// ── Force release (manager only) ────────────────────────────────────────────
const isReleasing = ref(false)

async function forceRelease() {
  const current = session.value
  if (!current || isReleasing.value) return
  if (
    !window.confirm(
      `Eliberezi forțat masa ${current.tableNumber ?? ''}? Sesiunea se închide fără plată ` +
        'înregistrată, iar acțiunea rămâne în jurnalul de audit cu numele tău.',
    )
  ) {
    return
  }
  isReleasing.value = true
  try {
    await waiterApi.forceReleaseSession(current.id)
    toast.success('Masa a fost eliberată forțat.')
    void store.refreshFloor()
    router.replace({ name: 'waiter-floor' })
  } catch (err) {
    toast.error(err instanceof Error ? err.message : 'Eliberarea forțată a eșuat.')
  } finally {
    isReleasing.value = false
  }
}
</script>

<template>
  <div>
    <header class="table-head">
      <button
        type="button"
        class="icon-btn"
        title="Înapoi la mese"
        aria-label="Înapoi la mese"
        @click="router.push({ name: 'waiter-floor' })"
      >
        <span aria-hidden="true">←</span>
      </button>
      <div class="min-w-0">
        <div class="eyebrow">Masa {{ session?.tableNumber ?? '—' }}</div>
        <h1 class="table-title">{{ session?.waiterName ?? '…' }}</h1>
        <div v-if="openedAtLabel" class="table-sub">Deschisă la {{ openedAtLabel }}</div>
      </div>
    </header>

    <div v-if="error" class="alert-error" role="alert">
      <span class="alert-icon" aria-hidden="true">⚠</span>
      <div class="alert-body">
        <div class="alert-title">Eroare</div>
        <div>{{ error }}</div>
      </div>
    </div>

    <p v-if="isLoading && !detail" class="state-note">Se încarcă…</p>

    <template v-else-if="session">
      <!-- Running total -->
      <section class="total-card">
        <div class="eyebrow">Total sesiune</div>
        <div class="total-amount">{{ formatMoney(cumulativeTotal) }} <span>RON</span></div>
        <div class="total-meta">
          {{ orders.length }} {{ orders.length === 1 ? 'rundă' : 'runde' }}
          <template v-if="hasDispatchingOrder"> · o rundă pleacă spre bucătărie</template>
        </div>
      </section>

      <!-- Rounds -->
      <h2 class="section-title">Runde</h2>
      <p v-if="!orders.length" class="state-note">Nicio comandă pe această masă încă.</p>

      <article v-for="order in orders" :key="order.id" class="card-white round-card">
        <div class="round-head">
          <div>
            <div class="round-num">#{{ order.orderNumber ?? '—' }}</div>
            <div class="round-time">{{ orderTime(order) }}</div>
          </div>
          <div class="round-tags">
            <span v-if="order.source === 'waiter'" class="tag-pill">Ospătar</span>
            <span class="tag-pill">{{ statusLabel(order.status) }}</span>
          </div>
        </div>

        <ul class="round-items">
          <li v-for="item in order.items" :key="item.id" class="round-item">
            <span class="ri-qty">×{{ item.quantity }}</span>
            <span class="ri-body">
              <span class="ri-name">{{ item.productName }}</span>
              <span v-if="item.notes" class="ri-notes">Obs: {{ item.notes }}</span>
            </span>
            <span class="ri-price">
              {{ formatMoney(item.unitPriceAtPurchase * item.quantity) }}
            </span>
          </li>
        </ul>

        <footer class="round-foot">
          <span class="round-total">{{ formatMoney(order.totalAmount) }} RON</span>
          <button
            v-if="canCancel(order)"
            type="button"
            class="btn-danger"
            :disabled="cancellingId === order.id"
            @click="cancelRound(order)"
          >
            {{ cancellingId === order.id ? 'Se anulează…' : 'Anulează' }}
          </button>
        </footer>
      </article>

      <!-- Actions -->
      <div class="action-row">
        <button type="button" class="pill-btn" @click="newOrder">Comandă nouă</button>
        <button type="button" class="btn-ghost" @click="openTransfer">Transferă</button>
        <button type="button" class="pill-btn-ink" @click="openBill">Notă de plată</button>
      </div>

      <button
        v-if="isManager"
        type="button"
        class="btn-danger force-btn"
        :disabled="isReleasing"
        @click="forceRelease"
      >
        {{ isReleasing ? 'Se eliberează…' : 'Eliberează forțat' }}
      </button>
    </template>

    <!-- Transfer modal -->
    <div v-if="transferOpen" class="sheet-backdrop" @click="transferOpen = false">
      <div class="sheet" role="dialog" aria-modal="true" aria-label="Transferă masa" @click.stop>
        <div class="sheet-grip" aria-hidden="true"></div>
        <div class="eyebrow">Transferă masa</div>
        <h2 class="sheet-title">Către cine?</h2>

        <p v-if="staffLoading" class="state-note">Se încarcă colegii…</p>
        <p v-else-if="!transferTargets.length" class="state-note">Niciun coleg disponibil.</p>
        <ul v-else class="staff-list">
          <li v-for="member in transferTargets" :key="member.userId">
            <button
              type="button"
              class="staff-row"
              :disabled="transferringTo !== null"
              @click="doTransfer(member)"
            >
              <span class="staff-name">{{ member.fullName }}</span>
              <span class="staff-role">{{ member.role }}</span>
              <span class="staff-arrow" aria-hidden="true">→</span>
            </button>
          </li>
        </ul>

        <div class="sheet-actions">
          <button type="button" class="btn-ghost" @click="transferOpen = false">Renunță</button>
        </div>
      </div>
    </div>

    <!-- Bill sheet -->
    <div v-if="billOpen" class="sheet-backdrop" @click="billOpen = false">
      <div class="sheet" role="dialog" aria-modal="true" aria-label="Notă de plată" @click.stop>
        <div class="sheet-grip" aria-hidden="true"></div>
        <div class="eyebrow">Notă de plată</div>
        <h2 class="sheet-title">{{ formatMoney(cumulativeTotal) }} RON</h2>

        <div v-if="hasDispatchingOrder" class="alert-warning" role="status">
          <span class="alert-icon" aria-hidden="true">⚠</span>
          <div class="alert-body">
            <div class="alert-title">Rundă în curs</div>
            <div>O comandă pleacă încă spre bucătărie. Nota se poate închide după ce ajunge.</div>
          </div>
        </div>

        <div class="field-label">Metodă de plată</div>
        <div class="segmented" role="group" aria-label="Metodă de plată">
          <button
            v-for="method in (['cash', 'card', 'mixed'] as SessionPaymentMethod[])"
            :key="method"
            type="button"
            class="segment"
            :class="{ 'segment-active': paymentMethod === method }"
            :aria-pressed="paymentMethod === method"
            @click="selectPayment(method)"
          >
            {{ PAYMENT_LABELS[method] }}
          </button>
        </div>

        <div v-if="paymentMethod === 'mixed'" class="mixed-grid">
          <label class="mixed-field">
            <span class="field-label">Numerar</span>
            <input
              v-model="cashInput"
              class="field-input"
              type="text"
              inputmode="decimal"
              autocomplete="off"
            />
          </label>
          <label class="mixed-field">
            <span class="field-label">Card</span>
            <input
              v-model="cardInput"
              class="field-input"
              type="text"
              inputmode="decimal"
              autocomplete="off"
            />
          </label>
          <p class="mixed-sum" :class="{ 'mixed-sum-bad': !mixedMatches }">
            Suma introdusă: {{ formatMoney(mixedSum) }} RON ·
            <template v-if="mixedMatches">corespunde totalului</template>
            <template v-else>trebuie să fie exact {{ formatMoney(cumulativeTotal) }} RON</template>
          </p>
        </div>

        <div class="sheet-actions">
          <button type="button" class="btn-ghost" :disabled="isClosing" @click="billOpen = false">
            Renunță
          </button>
          <button type="button" class="pill-btn" :disabled="!canClose" @click="closeTable">
            {{ isClosing ? 'Se închide…' : 'Închide masa' }}
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.table-head {
  display: flex;
  align-items: flex-start;
  gap: 10px;
  margin-bottom: 14px;
}
.table-title {
  font-size: 21px;
  font-weight: 800;
  line-height: 1.15;
  color: var(--ink);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.table-sub {
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 11px;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  color: var(--ink40);
  margin-top: 2px;
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

/* ── Running total ───────────────────────────────────────────────────────── */
.total-card {
  background: var(--ink);
  color: var(--paperHi);
  border-radius: 22px;
  padding: 16px 18px;
  margin-bottom: 18px;
}
.total-card .eyebrow { color: var(--lime); }
.total-amount {
  font-size: 34px;
  font-weight: 800;
  line-height: 1.05;
  color: var(--lime);
}
.total-amount span {
  font-size: 15px;
  font-weight: 700;
}
.total-meta {
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 11px;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  opacity: 0.75;
  margin-top: 4px;
}

/* ── Rounds ──────────────────────────────────────────────────────────────── */
.round-card {
  padding: 13px 14px;
  margin-bottom: 10px;
}
.round-head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 10px;
}
.round-num {
  font-size: 17px;
  font-weight: 800;
  color: var(--ink);
}
.round-time {
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 11px;
  color: var(--ink40);
}
.round-tags {
  display: flex;
  flex-wrap: wrap;
  justify-content: flex-end;
  gap: 5px;
}
.round-items {
  list-style: none;
  margin: 10px 0 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 7px;
}
.round-item {
  display: flex;
  gap: 9px;
  align-items: baseline;
  font-size: 14px;
}
.ri-qty {
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-weight: 700;
  font-size: 12.5px;
  color: var(--ink40);
  min-width: 26px;
}
.ri-body {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-width: 0;
}
.ri-name { color: var(--ink); }
.ri-notes {
  font-style: italic;
  font-size: 12.5px;
  color: var(--ink40);
}
.ri-price {
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 12.5px;
  color: var(--ink70);
}
.round-foot {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  margin-top: 11px;
  padding-top: 9px;
  border-top: 1px solid var(--ink06);
}
.round-total {
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 13px;
  font-weight: 700;
  color: var(--ink);
}

/* ── Actions ─────────────────────────────────────────────────────────────── */
.action-row {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 9px;
  margin-top: 18px;
}
.action-row > * { min-height: var(--tap-min); }
.action-row > *:last-child { grid-column: 1 / -1; }
.force-btn {
  width: 100%;
  min-height: var(--tap-min);
  margin-top: 9px;
}

/* ── Sheets (shared shape with the floor view) ───────────────────────────── */
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

/* ── Transfer list ───────────────────────────────────────────────────────── */
.staff-list {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 7px;
}
.staff-row {
  width: 100%;
  min-height: var(--tap-min);
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 11px 14px;
  border-radius: 16px;
  border: 1px solid var(--ink12);
  background: var(--card);
  color: var(--ink);
  cursor: pointer;
  text-align: left;
  transition: background 0.15s ease, border-color 0.15s ease;
}
.staff-row:hover:not(:disabled) { background: var(--ink06); border-color: var(--ink); }
.staff-row:disabled { opacity: 0.5; cursor: not-allowed; }
.staff-name { flex: 1; font-weight: 600; }
.staff-role {
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 10.5px;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  color: var(--ink40);
}
.staff-arrow { color: var(--ink40); }

/* ── Payment segmented control ───────────────────────────────────────────── */
.segmented {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 7px;
  margin: 6px 0 14px;
}
.segment {
  min-height: var(--tap-min);
  border-radius: 14px;
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
.segment-active {
  background: var(--ink);
  color: var(--lime);
  border-color: var(--ink);
}
.mixed-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 10px;
}
.mixed-field {
  display: flex;
  flex-direction: column;
  gap: 4px;
}
.mixed-sum {
  grid-column: 1 / -1;
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 11.5px;
  color: var(--ink70);
}
.mixed-sum-bad { color: var(--accent); }
</style>
