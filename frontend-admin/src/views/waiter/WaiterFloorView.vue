<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import type { WaiterFloorTableDto } from '@shared/types/waiter'
import { waiterApi } from '@/services/waiterApi'
import { useAuthStore } from '@/stores/useAuthStore'
import { useWaiterRealtimeStore } from '@/stores/useWaiterRealtimeStore'
import { useToast } from '@/composables/useToast'
import { formatMoney } from '@/utils/money'
import {
  seatSlots,
  tabletopDims,
  clampCapacity,
  normalizeShape,
  DEFAULT_CAPACITY,
  MAX_CAPACITY,
  type TableShape,
} from '@/utils/tableSeats'

/**
 * The waiter's home screen, in two presentations:
 *
 *  - "Mese" — the same spatial floor plan the manager arranges in Sala (zones,
 *    table shapes, seats, positions), drawn read-only: a waiter can look and tap
 *    (claim / open), never move, rename or edit a table. The map renders at the
 *    manager's desktop coordinates and is uniformly scaled down to the phone
 *    frame, so the room reads exactly like the admin plan.
 *  - "Mesele mele" (the `?filtru=ale-mele` tab) — the caller's open tables as a
 *    card list, because that tab is a work list (totals, order counts), not a map.
 *
 * Data comes exclusively from `useWaiterRealtimeStore` (events are triggers,
 * REST is truth) — this view never talks to SignalR itself and only calls the
 * API to *change* something (claim).
 */
const route = useRoute()
const router = useRouter()
const auth = useAuthStore()
const store = useWaiterRealtimeStore()
const toast = useToast()

type TableState = 'free' | 'mine' | 'other'

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

// ── Map geometry (mirrors FloorPlanView's constants so the plan reads the same) ─
const MAP_PAD = 24
const MAP_GAP = 24
const TABLE_MIN = 46
const TABLE_MAX = 74
const SEAT_MARGIN = 15
/** Below this the chips stop shrinking and the map pans horizontally instead. */
const MIN_SCALE = 0.55

function tableSize(capacity: number): number {
  const c = clampCapacity(capacity)
  return Math.round(TABLE_MIN + ((c - 1) / (MAX_CAPACITY - 1)) * (TABLE_MAX - TABLE_MIN))
}
function shapeOf(t: WaiterFloorTableDto): TableShape {
  return normalizeShape(t.shape)
}
function chipBox(t: WaiterFloorTableDto): { w: number; h: number } {
  const d = tabletopDims(tableSize(t.capacity ?? DEFAULT_CAPACITY), shapeOf(t))
  return { w: d.w + SEAT_MARGIN * 2, h: d.h + SEAT_MARGIN * 2 }
}
// Auto-grid fallback slots are spaced by the largest possible footprint, same
// rule as the admin canvas, so unplaced tables never overlap.
const CHIP_FALLBACK = (() => {
  const d = tabletopDims(TABLE_MAX, 'rectangle')
  return Math.max(d.w, d.h) + SEAT_MARGIN * 2
})()

// ── Zone tabs (read-only: no add / rename / delete) ─────────────────────────
const NO_ZONE = '__no_zone__'
const zoneTabs = computed(() => {
  const tabs = store.zones.map((z) => ({ id: z.id, name: z.name }))
  if (store.tables.some((t) => !t.zoneId)) tabs.push({ id: NO_ZONE, name: 'Fără zonă' })
  return tabs
})
const mapZoneId = ref<string | null>(null)
watch(
  zoneTabs,
  (tabs) => {
    if (!tabs.some((t) => t.id === mapZoneId.value)) mapZoneId.value = tabs[0]?.id ?? null
  },
  { immediate: true },
)

/** Red dot on a zone tab that still has unclaimed QR rounds somewhere in it. */
const zoneIdsWithUnclaimed = computed<Set<string>>(() => {
  const ids = new Set<string>()
  for (const t of store.tables) {
    if (t.unclaimedOrderCount > 0) ids.add(t.zoneId ?? NO_ZONE)
  }
  return ids
})

const mapTables = computed<WaiterFloorTableDto[]>(() =>
  store.tables.filter((t) =>
    mapZoneId.value === NO_ZONE ? !t.zoneId : t.zoneId === mapZoneId.value,
  ),
)

