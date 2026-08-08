<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { RouterLink } from 'vue-router'
import { apiClient, auxApi, tablesApi, type TableSummary } from '@/services/api-client'
import type { AuxOrder, AuxTableRow, OrderStatus } from '@shared/types/models'
import { resolveMediaUrl } from '@/services/mediaUrl'
import { useToast } from '@/composables/useToast'
import { formatMoney } from '@/utils/money'

const toast = useToast()

const rows = ref<AuxTableRow[]>([])
const tables = ref<TableSummary[]>([])
const isLoading = ref(false)
// Set once the first fetch lands, so the page-level empty state doesn't flash
// during the 5s polling refreshes.
const hasLoaded = ref(false)
const error = ref<string | null>(null)
const busyOrderId = ref<string | null>(null)

// One shared action modal, reused for both "move onto a real table" and
// "reassign to another waiter's aux table".
type ModalMode = 'table' | 'waiter'
const modalMode = ref<ModalMode | null>(null)
const modalOrder = ref<AuxOrder | null>(null)
const modalSourceWaiterId = ref<string | null>(null)
const selectedTargetId = ref<string | null>(null)

let pollTimer: number | null = null

// RO labels + chip colours mirror the kanban columns on the orders board.
const STATUS_LABELS: Partial<Record<OrderStatus, string>> = {
  submitted: 'Comandă',
  preparing: 'În pregătire',
  ready: 'Finalizată',
}
function statusLabel(status: OrderStatus): string {
  return STATUS_LABELS[status] ?? status
}

const totalParked = computed(() => rows.value.reduce((n, r) => n + r.orders.length, 0))

async function fetchAll() {
  try {
    isLoading.value = true
    error.value = null
    const [auxRows, tableList] = await Promise.all([auxApi.list(), tablesApi.list()])
    rows.value = auxRows
    tables.value = tableList
    hasLoaded.value = true
  } catch (err) {
    error.value = err instanceof Error ? err.message : 'Eroare la încărcarea meselor auxiliare.'
  } finally {
    isLoading.value = false
  }
}

function initialOf(name: string): string {
  const trimmed = name.trim()
  return trimmed ? trimmed.charAt(0).toUpperCase() : '⊕'
}

function formatHm(iso: string | null): string {
  if (!iso) return '—'
  const date = new Date(iso)
  if (Number.isNaN(date.getTime())) return '—'
  return date.toLocaleTimeString('ro-RO', { hour: '2-digit', minute: '2-digit' })
}

function openMoveModal(order: AuxOrder) {
  modalMode.value = 'table'
  modalOrder.value = order
  modalSourceWaiterId.value = null
  selectedTargetId.value = null
}

function openReassignModal(row: AuxTableRow, order: AuxOrder) {
  modalMode.value = 'waiter'
  modalOrder.value = order
  modalSourceWaiterId.value = row.waiterId
  selectedTargetId.value = null
}

function closeModal() {
  modalMode.value = null
  modalOrder.value = null
  modalSourceWaiterId.value = null
  selectedTargetId.value = null
}

const modalTitle = computed(() => {
  const order = modalOrder.value
  if (!order) return ''
  const n = order.orderNumber ?? '—'
  return modalMode.value === 'table'
    ? `Mută comanda #${n} pe o masă`
    : `Reatribuie comanda #${n}`
})

// Reassign candidates: every OTHER waiter row still marked as active staff.
const waiterTargets = computed<AuxTableRow[]>(() =>
  rows.value.filter((r) => r.isActiveStaff && r.waiterId !== modalSourceWaiterId.value),
)

async function confirmModal() {
  const order = modalOrder.value
  const mode = modalMode.value
  const targetId = selectedTargetId.value
  if (!order || !mode || !targetId || busyOrderId.value !== null) return

  busyOrderId.value = order.id
  try {
    if (mode === 'table') {
      await auxApi.moveToTable(order.id, targetId)
      const target = tables.value.find((t) => t.id === targetId)
      toast.success(`Comanda a fost mutată pe masa ${target?.tableNumber ?? '—'}.`)
    } else {
      await auxApi.reassign(order.id, targetId)
      toast.success('Comanda a fost reatribuită.')
    }
    closeModal()
    await fetchAll()
  } catch (err) {
    toast.error(err instanceof Error ? err.message : 'Acțiunea a eșuat.')
  } finally {
    busyOrderId.value = null
  }
}

