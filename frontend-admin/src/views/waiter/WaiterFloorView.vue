<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import type { WaiterFloorTableDto } from '@shared/types/waiter'
import { waiterApi } from '@/services/waiterApi'
import { useAuthStore } from '@/stores/useAuthStore'
import { useWaiterRealtimeStore } from '@/stores/useWaiterRealtimeStore'
import { useToast } from '@/composables/useToast'
import { formatMoney } from '@/utils/money'

/**
 * The waiter's home screen: every table in the location with its live claim
 * state. Data comes exclusively from `useWaiterRealtimeStore` (events are
 * triggers, REST is truth) — this view never talks to SignalR itself and only
 * calls the API to *change* something (claim).
 */
const route = useRoute()
const router = useRouter()
const auth = useAuthStore()
const store = useWaiterRealtimeStore()
const toast = useToast()

type TableState = 'free' | 'mine' | 'other'

const selectedZoneId = ref<string | null>(null)
const isRefreshing = ref(false)
const isClaiming = ref(false)
/** The table whose bottom sheet is open (claim confirm, or owner info). */
const sheetTable = ref<WaiterFloorTableDto | null>(null)

// Gating place ③ — force-release is a manager path, so the occupied-table sheet
// only points at it for someone who can actually use it.
const isManager = computed(() => auth.canManageWaitersOfActive)

// The bottom bar's "Mesele mele" tab is this same screen, filtered by a query
// flag — one floor view, two tabs.
const mineOnly = computed(() => route.query.filtru === 'ale-mele')

const zones = computed(() => store.zones)

const visibleTables = computed<WaiterFloorTableDto[]>(() => {
  const source = mineOnly.value ? store.myTables : store.tables
  const inZone = selectedZoneId.value
    ? source.filter((t) => t.zoneId === selectedZoneId.value)
    : source
  // Table numbers are free-form strings ("12", "Terasa 3") — numeric-aware sort
  // keeps 2 before 10 without assuming they parse as integers.
  return [...inZone].sort((a, b) =>
    (a.tableNumber ?? '').localeCompare(b.tableNumber ?? '', 'ro', { numeric: true }),
  )
})

function stateOf(table: WaiterFloorTableDto): TableState {
  if (!table.session) return 'free'
  return table.session.waiterUserId === auth.user?.id ? 'mine' : 'other'
}

function stateLabel(table: WaiterFloorTableDto): string {
  const state = stateOf(table)
  if (state === 'free') return 'Liberă'
  if (state === 'mine') return 'Masa mea'
  return `Ocupată — ${table.session?.waiterName ?? 'coleg'}`
}

function stateClass(table: WaiterFloorTableDto) {
  return {
    'tc-free': stateOf(table) === 'free',
    'tc-mine': stateOf(table) === 'mine',
    'tc-other': stateOf(table) === 'other',
    'tc-help': table.hasOpenHelpRequest,
  }
}

function openTable(table: WaiterFloorTableDto) {
  router.push({ name: 'waiter-table', params: { tableId: table.id } })
}

function onTapTable(table: WaiterFloorTableDto) {
  if (stateOf(table) === 'mine') {
    openTable(table)
    return
  }
  sheetTable.value = table
}

function closeSheet() {
  if (isClaiming.value) return
  sheetTable.value = null
}

async function confirmClaim() {
  const table = sheetTable.value
  if (!table || isClaiming.value) return
  isClaiming.value = true
  try {
    const session = await waiterApi.claimTable(table.id)
    sheetTable.value = null
    toast.success(table.tableNumber ? `Ai preluat masa ${table.tableNumber}.` : 'Ai preluat masa.')
    void store.refreshFloor()
    router.push({ name: 'waiter-table', params: { tableId: session.tableId } })
  } catch (err) {
    // 409: the API's Romanian message already names the waiter who holds it.
    toast.error(err instanceof Error ? err.message : 'Masa nu a putut fi preluată.')
    sheetTable.value = null
    await store.refreshFloor()
  } finally {
    isClaiming.value = false
  }
}

async function refresh() {
  isRefreshing.value = true
  try {
    await store.refreshFloor()
  } finally {
    isRefreshing.value = false
  }
}

