<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import type { WaiterAuxItemDto, WaiterAuxTableResponse } from '@shared/types/waiter'
import { waiterApi } from '@/services/waiterApi'
import { useAuthStore } from '@/stores/useAuthStore'
import { useWaiterRealtimeStore } from '@/stores/useWaiterRealtimeStore'
import { useToast } from '@/composables/useToast'
import { formatMoney } from '@/utils/money'

/**
 * "Masa auxiliară" — the caller's private holding area. Lines land here when
 * pulled off a live round (from the table screen); from here they either move
 * onto one of the caller's claimed tables (joining that bill as an archived
 * round) or get written off. Strictly per-waiter: nobody else sees this list.
 */
const router = useRouter()
const auth = useAuthStore()
const store = useWaiterRealtimeStore()
const toast = useToast()

const aux = ref<WaiterAuxTableResponse | null>(null)
const isLoading = ref(false)
const busyItemId = ref<string | null>(null)
/** The parked line whose "move to table" picker is open. */
const pickerItem = ref<WaiterAuxItemDto | null>(null)

const items = computed(() => aux.value?.items ?? [])
const total = computed(() => aux.value?.total ?? 0)

/** Targets: the caller's own claimed tables (a line can only join your bill). */
const myTables = computed(() => store.myTables)

let fetching = false
async function load() {
  if (fetching) return
  fetching = true
  isLoading.value = true
  try {
    aux.value = await waiterApi.getAuxTable()
  } catch {
    // Keep the last good list; the next refresh reconciles it.
  } finally {
    fetching = false
    isLoading.value = false
  }
}

function timeLabel(createdAt: string): string {
  return new Date(createdAt).toLocaleTimeString('ro-RO', { hour: '2-digit', minute: '2-digit' })
}

function provenance(item: WaiterAuxItemDto): string {
  const parts: string[] = []
  if (item.sourceTableNumber) parts.push(`de la masa ${item.sourceTableNumber}`)
  if (item.sourceOrderNumber) parts.push(`comanda #${item.sourceOrderNumber}`)
  parts.push(timeLabel(item.createdAt))
  return parts.join(' · ')
}

function openPicker(item: WaiterAuxItemDto) {
  pickerItem.value = item
}
function closePicker() {
  if (busyItemId.value) return
  pickerItem.value = null
}

async function moveToTable(tableId: string, tableNumber: string | null) {
  const item = pickerItem.value
  if (!item || busyItemId.value) return
  busyItemId.value = item.id
  try {
    await waiterApi.moveAuxItemToTable(item.id, tableId)
    pickerItem.value = null
    toast.success(
      tableNumber
        ? `„${item.productName}” a intrat pe nota mesei ${tableNumber}.`
        : `„${item.productName}” a intrat pe nota mesei.`,
    )
    void store.refreshFloor()
    void load()
  } catch (err) {
    toast.error(err instanceof Error ? err.message : 'Mutarea pe masă a eșuat.')
    void load()
  } finally {
    busyItemId.value = null
  }
}

async function discard(item: WaiterAuxItemDto) {
  if (busyItemId.value) return
  if (!window.confirm(`Ștergi „${item.productName}” ×${item.quantity} de pe masa auxiliară?`)) return
  busyItemId.value = item.id
  try {
    await waiterApi.discardAuxItem(item.id)
    toast.success('Produsul a fost șters de pe masa auxiliară.')
    void load()
  } catch (err) {
    toast.error(err instanceof Error ? err.message : 'Ștergerea a eșuat.')
  } finally {
    busyItemId.value = null
  }
}

// The floor snapshot is this view's clock too — my-table changes (claims,
// transfers, closes) re-derive the picker options and the list stays fresh.
watch(
  () => store.floor,
  () => void load(),
)

onMounted(async () => {
  const rid = auth.activeRestaurantId
  if (rid) await store.start(rid)
  await load()
})
</script>