async function cancelOrder(order: AuxOrder) {
  if (busyOrderId.value !== null) return
  if (!window.confirm(`Anulezi comanda #${order.orderNumber ?? '—'}? Stocul consumat va fi pus la loc.`)) return

  busyOrderId.value = order.id
  try {
    await apiClient.cancelOrder(order.id)
    toast.success('Comanda a fost anulată.')
    await fetchAll()
  } catch (err) {
    toast.error(err instanceof Error ? err.message : 'Anularea comenzii a eșuat.')
  } finally {
    busyOrderId.value = null
  }
}

onMounted(() => {
  fetchAll()
  pollTimer = window.setInterval(fetchAll, 5000)
})
onBeforeUnmount(() => {
  if (pollTimer !== null) window.clearInterval(pollTimer)
})
</script>

<template>
  <div class="max-w-7xl mx-auto relative">
    <div class="flex items-center justify-between mb-6 flex-wrap gap-3">
      <div class="tag-pill" role="status">
        <span class="live-dot" aria-hidden="true"></span>
        <span>{{ totalParked }} {{ totalParked === 1 ? 'comandă parcată' : 'comenzi parcate' }}</span>
      </div>
      <button class="pill-btn-ink" :disabled="isLoading" @click="fetchAll">
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

    <!-- No active waiters yet → point the manager at the approvals page. -->
    <div v-if="hasLoaded && rows.length === 0" class="page-empty card-paper">
      <p class="page-empty-text">Niciun ospătar activ încă. Aprobă ospătari din pagina „Ospătari”.</p>
      <RouterLink to="/waiters" class="btn-ghost">Deschide pagina „Ospătari” →</RouterLink>
    </div>

    <div v-else class="aux-grid">
      <article v-for="row in rows" :key="row.waiterId" class="card-paper waiter-card">
        <header class="wc-head">
          <div class="wc-avatar">
            <img v-if="row.avatarUrl" :src="resolveMediaUrl(row.avatarUrl)" alt="" />
            <span v-else>{{ initialOf(row.waiterName) }}</span>
          </div>
          <div class="wc-meta">
            <div class="wc-name" :title="row.waiterName">{{ row.waiterName }}</div>
            <span v-if="!row.isActiveStaff" class="wc-inactive">Cont inactiv</span>
          </div>
          <span class="count-pill">{{ row.orders.length }}</span>
        </header>

        <div v-if="row.orders.length === 0" class="wc-empty">
          Nicio comandă pe masa auxiliară.
        </div>

        <div v-else class="wc-orders">
          <article v-for="order in row.orders" :key="order.id" class="aux-order">
            <div class="ao-top">
              <span class="font-display text-base font-bold leading-none">#{{ order.orderNumber ?? '—' }}</span>
              <span class="status-chip" :class="`status-chip--${order.status}`">{{ statusLabel(order.status) }}</span>
            </div>

            <div class="ao-prov">
              De la masa {{ order.movedFromTableNumber ?? '?' }} · {{ formatHm(order.movedToAuxAt) }}<template v-if="order.claimedByName"> · {{ order.claimedByName }}</template>
            </div>

            <ul class="ao-items">
              <li v-for="item in order.items" :key="item.id" class="ao-item">
                <span class="ao-qty">×{{ item.quantity }}</span>
                <span class="ao-name">{{ item.productName }}</span>
              </li>
            </ul>

            <footer class="ao-bot">
              <span class="text-[12px] font-mono uppercase tracking-[0.05em] text-ink-70">
                {{ formatMoney(order.totalAmount) }} RON
              </span>
              <div class="ao-actions">
                <button
                  type="button"
                  class="ao-btn"
                  :disabled="busyOrderId !== null"
                  @click="openMoveModal(order)"
                >Pe masă…</button>
                <button
                  type="button"
                  class="ao-btn"
                  :disabled="busyOrderId !== null"
                  @click="openReassignModal(row, order)"
                >Reatribuie…</button>
                <button
                  type="button"
                  class="ao-btn ao-btn--danger"
                  :disabled="busyOrderId !== null"
                  @click="cancelOrder(order)"
                >Anulează</button>
              </div>
            </footer>
          </article>
        </div>
      </article>
    </div>

    <!-- Action modal (move to table / reassign) — teleported over the shell like
         the floor-plan dialogs so no ancestor can clip it. -->
    <Teleport to="body">
      <div
        v-if="modalOrder && modalMode"
        class="aux-modal-backdrop"
        @pointerdown.self="closeModal"
      >
        <div class="aux-modal card-white" role="dialog" aria-modal="true">
          <div class="aux-modal-head">
            <div>
              <div class="eyebrow">Masă auxiliară</div>
              <div class="display-title text-2xl">{{ modalTitle }}</div>
            </div>
            <button type="button" class="icon-btn" aria-label="Închide" @click="closeModal">✕</button>
          </div>

          <template v-if="modalMode === 'table'">
            <div v-if="tables.length === 0" class="aux-modal-empty">
              Nu există mese. Creează mese din pagina „Coduri QR”.
            </div>
            <div v-else class="option-list" role="radiogroup" aria-label="Alege masa">
              <label
                v-for="t in tables"
                :key="t.id"
                class="option-row"
                :class="{ 'option-row--active': selectedTargetId === t.id }"
              >
                <input
                  v-model="selectedTargetId"
                  type="radio"
                  name="aux-target"
                  class="option-radio"
                  :value="t.id"
                />
                <span class="option-label">Masa {{ t.tableNumber ?? '—' }}</span>
              </label>
            </div>
          </template>

          <template v-else>
            <div v-if="waiterTargets.length === 0" class="aux-modal-empty">
              Nu există alt ospătar activ.
            </div>
            <div v-else class="option-list" role="radiogroup" aria-label="Alege ospătarul">
              <label
                v-for="w in waiterTargets"
                :key="w.waiterId"
                class="option-row"
                :class="{ 'option-row--active': selectedTargetId === w.waiterId }"
              >
                <input
                  v-model="selectedTargetId"
                  type="radio"
                  name="aux-target"
                  class="option-radio"
                  :value="w.waiterId"
                />
                <span class="option-label">{{ w.waiterName }}</span>
              </label>
            </div>
          </template>

          <div class="aux-modal-actions">
            <button type="button" class="btn-ghost" @click="closeModal">Renunță</button>
            <button
              type="button"
              class="pill-btn-ink"
              :disabled="!selectedTargetId || busyOrderId !== null"
              @click="confirmModal"
            >
              {{ busyOrderId !== null ? 'Se salvează…' : 'Confirmă' }}
            </button>
          </div>
        </div>
      </div>
    </Teleport>
  </div>