// ── Map layout: persisted coordinates, with an auto-grid for never-placed tables ─
const mapLayout = computed(() => {
  const positions: Record<string, { x: number; y: number }> = {}
  const unplaced: WaiterFloorTableDto[] = []
  let maxX = 0
  let maxY = 0

  for (const t of mapTables.value) {
    // (0,0) means the manager never dragged this table — grid-place it below.
    if (t.positionX === 0 && t.positionY === 0) {
      unplaced.push(t)
      continue
    }
    positions[t.id] = { x: t.positionX, y: t.positionY }
    const box = chipBox(t)
    maxX = Math.max(maxX, t.positionX + box.w)
    maxY = Math.max(maxY, t.positionY + box.h)
  }

  const baseW = Math.max(maxX, 380)
  const cols = Math.max(1, Math.floor((baseW - MAP_PAD) / (CHIP_FALLBACK + MAP_GAP)))
  const startY = maxY > 0 ? maxY + MAP_GAP : MAP_PAD
  unplaced.forEach((t, i) => {
    const x = MAP_PAD + (i % cols) * (CHIP_FALLBACK + MAP_GAP)
    const y = startY + Math.floor(i / cols) * (CHIP_FALLBACK + MAP_GAP)
    positions[t.id] = { x, y }
    const box = chipBox(t)
    maxX = Math.max(maxX, x + box.w)
    maxY = Math.max(maxY, y + box.h)
  })

  return { positions, w: maxX + MAP_PAD, h: maxY + MAP_PAD }
})

// Live canvas width → uniform scale factor. Below MIN_SCALE the map keeps its
// size and pans horizontally instead of shrinking chips beyond usable taps.
const mapEl = ref<HTMLElement | null>(null)
const mapWidth = ref(400)
let resizeObs: ResizeObserver | null = null
watch(mapEl, (el) => {
  resizeObs?.disconnect()
  resizeObs = null
  if (el) {
    mapWidth.value = el.clientWidth || mapWidth.value
    resizeObs = new ResizeObserver((entries) => {
      const w = entries[0]?.contentRect.width
      if (w) mapWidth.value = w
    })
    resizeObs.observe(el)
  }
})
onBeforeUnmount(() => {
  resizeObs?.disconnect()
  resizeObs = null
})

const mapScale = computed(() => {
  const natural = mapLayout.value.w
  if (natural <= 0) return 1
  return Math.min(1, Math.max(MIN_SCALE, mapWidth.value / natural))
})

// The stage renders at natural size and is transform-scaled; the sizer div gives
// the scroll container the *scaled* footprint (transforms don't affect layout).
const stageStyle = computed(() => ({
  width: `${mapLayout.value.w}px`,
  height: `${mapLayout.value.h}px`,
  transform: `scale(${mapScale.value})`,
}))
const sizerStyle = computed(() => ({
  width: `${Math.round(mapLayout.value.w * mapScale.value)}px`,
  height: `${Math.round(mapLayout.value.h * mapScale.value)}px`,
}))

function chipStyle(t: WaiterFloorTableDto): Record<string, string> {
  const p = mapLayout.value.positions[t.id] ?? { x: 0, y: 0 }
  const box = chipBox(t)
  return { left: `${p.x}px`, top: `${p.y}px`, width: `${box.w}px`, height: `${box.h}px` }
}

function tableTopStyle(t: WaiterFloorTableDto): Record<string, string> {
  const base = tableSize(t.capacity ?? DEFAULT_CAPACITY)
  const d = tabletopDims(base, shapeOf(t))
  return {
    width: `${d.w}px`,
    height: `${d.h}px`,
    borderRadius: d.radius,
    fontSize: `${Math.round(base * 0.32)}px`,
  }
}

function seatsFor(t: WaiterFloorTableDto) {
  return seatSlots(t.capacity ?? DEFAULT_CAPACITY, shapeOf(t))
}

// ── Table state (shared by map, list and sheet) ─────────────────────────────
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