// App.vue owns the store lifecycle; this is the deep-link / hard-reload safety
// net (`start` is idempotent for the restaurant it is already connected to).
async function load() {
  const rid = auth.activeRestaurantId
  if (!rid) return
  await store.start(rid)
  await store.refreshFloor()
}
onMounted(load)
</script>

<template>
  <div>
    <header class="floor-head">
      <div class="min-w-0">
        <div class="eyebrow">{{ mineOnly ? 'Mesele mele' : 'Sala' }}</div>
        <h1 class="floor-title">{{ mineOnly ? 'Mesele deschise de mine' : 'Toate mesele' }}</h1>
      </div>
      <button
        type="button"
        class="icon-btn"
        title="Reîncarcă"
        aria-label="Reîncarcă"
        :disabled="isRefreshing"
        @click="refresh"
      >
        <span :class="{ 'animate-spin': isRefreshing }" aria-hidden="true">↻</span>
      </button>
    </header>

    <div v-if="zones.length" class="zone-chips" role="tablist" aria-label="Filtrează după zonă">
      <button
        type="button"
        class="chip"
        :class="{ 'chip-active': selectedZoneId === null }"
        role="tab"
        :aria-selected="selectedZoneId === null"
        @click="selectedZoneId = null"
      >
        Toate
      </button>
      <button
        v-for="zone in zones"
        :key="zone.id"
        type="button"
        class="chip"
        :class="{ 'chip-active': selectedZoneId === zone.id }"
        role="tab"
        :aria-selected="selectedZoneId === zone.id"
        @click="selectedZoneId = zone.id"
      >
        {{ zone.name }}
      </button>
    </div>

    <p v-if="!visibleTables.length" class="floor-empty">
      {{ mineOnly ? 'Nu ai nicio masă deschisă.' : 'Nicio masă în această zonă.' }}
    </p>

    <div v-else class="table-grid">
      <button
        v-for="table in visibleTables"
        :key="table.id"
        type="button"
        class="card-white table-card"
        :class="stateClass(table)"
        @click="onTapTable(table)"
      >
        <span class="tc-head">
          <span class="tc-num">{{ table.tableNumber ?? '—' }}</span>
          <span v-if="table.hasOpenHelpRequest" class="tc-help-badge">Cere ajutor</span>
        </span>

        <span class="tc-state">{{ stateLabel(table) }}</span>

        <span class="tc-foot">
          <span v-if="table.activeOrderCount" class="tc-orders">
            {{ table.activeOrderCount }}
            {{ table.activeOrderCount === 1 ? 'comandă' : 'comenzi' }}
          </span>
          <span v-if="table.session" class="tc-total">
            {{ formatMoney(table.session.cumulativeTotal) }} RON
          </span>
        </span>
      </button>
    </div>

    <!-- Bottom sheet: claim confirmation for a free table, owner info otherwise. -->
    <div v-if="sheetTable" class="sheet-backdrop" @click="closeSheet">
      <div
        class="sheet"
        role="dialog"
        aria-modal="true"
        :aria-label="`Masa ${sheetTable.tableNumber ?? ''}`"
        @click.stop
      >
        <div class="sheet-grip" aria-hidden="true"></div>

        <template v-if="stateOf(sheetTable) === 'free'">
          <div class="eyebrow">Masă liberă</div>
          <h2 class="sheet-title">Preiei masa {{ sheetTable.tableNumber ?? '—' }}?</h2>
          <p class="sheet-text">
            Masa îți va fi atribuită până la închiderea notei. Comenzile trimise de la codul QR
            al mesei intră automat în aceeași sesiune.
          </p>
          <div class="sheet-actions">
            <button type="button" class="btn-ghost" :disabled="isClaiming" @click="closeSheet">
              Renunță
            </button>
            <button type="button" class="pill-btn" :disabled="isClaiming" @click="confirmClaim">
              {{ isClaiming ? 'Se preia…' : 'Preia masa' }}
            </button>
          </div>
        </template>

        <template v-else>
          <div class="eyebrow">Masă ocupată</div>
          <h2 class="sheet-title">Masa {{ sheetTable.tableNumber ?? '—' }}</h2>
          <p class="sheet-text">
            Deschisă de <strong>{{ sheetTable.session?.waiterName }}</strong>.
            Total curent: {{ formatMoney(sheetTable.session?.cumulativeTotal) }} RON.
          </p>
          <p v-if="isManager" class="sheet-note">
            Ca manager poți elibera forțat masa din ecranul mesei — acțiunea este
            înregistrată în jurnalul de audit.
          </p>
          <div class="sheet-actions">
            <button type="button" class="btn-ghost" @click="closeSheet">Închide</button>
            <button
              v-if="isManager"
              type="button"
              class="pill-btn"
              @click="openTable(sheetTable)"
            >
              Preia (admin)
            </button>
          </div>
        </template>
      </div>
    </div>
  </div>
