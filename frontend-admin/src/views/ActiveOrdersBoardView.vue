<script setup lang="ts">
import { ref, computed, onMounted, onBeforeUnmount } from 'vue'
import { useRoute } from 'vue-router'
import { apiClient, auxApi, waiterApi, type ActiveOrder, type ActiveOrderStatus, type WaiterRow } from '@/services/api-client'
import type { MoveOrderToAuxPayload } from '@shared/types/models'
import { useAuthStore } from '@/stores/useAuthStore'
import { useToast } from '@/composables/useToast'
import { formatMoney } from '@/utils/money'

type ActiveStatus = ActiveOrderStatus

const route = useRoute()
const auth = useAuthStore()
const toast = useToast()
const orders = ref<ActiveOrder[]>([])
const isLoading = ref(false)
const error = ref<string | null>(null)
const now = ref(Date.now())
const dragOrderId = ref<string | null>(null)
const dragOverColumn = ref<ActiveStatus | null>(null)
const focusOrderId = computed(() => (route.query.focus as string | undefined) ?? null)

// On phones we collapse the 3-column board into a single column controlled by
// the filter bar; on desktop all three columns show side by side as a kanban.
const isMobile = ref(false)
const activeStatus = ref<ActiveStatus>('submitted')
let mql: MediaQueryList | null = null

let pollTimer: number | null = null
let tickTimer: number | null = null

// Mirrors backend OrderFinalizationService.GracePeriod.
const GRACE_SECONDS = 45

const COLUMNS: Array<{ key: ActiveStatus; label: string; hint: string }> = [
  { key: 'submitted', label: 'Comandă', hint: 'Tocmai sosite de la masă' },
  { key: 'preparing', label: 'În pregătire', hint: 'Sunt în lucru acum' },
  { key: 'ready', label: 'Finalizată', hint: `Se arhivează automat în ${GRACE_SECONDS}s` },
]

// Forward-only workflow used by the per-card action button: Comandă → În
// pregătire → Finalizată. `ready` is terminal (it auto-archives after the grace
// period), so it has no next step.
const NEXT_STATUS: Partial<Record<ActiveStatus, ActiveStatus>> = {
  submitted: 'preparing',
  preparing: 'ready',
}

function nextLabel(status: ActiveStatus): string | null {
  const next = NEXT_STATUS[status]
  if (!next) return null
  return COLUMNS.find((c) => c.key === next)?.label ?? null
}

// Who may move/advance an order:
//  • Managers (OrgOwner of the org, RestaurantAdmin of this location) — any order.
//  • Waiters — only the orders they personally claimed in „Sala".
// Everything else is shown read-only (greyed, no drag, no action button).
const myUserId = computed(() => auth.user?.id ?? null)
const isManager = computed(() => auth.canManageWaitersOfActive)
function canInteract(order: ActiveOrder): boolean {
  if (isManager.value) return true
  return !!order.claimedByWaiterId && order.claimedByWaiterId === myUserId.value
}

// Desktop shows every column; mobile shows only the one picked in the filter.
const visibleColumns = computed(() =>
  isMobile.value ? COLUMNS.filter((c) => c.key === activeStatus.value) : COLUMNS,
)

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
  // Only orders the current user is allowed to act on can be dragged.
  const order = orders.value.find((o) => o.id === orderId)
  if (!order || !canInteract(order)) {
    e.preventDefault()
    return
  }
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

// Shared by drag-and-drop (desktop) and the per-card action button (mobile).
async function changeStatus(order: ActiveOrder, target: ActiveStatus) {
  if (order.status === target) return
  // Permission gate: managers move anything, waiters only their own claimed orders.
  if (!canInteract(order)) return

  // Optimistic update so the card snaps to the new column instantly.
  const prevStatus = order.status
  const prevReadyAt = order.readyAt
  order.status = target
  order.readyAt = target === 'ready' ? new Date().toISOString() : null

  try {
    const updated = await apiClient.updateOrderStatus(order.id, target)
    // Trust the server timestamp (avoids drift with the grace-period countdown).
    order.readyAt = updated.readyAt ?? order.readyAt
  } catch (err) {
    order.status = prevStatus
    order.readyAt = prevReadyAt
    error.value = err instanceof Error ? err.message : 'Mutarea comenzii a eșuat.'
  }
}