const myTablesList = computed<WaiterFloorTableDto[]>(() =>
  // Table numbers are free-form strings ("12", "Terasa 3") — numeric-aware sort
  // keeps 2 before 10 without assuming they parse as integers.
  [...store.myTables].sort((a, b) =>
    (a.tableNumber ?? '').localeCompare(b.tableNumber ?? '', 'ro', { numeric: true }),
  ),
)

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
        <h1 class="floor-title">{{ mineOnly ? 'Mesele deschise de mine' : 'Planul sălii' }}</h1>
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

    <!-- ══ "Mesele mele" — the caller's open tables as a work list ══ -->
    <template v-if="mineOnly">
      <p v-if="!myTablesList.length" class="floor-empty">Nu ai nicio masă deschisă.</p>

      <div v-else class="table-grid">
        <button
          v-for="table in myTablesList"
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
    </template>

    <!-- ══ "Mese" — the Sala floor plan, read-only layout, live states ══ -->
    <template v-else>
      <div v-if="zoneTabs.length" class="zone-chips" role="tablist" aria-label="Alege zona">
        <button
          v-for="zone in zoneTabs"
          :key="zone.id"
          type="button"
          class="chip"
          :class="{ 'chip-active': mapZoneId === zone.id }"
          role="tab"
          :aria-selected="mapZoneId === zone.id"
          @click="mapZoneId = zone.id"
        >
          <span
            v-if="zoneIdsWithUnclaimed.has(zone.id)"
            class="chip-dot"
            title="Comandă nepreluată în această zonă"
          ></span>
          {{ zone.name }}
        </button>
      </div>

      <p v-if="!store.tables.length" class="floor-empty">
        Sala nu a fost încă configurată. Roagă un administrator să adauge zone și mese.
      </p>
      <p v-else-if="!mapTables.length" class="floor-empty">Nicio masă în această zonă.</p>

      <div v-else ref="mapEl" class="map-scroll card-paper">
        <div class="map-sizer" :style="sizerStyle">
          <div class="map-stage" :style="stageStyle">
            <button
              v-for="table in mapTables"
              :key="table.id"
              type="button"
              class="map-chip"
              :aria-label="`Masa ${table.tableNumber ?? ''} — ${stateLabel(table)}`"
              :style="chipStyle(table)"
              @click="onTapTable(table)"
            >
              <span
                class="map-top"
                :class="{
                  'map-top--free': stateOf(table) === 'free',
                  'map-top--mine': stateOf(table) === 'mine',
                  'map-top--other': stateOf(table) === 'other',
                  'map-top--help': table.hasOpenHelpRequest,
                }"
                :style="tableTopStyle(table)"
              >
                <span
                  v-for="s in seatsFor(table)"
                  :key="s.key"
                  class="map-seat"
                  :style="s.style"
                  aria-hidden="true"
                ></span>
                <span class="map-num">{{ table.tableNumber || '—' }}</span>
                <span
                  v-if="table.unclaimedOrderCount > 0"
                  class="map-badge map-badge--unclaimed"
                  title="Comandă nepreluată"
                ></span>
                <span
                  v-else-if="table.activeOrderCount > 0"
                  class="map-badge map-badge--active"
                  title="Comandă în lucru"
                ></span>
                <span
                  v-if="table.hasOpenHelpRequest"
                  class="map-badge map-badge--call"
                  title="Cheamă ospătar"
                >◉</span>
              </span>
            </button>
          </div>
        </div>
      </div>

      <!-- Legend — the map speaks in colour; spell it out once. -->
      <div v-if="mapTables.length" class="map-legend" aria-hidden="true">
        <span class="lg"><span class="lg-swatch lg-free"></span>Liberă</span>
        <span class="lg"><span class="lg-swatch lg-mine"></span>Masa mea</span>
        <span class="lg"><span class="lg-swatch lg-other"></span>Ocupată</span>
        <span class="lg"><span class="lg-dot"></span>Comandă nepreluată</span>
        <span class="lg"><span class="lg-call">◉</span>Chemare</span>
      </div>
    </template>

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
          <p v-if="sheetTable.unclaimedOrderCount > 0" class="sheet-note">
            Masa are deja
            {{ sheetTable.unclaimedOrderCount }}
            {{ sheetTable.unclaimedOrderCount === 1 ? 'comandă nepreluată' : 'comenzi nepreluate' }}
            — la preluare {{ sheetTable.unclaimedOrderCount === 1 ? 'intră' : 'intră toate' }} pe nota ta.
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

/* ── Zone tabs ───────────────────────────────────────────────────────────── */
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
  display: inline-flex;
  align-items: center;
  gap: 7px;
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
.chip-dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: var(--accent);
  flex-shrink: 0;
  animation: map-pulse 1.6s ease-in-out infinite;
}
.chip-active .chip-dot { background: var(--lime); }

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

/* ── The floor map ───────────────────────────────────────────────────────── */
/* The stage renders at the manager's desktop coordinates and is uniformly
   scaled down; the sizer carries the scaled footprint so the scroll container
   lays out correctly (transforms don't affect layout). Below MIN_SCALE the map
   stops shrinking and pans horizontally instead. */
.map-scroll {
  overflow-x: auto;
  overflow-y: hidden;
  padding: 0;
  -webkit-overflow-scrolling: touch;
}
.map-sizer {
  position: relative;
  min-width: 100%;
}
.map-stage {
  position: absolute;
  top: 0;
  left: 0;
  transform-origin: top left;
}