</template>

<style scoped>
.aux-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(340px, 1fr));
  gap: 16px;
  align-items: start;
}

/* ── Waiter card ─────────────────────────────────────────────────────────── */
.waiter-card {
  padding: 16px;
  display: flex;
  flex-direction: column;
  gap: 12px;
}
.wc-head {
  display: flex;
  align-items: center;
  gap: 12px;
}
.wc-avatar {
  width: 36px;
  height: 36px;
  border-radius: 50%;
  background: var(--ink);
  color: var(--paperHi);
  display: grid;
  place-items: center;
  font-weight: 700;
  font-size: 14px;
  overflow: hidden;
  flex-shrink: 0;
}
.wc-avatar img {
  width: 100%;
  height: 100%;
  object-fit: cover;
}
.wc-meta {
  flex: 1;
  min-width: 0;
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}
.wc-name {
  font-weight: 700;
  font-size: 16px;
  color: var(--ink);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.wc-inactive {
  font-family: 'JetBrains Mono', monospace;
  font-size: 10px;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  padding: 3px 8px;
  border-radius: 999px;
  background: var(--accentSoft);
  color: var(--accentDk);
  border: 1px solid var(--accentBorder);
  flex-shrink: 0;
}
.count-pill {
  background: var(--ink06);
  color: var(--ink);
  font-family: 'JetBrains Mono', monospace;
  font-weight: 700;
  font-size: 12px;
  padding: 5px 11px;
  border-radius: 999px;
  border: 1px solid var(--ink12);
  flex-shrink: 0;
}
.wc-empty {
  display: grid;
  place-items: center;
  text-align: center;
  padding: 26px 12px;
  font-family: 'JetBrains Mono', monospace;
  font-size: 12.5px;
  color: var(--ink40);
  text-transform: uppercase;
  letter-spacing: 0.05em;
  border: 1px dashed var(--ink12);
  border-radius: 14px;
}
.wc-orders {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

/* ── Parked-order sub-card ───────────────────────────────────────────────── */
.aux-order {
  background: var(--card, var(--paperHi));
  border: 1px solid var(--ink12);
  border-radius: 14px;
  padding: 12px;
}
.ao-top {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
}
/* Same treatments as the kanban column heads: lime / accentSoft / ink+lime. */
.status-chip {
  font-family: 'JetBrains Mono', monospace;
  font-size: 11px;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  padding: 4px 10px;
  border-radius: 999px;
  border: 1px solid transparent;
  background: var(--ink06);
  color: var(--ink);
  flex-shrink: 0;
}
.status-chip--submitted {
  background: var(--lime);
  color: var(--onLime);
}
.status-chip--preparing {
  background: var(--accentSoft);
  color: var(--accentDk);
  border-color: var(--accentBorder);
}
.status-chip--ready {
  background: var(--ink);
  color: var(--lime);
}
[data-theme="dark"] .status-chip--preparing { color: #ffffff; }
[data-theme="dark"] .status-chip--ready { color: var(--accent); }

.ao-prov {
  margin-top: 6px;
  font-family: 'JetBrains Mono', monospace;
  font-size: 11.5px;
  color: var(--ink70);
}
.ao-items {
  margin: 10px 0 0;
  padding: 0;
  list-style: none;
  display: flex;
  flex-direction: column;
  gap: 4px;
}
.ao-item {
  display: flex;
  gap: 8px;
  align-items: baseline;
  font-size: 14px;
  color: var(--ink);
}
.ao-qty {
  font-family: 'JetBrains Mono', monospace;
  font-weight: 700;
  font-size: 12.5px;
  color: var(--ink70);
  min-width: 26px;
}
.ao-name { font-weight: 500; }
.ao-bot {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
  margin-top: 12px;
  padding-top: 10px;
  border-top: 1px solid var(--ink06);
}
.ao-actions {
  display: flex;
  align-items: center;
  gap: 6px;
  flex-wrap: wrap;
}
.ao-btn {
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
.ao-btn:hover:not(:disabled) {
  background: var(--ink06);
  border-color: var(--ink);
}
.ao-btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}
.ao-btn--danger {
  color: var(--accent);
  border-color: var(--accentBorder);
}
.ao-btn--danger:hover:not(:disabled) {
  background: var(--accentSoft);
  border-color: var(--accent);
}

/* ── Page-level empty state ──────────────────────────────────────────────── */
.page-empty {
  display: grid;
  place-items: center;
  gap: 14px;
  text-align: center;
  padding: 44px 24px;
}
.page-empty-text {
  margin: 0;
  font-family: 'JetBrains Mono', monospace;
  font-size: 13px;
  color: var(--ink70);
  text-transform: uppercase;
  letter-spacing: 0.05em;
}

/* ── Action modal (Teleport to body, floor-plan dialog language) ─────────── */
.aux-modal-backdrop {
  position: fixed;
  inset: 0;
  z-index: 80;
  background: rgba(0, 0, 0, 0.5);
  -webkit-backdrop-filter: blur(3px);
  backdrop-filter: blur(3px);
  display: grid;
  place-items: center;
  padding: 20px;
}
.aux-modal {
  width: min(92vw, 460px);
  max-height: 86vh;
  overflow-y: auto;
  padding: 22px;
  box-shadow: var(--shadow-pop);
}
.aux-modal-head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 12px;
  margin-bottom: 16px;
}
.aux-modal-empty {
  padding: 20px;
  text-align: center;
  font-family: 'JetBrains Mono', monospace;
  font-size: 13px;
  color: var(--ink40);
  border: 1px dashed var(--ink12);
  border-radius: 14px;
}
.option-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
  max-height: 44vh;
  overflow-y: auto;
  padding-right: 2px;
}
.option-row {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 10px 14px;
  border: 1px solid var(--ink12);
  border-radius: 12px;
  background: var(--card, var(--paperHi));
  cursor: pointer;
  transition: background 0.15s ease, border-color 0.15s ease;
}
.option-row:hover {
  background: var(--ink06);
  border-color: var(--ink);
}
.option-row--active {
  background: color-mix(in srgb, var(--lime) 22%, var(--paperHi));
  border-color: var(--ink);
}
.option-radio {
  width: 16px;
  height: 16px;
  accent-color: var(--ink);
  flex-shrink: 0;
}
.option-label {
  font-weight: 600;
  font-size: 14.5px;
  color: var(--ink);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.aux-modal-actions {
  display: flex;
  justify-content: flex-end;
  gap: 10px;
  margin-top: 18px;
}
</style>