<template>
  <div>
    <header class="aux-head">
      <button
        type="button"
        class="icon-btn"
        title="Înapoi"
        aria-label="Înapoi"
        @click="router.back()"
      >
        <span aria-hidden="true">←</span>
      </button>
      <div class="min-w-0">
        <div class="eyebrow">A ta, separată</div>
        <h1 class="aux-title">Masa auxiliară</h1>
      </div>
      <button
        type="button"
        class="icon-btn"
        title="Reîncarcă"
        aria-label="Reîncarcă"
        :disabled="isLoading"
        @click="load"
      >
        <span :class="{ 'animate-spin': isLoading }" aria-hidden="true">↻</span>
      </button>
    </header>

    <p class="aux-hint">
      Produse scoase de pe note — probleme, mese schimbate. Le poți muta pe una
      dintre mesele tale sau le poți șterge.
    </p>

    <section class="aux-total card-white">
      <span class="eyebrow">Total parcat</span>
      <span class="aux-total-amount">{{ formatMoney(total) }} <span>RON</span></span>
    </section>

    <div v-if="!items.length" class="aux-empty">
      Nimic pe masa auxiliară.
    </div>

    <ul v-else class="aux-list">
      <li v-for="item in items" :key="item.id" class="card-white aux-item">
        <div class="ai-top">
          <span class="ai-qty">×{{ item.quantity }}</span>
          <span class="ai-body">
            <span class="ai-name">{{ item.productName }}</span>
            <span v-if="item.notes" class="ai-notes">Obs: {{ item.notes }}</span>
            <span class="ai-src">{{ provenance(item) }}</span>
          </span>
          <span class="ai-price">{{ formatMoney(item.quantity * item.unitPriceAtPurchase) }}</span>
        </div>
        <div class="ai-actions">
          <button
            type="button"
            class="pill-btn ai-move"
            :disabled="busyItemId !== null"
            @click="openPicker(item)"
          >
            Mută pe masă
          </button>
          <button
            type="button"
            class="btn-danger"
            :disabled="busyItemId !== null"
            @click="discard(item)"
          >
            Șterge
          </button>
        </div>
      </li>
    </ul>

    <!-- Target picker: one of the caller's claimed tables. -->
    <div v-if="pickerItem" class="sheet-backdrop" @click="closePicker">
      <div
        class="sheet"
        role="dialog"
        aria-modal="true"
        aria-label="Mută pe masă"
        @click.stop
      >
        <div class="sheet-grip" aria-hidden="true"></div>
        <div class="eyebrow">Mută pe masă</div>
        <h2 class="sheet-title">„{{ pickerItem.productName }}” ×{{ pickerItem.quantity }}</h2>

        <p v-if="!myTables.length" class="sheet-note">
          Nu ai nicio masă preluată. Preia întâi masa (din „Mese” sau „Nepreluate”),
          apoi mută produsul pe ea.
        </p>
        <ul v-else class="target-list">
          <li v-for="table in myTables" :key="table.id">
            <button
              type="button"
              class="target-row"
              :disabled="busyItemId !== null"
              @click="moveToTable(table.id, table.tableNumber)"
            >
              <span class="target-num">Masa {{ table.tableNumber ?? '—' }}</span>
              <span class="target-total">
                {{ formatMoney(table.session?.cumulativeTotal) }} RON
              </span>
              <span class="target-arrow" aria-hidden="true">→</span>
            </button>
          </li>
        </ul>

        <div class="sheet-actions">
          <button type="button" class="btn-ghost" :disabled="busyItemId !== null" @click="closePicker">
            Renunță
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.aux-head {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-bottom: 8px;
}
.aux-title {
  font-size: 22px;
  font-weight: 800;
  line-height: 1.15;
  color: var(--ink);
}
.aux-head .min-w-0 { flex: 1; }
.aux-hint {
  font-size: 13px;
  color: var(--ink70);
  margin-bottom: 14px;
}

.aux-total {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  padding: 14px;
  border: 1px solid var(--ink12);
  margin-bottom: 14px;
}
.aux-total-amount {
  font-size: 26px;
  font-weight: 800;
  color: var(--ink);
}
.aux-total-amount span {
  font-size: 13px;
  font-weight: 700;
  color: var(--ink40);
}

.aux-empty {
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

.aux-list {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 10px;
}
.aux-item {
  padding: 12px;
  border: 1px solid var(--ink12);
}
.ai-top {
  display: flex;
  gap: 8px;
  align-items: baseline;
}
.ai-qty {
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-weight: 700;
  font-size: 13px;
  color: var(--ink70);
  min-width: 28px;
}
.ai-body {
  display: flex;
  flex-direction: column;
  min-width: 0;
  flex: 1;
}
.ai-name {
  font-size: 15px;
  font-weight: 600;
  color: var(--ink);
}
.ai-notes {
  font-size: 12px;
  font-style: italic;
  color: var(--ink40);
  line-height: 1.3;
}
.ai-src {
  margin-top: 3px;
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 10.5px;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  color: var(--ink40);
}
.ai-price {
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 12.5px;
  font-weight: 700;
  color: var(--ink70);
  flex-shrink: 0;
}
.ai-actions {
  display: flex;
  gap: 8px;
  margin-top: 12px;
}
.ai-actions > * {
  flex: 1;
  min-height: var(--tap-min);
}

/* ── Target picker sheet ─────────────────────────────────────────────────── */
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
  font-size: 20px;
  font-weight: 800;
  line-height: 1.2;
  color: var(--ink);
  margin-bottom: 12px;
}
.sheet-note {
  font-size: 12.5px;
  color: var(--accentDk);
  background: var(--accentSoft);
  border: 1px solid var(--accentBorder);
  border-radius: 14px;
  padding: 9px 12px;
  margin-bottom: 14px;
}
.target-list {
  list-style: none;
  margin: 0 0 14px;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 8px;
  max-height: 45vh;
  overflow-y: auto;
}
.target-row {
  width: 100%;
  display: flex;
  align-items: center;
  gap: 10px;
  min-height: var(--tap-min);
  padding: 10px 14px;
  border-radius: 14px;
  border: 1px solid var(--ink12);
  background: var(--card);
  cursor: pointer;
  transition: border-color 0.15s ease, background 0.15s ease;
}
.target-row:active { background: var(--ink06); }
.target-num {
  font-size: 16px;
  font-weight: 700;
  color: var(--ink);
  flex: 1;
  text-align: left;
}
.target-total {
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 11px;
  font-weight: 700;
  padding: 3px 8px;
  border-radius: 999px;
  background: var(--lime);
  color: var(--onLime);
}
.target-arrow {
  color: var(--ink40);
  font-family: 'JetBrains Mono', ui-monospace, monospace;
}
.sheet-actions {
  display: flex;
  gap: 10px;
}
.sheet-actions > * {
  flex: 1;
  min-height: var(--tap-min);
}
</style>