.map-chip {
  position: absolute;
  border: none;
  background: transparent;
  box-shadow: none;
  padding: 0;
  display: grid;
  place-items: center;
  color: var(--ink);
  cursor: pointer;
  touch-action: manipulation;
  user-select: none;
  -webkit-user-select: none;
  z-index: 1;
}
.map-chip:active .map-top { transform: scale(0.96); }

/* Tabletop — shape + capacity come from inline styles (same math as Sala). */
.map-top {
  position: relative;
  background: var(--card);
  border: 1.5px solid var(--ink12);
  box-shadow: var(--shadow-soft);
  display: grid;
  place-items: center;
  transition: transform 0.12s ease, border-color 0.15s ease, background 0.15s ease;
}
.map-seat {
  position: absolute;
  background: var(--ink40);
  border-radius: 3px;
}
.map-num {
  font-family: 'Inter Tight', sans-serif;
  font-weight: 700;
  font-size: inherit; /* scales with the tabletop (set inline per capacity) */
  line-height: 1;
  pointer-events: none;
}

/* The three claim states — same vocabulary as the old tiles. */
.map-top--free {
  border-style: dashed;
  border-color: var(--accentBorder);
}
.map-top--mine {
  background: var(--accent);
  border-color: var(--accent);
}
.map-top--mine .map-num { color: var(--onAccent); }
.map-top--mine .map-seat { background: color-mix(in srgb, var(--accent) 45%, var(--ink40)); }
.map-top--other { opacity: 0.55; }
.map-top--help {
  border-color: var(--accent);
  box-shadow: 0 0 0 3px color-mix(in srgb, var(--accent) 24%, transparent);
  animation: map-help-pulse 1.6s ease-in-out infinite;
}
.map-top--help.map-top--other { opacity: 1; }

/* Live overlays — unclaimed round (pulsing), claimed round (calm), call. */
.map-badge {
  position: absolute;
  pointer-events: none;
}
.map-badge--unclaimed {
  top: -6px;
  right: -6px;
  width: 15px;
  height: 15px;
  border-radius: 50%;
  background: var(--accent);
  border: 2px solid var(--card);
  animation: map-pulse 1.6s ease-in-out infinite;
}
.map-badge--active {
  top: -6px;
  right: -6px;
  width: 13px;
  height: 13px;
  border-radius: 50%;
  background: var(--lime);
  border: 2px solid var(--card);
}
.map-badge--call {
  top: -13px;
  left: -9px;
  font-size: 15px;
  line-height: 1;
  color: var(--accent);
  animation: map-call 1.2s ease-in-out infinite;
  transform-origin: 50% 100%;
}
@keyframes map-pulse {
  0%, 100% { box-shadow: 0 0 0 0 var(--accentBorder); }
  50%      { box-shadow: 0 0 0 6px transparent; }
}
@keyframes map-help-pulse {
  0%, 100% { box-shadow: 0 0 0 3px color-mix(in srgb, var(--accent) 24%, transparent); }
  50%      { box-shadow: 0 0 0 7px color-mix(in srgb, var(--accent) 6%, transparent); }
}
@keyframes map-call {
  0%, 100% { transform: rotate(0deg); }
  20%      { transform: rotate(14deg); }
  60%      { transform: rotate(-12deg); }
}
@media (prefers-reduced-motion: reduce) {
  .map-badge--unclaimed,
  .map-badge--call,
  .map-top--help,
  .chip-dot { animation: none; }
}

/* ── Legend ──────────────────────────────────────────────────────────────── */
.map-legend {
  display: flex;
  flex-wrap: wrap;
  gap: 10px 14px;
  margin-top: 12px;
  padding: 0 4px;
}
.lg {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 10px;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  color: var(--ink40);
}
.lg-swatch {
  width: 14px;
  height: 14px;
  border-radius: 5px;
  border: 1.5px solid var(--ink12);
  background: var(--card);
}
.lg-free {
  border-style: dashed;
  border-color: var(--accentBorder);
}
.lg-mine {
  background: var(--accent);
  border-color: var(--accent);
}
.lg-other { opacity: 0.55; }
.lg-dot {
  width: 10px;
  height: 10px;
  border-radius: 50%;
  background: var(--accent);
}
.lg-call {
  color: var(--accent);
  font-size: 12px;
  line-height: 1;
}

/* ── "Mesele mele" card grid (unchanged presentation) ────────────────────── */
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