async function cancelOrder(order: ActiveOrder) {
  if (!window.confirm(`Anulezi comanda #${order.orderNumber ?? '—'}? Stocul consumat va fi pus la loc.`)) return

  // Optimistic: drop the card immediately, restore it if the request fails.
  const idx = orders.value.findIndex(o => o.id === order.id)
  const removed = idx !== -1 ? orders.value.splice(idx, 1)[0] : null
  try {
    await apiClient.cancelOrder(order.id)
  } catch (err) {
    if (removed) orders.value.push(removed)
    error.value = err instanceof Error ? err.message : 'Anularea comenzii a eșuat.'
  }
}

// ── „Mută” — move an order (whole or selected items) onto an aux table ──────
// Waiters move their own claimed orders onto their OWN aux table; managers move
// any order and must pick the target waiter. Orders already on an aux table
// (isAuxTable) are handled from the manager console, not from the board.
const moveOrder = ref<ActiveOrder | null>(null) // order being moved; null = modal closed
const moveScope = ref<'whole' | 'items'>('whole')
const moveQuantities = ref<Record<string, number>>({}) // orderItemId → qty to move (items mode)
const moveTargetWaiterId = ref<string | null>(null)
const moveBusy = ref(false)
const waiters = ref<WaiterRow[]>([])
const waitersLoaded = ref(false)
const waitersLoading = ref(false)

function openMove(order: ActiveOrder) {
  moveOrder.value = order
  moveScope.value = 'whole'
  moveTargetWaiterId.value = null
  moveBusy.value = false
  const quantities: Record<string, number> = {}
  for (const item of order.items) quantities[item.id] = 0
  moveQuantities.value = quantities
  // Managers pick the target waiter — load the active list once, lazily.
  if (isManager.value && !waitersLoaded.value && !waitersLoading.value) loadWaiters()
}

function closeMove() {
  moveOrder.value = null
}

async function loadWaiters() {
  try {
    waitersLoading.value = true
    waiters.value = await waiterApi.listByRestaurant(auth.activeRestaurantId!, 'active')
    waitersLoaded.value = true
  } catch (err) {
    toast.error(err instanceof Error ? err.message : 'Nu am putut încărca ospătarii.')
  } finally {
    waitersLoading.value = false
  }
}

function stepMoveQty(itemId: string, delta: number, max: number) {
  const current = moveQuantities.value[itemId] ?? 0
  moveQuantities.value[itemId] = Math.min(max, Math.max(0, current + delta))
}

// Total units picked in "Doar anumite produse" mode — 0 means nothing to move.
const moveSelectedCount = computed(() =>
  Object.values(moveQuantities.value).reduce((sum, qty) => sum + qty, 0),
)

const canSubmitMove = computed(() => {
  if (!moveOrder.value || moveBusy.value) return false
  if (moveScope.value === 'items' && moveSelectedCount.value === 0) return false
  if (isManager.value && !moveTargetWaiterId.value) return false
  return true
})

async function submitMove() {
  const order = moveOrder.value
  if (!order || !canSubmitMove.value) return

  const payload: MoveOrderToAuxPayload = {}
  if (isManager.value) payload.targetWaiterId = moveTargetWaiterId.value

  if (moveScope.value === 'items') {
    // Every item at full quantity = a whole-order move — send it as one (omit items).
    const isWholeOrder = order.items.every(
      (item) => (moveQuantities.value[item.id] ?? 0) === item.quantity,
    )
    if (!isWholeOrder) {
      payload.items = order.items
        .map((item) => ({ orderItemId: item.id, quantity: moveQuantities.value[item.id] ?? 0 }))
        .filter((line) => line.quantity > 0)
    }
  }

  try {
    moveBusy.value = true
    await auxApi.moveOrderToAux(order.id, payload)
    toast.success('Comanda a fost mutată pe masa auxiliară.')
    closeMove()
    fetchActive()
  } catch (err) {
    toast.error(err instanceof Error ? err.message : 'Mutarea pe masa auxiliară a eșuat.')
  } finally {
    moveBusy.value = false
  }
}