</template>

<style scoped>
.floor-head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 12px;
  margin-bottom: 14px;
}
.floor-title {
  font-size: 22px;
  font-weight: 800;
  line-height: 1.15;
  color: var(--ink);
}

/* ── Zone filter ─────────────────────────────────────────────────────────── */
.zone-chips {
  display: flex;
  gap: 8px;
  overflow-x: auto;
  padding-bottom: 4px;
  margin-bottom: 14px;
  scrollbar-width: none;
}
.zone-chips::-webkit-scrollbar { display: none; }
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

.floor-empty {
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

/* ── Table grid ──────────────────────────────────────────────────────────── */
.table-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 10px;
}
@media (min-width: 380px) {
  .table-grid { grid-template-columns: repeat(3, minmax(0, 1fr)); }
}
.table-card {
  min-height: calc(var(--tap-min) * 2);
  display: flex;
  flex-direction: column;
  gap: 6px;
  padding: 12px 12px 11px;
  text-align: left;
  cursor: pointer;
  border: 1px solid var(--ink12);
  transition: transform 0.12s ease, border-color 0.16s ease, background 0.16s ease;
}
.table-card:active { transform: scale(0.97); }
.tc-head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 6px;
}
.tc-num {
  font-size: 26px;
  font-weight: 800;
  line-height: 1;
  color: var(--ink);
}
.tc-state {
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 10.5px;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  color: var(--ink70);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.tc-foot {
  margin-top: auto;
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 6px;
}
.tc-orders,
.tc-total {
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 10.5px;
  font-weight: 700;
  padding: 3px 8px;
  border-radius: 999px;
  background: var(--ink06);
  color: var(--ink70);
}
.tc-total {
  background: var(--lime);
  color: var(--onLime);
}

/* Four states — free, mine, someone else's, and "needs help" on top of any. */
.tc-free {
  border-color: var(--accentBorder);
  border-style: dashed;
}
.tc-mine {
  background: var(--accent);
  border-color: var(--accent);
}
.tc-mine .tc-num,
.tc-mine .tc-state { color: var(--onAccent); }
.tc-mine .tc-orders {
  background: rgba(0, 0, 0, 0.16);
  color: var(--onAccent);
}
.tc-other {
  opacity: 0.62;
  border-style: solid;
}
.tc-help {
  border-color: var(--accent);
  box-shadow: 0 0 0 3px color-mix(in srgb, var(--accent) 24%, transparent);
  animation: tc-help-pulse 1.6s ease-in-out infinite;
}
.tc-help.tc-other { opacity: 1; }
.tc-help-badge {
  flex-shrink: 0;
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 9px;
  font-weight: 800;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  padding: 3px 7px;
  border-radius: 999px;
  background: var(--accent);
  color: var(--onAccent);
  line-height: 1.2;
}
@keyframes tc-help-pulse {
  0%, 100% { box-shadow: 0 0 0 3px color-mix(in srgb, var(--accent) 24%, transparent); }
  50%      { box-shadow: 0 0 0 7px color-mix(in srgb, var(--accent) 6%, transparent); }
}
@media (prefers-reduced-motion: reduce) {
  .tc-help { animation: none; }
}

/* ── Bottom sheet ────────────────────────────────────────────────────────── */
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
  margin-bottom: 8px;
}
.sheet-text {
  font-size: 14px;
  color: var(--ink70);
  margin-bottom: 14px;
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
.sheet-actions {
  display: flex;
  gap: 10px;
}
.sheet-actions > * { flex: 1; min-height: var(--tap-min); }
</style>