async function onDrop(e: DragEvent, target: ActiveStatus) {
  e.preventDefault()
  const orderId = e.dataTransfer?.getData('text/plain') || dragOrderId.value
  dragOrderId.value = null
  dragOverColumn.value = null
  if (!orderId) return

  const order = orders.value.find(o => o.id === orderId)
  if (!order) return
  await changeStatus(order, target)
}

// Advance one step in the workflow from the card's action button.
function advance(order: ActiveOrder) {
  const next = NEXT_STATUS[order.status]
  if (next) changeStatus(order, next)
}

function onMqlChange(e: MediaQueryListEvent) {
  isMobile.value = e.matches
}

onMounted(() => {
  // Keep this breakpoint in sync with the kanban grid media query below.
  mql = window.matchMedia('(max-width: 899px)')
  isMobile.value = mql.matches
  mql.addEventListener('change', onMqlChange)

  fetchActive()
  pollTimer = window.setInterval(fetchActive, 5000)
  tickTimer = window.setInterval(() => (now.value = Date.now()), 1000)
})
onBeforeUnmount(() => {
  mql?.removeEventListener('change', onMqlChange)
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

    <!-- Mobile-only status filter: tap to switch which single column shows. -->
    <div v-if="isMobile" class="mobile-filter" role="tablist" aria-label="Filtrează comenzile după stare">
      <button
        v-for="col in COLUMNS"
        :key="col.key"
        type="button"
        class="filter-tab"
        :class="[`filter-tab--${col.key}`, { 'filter-tab--active': activeStatus === col.key }]"
        role="tab"
        :aria-selected="activeStatus === col.key"
        @click="activeStatus = col.key"
      >
        <span class="filter-tab-label">{{ col.label }}</span>
        <span class="filter-count">{{ byColumn[col.key].length }}</span>
      </button>
    </div>

    <div class="kanban">
      <section
        v-for="col in visibleColumns"
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
              'kanban-card--locked': !canInteract(order),
            }"
            :draggable="canInteract(order)"
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
                <!-- Already parked on a waiter's aux table — the manager console
                     handles it from here, the board just flags it. -->
                <span v-if="order.isAuxTable" class="kc-aux-chip">AUX</span>
              </div>
            </div>

            <ul class="kc-items">
              <li v-for="item in order.items" :key="item.id" class="kc-item">
                <span class="kc-qty">×{{ item.quantity }}</span>
                <span class="kc-name">{{ item.productName }}</span>
              </li>
            </ul>

            <div class="kc-claim">
              <span v-if="order.claimedByWaiterId" class="claim-chip" :title="`Preluat de ${order.claimedByName}`">
                <span aria-hidden="true">✓</span> Preluat · {{ order.claimedByName || 'coleg' }}
              </span>
              <span v-else class="claim-chip claim-chip--open">
                Nepreluată · preia din „Sala”
              </span>
            </div>

            <footer class="kc-bot">
              <span class="text-[12px] font-mono uppercase tracking-[0.05em] text-ink-70">
                {{ formatMoney(order.totalAmount) }} RON
              </span>
              <div class="kc-foot-right">
                <span
                  v-if="secondsLeft(order) !== null"
                  class="countdown"
                  :class="{ 'countdown--urgent': (secondsLeft(order) ?? 99) <= 10 }"
                >
                  arhivare {{ secondsLeft(order) }}s
                </span>
                <button
                  v-if="canInteract(order) && !order.isAuxTable"
                  class="kc-move"
                  type="button"
                  title="Mută comanda pe o masă auxiliară"
                  aria-label="Mută comanda"
                  @click.stop="openMove(order)"
                >Mută</button>
                <button
                  class="kc-cancel"
                  type="button"
                  title="Anulează comanda"
                  aria-label="Anulează comanda"
                  @click.stop="cancelOrder(order)"
                >Anulează</button>
              </div>
            </footer>

            <!-- Per-card status advance (primary control on touch, where drag
                 is impractical). Only claimed orders can move forward. -->
            <button
              v-if="isMobile && canInteract(order) && nextLabel(order.status)"
              type="button"
              class="advance-btn"
              @click="advance(order)"
            >
              <span aria-hidden="true">→</span> {{ nextLabel(order.status) }}
            </button>
          </article>

          <div v-if="byColumn[col.key].length === 0" class="empty-slot">
            Trage o comandă aici
          </div>
        </div>
      </section>
    </div>

    <!-- „Mută” modal — whole order or selected items onto a waiter's aux table.
         Teleported to <body> so it overlays the whole dashboard, clear of the
         shell's stacking context (same pattern as the floor-plan modals). -->
    <Teleport to="body">
      <div v-if="moveOrder" class="mv-backdrop" @pointerdown.self="closeMove">
        <div class="mv-modal card-white" role="dialog" aria-modal="true">
          <div class="mv-head">
            <div>
              <div class="display-title text-2xl">Mută comanda #{{ moveOrder.orderNumber ?? '—' }}</div>
              <div class="mv-sub">De pe masa {{ moveOrder.tableNumber ?? '?' }}</div>
            </div>
            <button type="button" class="icon-btn" aria-label="Închide" @click="closeMove">✕</button>
          </div>

          <!-- Scope: the whole order, or only some items (with quantities). -->
          <div class="mv-field" role="radiogroup" aria-label="Ce se mută">
            <label class="mv-radio" :class="{ 'mv-radio--on': moveScope === 'whole' }">
              <input v-model="moveScope" type="radio" name="mv-scope" value="whole" />
              <span>Toată comanda</span>
            </label>
            <label class="mv-radio" :class="{ 'mv-radio--on': moveScope === 'items' }">
              <input v-model="moveScope" type="radio" name="mv-scope" value="items" />
              <span>Doar anumite produse</span>
            </label>
          </div>

          <div v-if="moveScope === 'items' && moveOrder" class="mv-field">
            <div class="mv-label">Produse de mutat</div>
            <div class="mv-items">
              <div v-for="item in moveOrder.items" :key="item.id" class="mv-item">
                <div class="mv-item-info">
                  <span class="mv-item-name">{{ item.productName }}</span>
                  <span class="mv-item-avail">×{{ item.quantity }}</span>
                </div>
                <!-- Stepper on purpose (no number input — native validation
                     silently blocks submits elsewhere in this codebase). -->
                <div class="mv-stepper">
                  <button
                    type="button"
                    class="mv-step"
                    :disabled="(moveQuantities[item.id] ?? 0) <= 0"
                    aria-label="Mai puțin"
                    @click="stepMoveQty(item.id, -1, item.quantity)"
                  >−</button>
                  <span class="mv-step-val">{{ moveQuantities[item.id] ?? 0 }}</span>
                  <button
                    type="button"
                    class="mv-step"
                    :disabled="(moveQuantities[item.id] ?? 0) >= item.quantity"
                    aria-label="Mai mult"
                    @click="stepMoveQty(item.id, 1, item.quantity)"
                  >+</button>
                </div>
              </div>
            </div>
            <p v-if="moveSelectedCount === 0" class="mv-hint">Alege cel puțin un produs.</p>
          </div>

          <!-- Target: managers pick the waiter; waiters always get their own table. -->
          <div class="mv-field">
            <template v-if="isManager">
              <div class="mv-label">Pe masa auxiliară a:</div>
              <p v-if="waitersLoading" class="mv-hint">Se încarcă ospătarii…</p>
              <template v-else>
                <p v-if="waiters.length === 0" class="mv-hint">
                  Niciun ospătar activ. Aprobă ospătari din pagina „Ospătari”.
                </p>
                <label
                  v-for="w in waiters"
                  :key="w.userId"
                  class="mv-radio"
                  :class="{ 'mv-radio--on': moveTargetWaiterId === w.userId }"
                >
                  <input v-model="moveTargetWaiterId" type="radio" name="mv-waiter" :value="w.userId" />
                  <span>{{ w.fullName }}</span>
                </label>
              </template>
            </template>
            <p v-else class="mv-static">Comanda va fi mutată pe masa ta auxiliară.</p>
          </div>

          <div class="mv-actions">
            <button type="button" class="btn-ghost" @click="closeMove">Renunță</button>
            <button type="button" class="pill-btn-ink" :disabled="!canSubmitMove" @click="submitMove">
              {{ moveBusy ? 'Se mută…' : 'Mută comanda' }}
            </button>
          </div>
        </div>
      </div>
    </Teleport>
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

/* Orders the current user can't act on (unclaimed, or claimed by someone else
   when you're a waiter) are visible but read-only. Managers see no locked cards. */
.kanban-card--locked {
  opacity: 0.5;
  cursor: default;
  border-style: dashed;
}
.kanban-card--locked:hover {
  box-shadow: none;
  border-color: var(--ink12);
}
.kc-claim { margin-top: 10px; }
.claim-chip {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  font-family: 'JetBrains Mono', monospace;
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
/* Order lives on a waiter's aux table — flagged next to the table label. */
.kc-aux-chip {
  display: inline-flex;
  align-items: center;
  margin-top: 4px;
  font-family: 'JetBrains Mono', monospace;
  font-size: 10px;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.08em;
  padding: 2px 8px;
  border-radius: 999px;
  background: var(--accentSoft);
  color: var(--accentDk);
  border: 1px solid var(--accentBorder);
}
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
.kc-foot-right {
  display: flex;
  align-items: center;
  gap: 8px;
}
.kc-cancel {
  font-family: 'JetBrains Mono', monospace;
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
.kc-cancel:hover {
  background: var(--accentSoft);
}
/* Same pill family as .kc-cancel, but neutral ink — a routine action, not a
   destructive one. */
.kc-move {
  font-family: 'JetBrains Mono', monospace;
  font-size: 11px;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  padding: 4px 10px;
  border-radius: 999px;
  background: transparent;
  color: var(--ink);
  border: 1px solid var(--ink12);
  cursor: pointer;
  transition: background 0.15s ease, border-color 0.15s ease;
}
.kc-move:hover {
  background: var(--ink06);
  border-color: var(--ink);
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

/* ── Mobile filter bar — segmented control to pick the single visible column ── */
.mobile-filter {
  display: flex;
  gap: 8px;
  margin-bottom: 16px;
}
.filter-tab {
  flex: 1;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 4px;
  padding: 10px 6px;
  border: 1px solid var(--ink12);
  border-radius: 14px;
  background: var(--paperHi);
  color: var(--ink70);
  cursor: pointer;
  transition: background 0.16s ease, color 0.16s ease, border-color 0.16s ease;
}
.filter-tab-label {
  font-family: 'JetBrains Mono', monospace;
  font-size: 12px;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.03em;
  text-align: center;
  line-height: 1.15;
}
.filter-count {
  font-family: 'JetBrains Mono', monospace;
  font-weight: 700;
  font-size: 12px;
  padding: 2px 9px;
  border-radius: 999px;
  background: rgba(0, 0, 0, 0.1);
  color: inherit;
}
/* Active tab borrows each column's signature color treatment. */
.filter-tab--active.filter-tab--submitted {
  background: var(--lime);
  color: var(--onLime);
  border-color: transparent;
}
.filter-tab--active.filter-tab--preparing {
  background: var(--accentSoft);
  color: var(--accentDk);
  border-color: var(--accentBorder);
}
.filter-tab--active.filter-tab--ready {
  background: var(--ink);
  color: var(--lime);
  border-color: transparent;
}
.filter-tab--active .filter-count {
  background: rgba(0, 0, 0, 0.18);
}
[data-theme="dark"] .filter-tab--active.filter-tab--preparing { color: #FFFFFF; }
[data-theme="dark"] .filter-tab--active.filter-tab--ready { color: var(--accent); }

/* ── Per-card advance button (mobile primary action) ── */
.advance-btn {
  width: 100%;
  margin-top: 12px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  padding: 11px 12px;
  border: none;
  border-radius: 12px;
  background: var(--ink);
  color: var(--lime);
  font-family: 'JetBrains Mono', monospace;
  font-size: 13px;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  cursor: pointer;
  transition: filter 0.16s ease;
}
.advance-btn:active { filter: brightness(1.15); }
[data-theme="dark"] .advance-btn { color: var(--accent); }

/* On phones the board is a single scrollable column, not a tall kanban lane,
   so drop the desktop min-height floor and the grab cursor. */
@media (max-width: 899px) {
  .column { min-height: auto; }
  .kanban-card { cursor: default; }
  .kanban-card:active { cursor: default; }
  .empty-slot { padding: 48px 12px; }
}

/* ── „Mută” modal — teleported over the whole dashboard ── */
.mv-backdrop {
  position: fixed;
  inset: 0;
  z-index: 90;
  background: rgba(0, 0, 0, 0.5);
  -webkit-backdrop-filter: blur(3px);
  backdrop-filter: blur(3px);
  display: grid;
  place-items: center;
  padding: 20px;
}
.mv-modal {
  width: min(92vw, 460px);
  max-height: 86vh;
  overflow-y: auto;
  padding: 22px;
  box-shadow: var(--shadow-pop);
}
.mv-head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 12px;
  margin-bottom: 16px;
}
.mv-sub {
  margin-top: 4px;
  font-family: 'JetBrains Mono', monospace;
  font-size: 12px;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  color: var(--ink70);
}
.mv-field { margin-bottom: 16px; }
.mv-label {
  font-family: 'JetBrains Mono', monospace;
  font-size: 11px;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.06em;
  color: var(--ink70);
  margin-bottom: 8px;
}
.mv-radio {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 9px 12px;
  border: 1px solid var(--ink12);
  border-radius: 12px;
  margin-bottom: 6px;
  cursor: pointer;
  transition: border-color 0.15s ease, background 0.15s ease;
}
.mv-radio:hover { border-color: var(--ink); }
.mv-radio--on {
  border-color: var(--ink);
  background: var(--paperHi);
}
.mv-radio input { accent-color: var(--ink); }
.mv-radio span {
  font-size: 14px;
  font-weight: 500;
  color: var(--ink);
}
.mv-items {
  border: 1px solid var(--ink12);
  border-radius: 14px;
  padding: 2px 12px;
}
.mv-item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  padding: 9px 0;
}
.mv-item + .mv-item { border-top: 1px solid var(--ink06); }
.mv-item-info {
  display: flex;
  align-items: baseline;
  gap: 6px;
  min-width: 0;
}
.mv-item-name {
  font-size: 14px;
  font-weight: 500;
  color: var(--ink);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.mv-item-avail {
  font-family: 'JetBrains Mono', monospace;
  font-size: 12px;
  font-weight: 700;
  color: var(--ink70);
  flex-shrink: 0;
}
.mv-stepper {
  display: inline-flex;
  align-items: center;
  gap: 2px;
  border: 1px solid var(--ink12);
  border-radius: 999px;
  padding: 2px;
  flex-shrink: 0;
}
.mv-step {
  width: 28px;
  height: 28px;
  display: grid;
  place-items: center;
  border: none;
  border-radius: 999px;
  background: transparent;
  color: var(--ink);
  font-size: 16px;
  font-weight: 700;
  line-height: 1;
  cursor: pointer;
  transition: background 0.15s ease;
}
.mv-step:hover:not(:disabled) { background: var(--ink06); }
.mv-step:disabled { opacity: 0.35; cursor: default; }
.mv-step-val {
  min-width: 26px;
  text-align: center;
  font-family: 'JetBrains Mono', monospace;
  font-size: 13px;
  font-weight: 700;
  color: var(--ink);
}
.mv-hint {
  margin: 6px 0 0;
  font-family: 'JetBrains Mono', monospace;
  font-size: 12px;
  color: var(--ink70);
}
.mv-static {
  margin: 0;
  font-size: 14px;
  color: var(--ink);
}
.mv-actions {
  display: flex;
  justify-content: flex-end;
  gap: 10px;
  margin-top: 18px;
}
</style>
