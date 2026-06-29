<script setup lang="ts">
import { computed, onMounted, onUnmounted, reactive, ref, watch } from 'vue'
import { useFloorStore } from '@/stores/useFloorStore'
import { useAuthStore } from '@/stores/useAuthStore'
import { useToast } from '@/composables/useToast'
import type { TableSummary, ZoneSummary } from '@/services/api-client'

const store = useFloorStore()
const auth = useAuthStore()
const toast = useToast()

// ── Layout constants ─────────────────────────────────────────────────────────
const CHIP = 64 // chip diameter in px (> the 56px floor)
const GAP = 28 // spacing between auto-grid slots
const PAD = 28 // inset from the canvas edges
const MOVE_THRESHOLD = 4 // px the pointer must travel before a press counts as a drag
const PANEL_W = 264 // edit-popover width

// Canvas geometry. We track the live width so the auto-grid reflows on resize.
const canvasEl = ref<HTMLElement | null>(null)
const panelEl = ref<HTMLElement | null>(null)
const canvasWidth = ref(900)
let resizeObs: ResizeObserver | null = null

// On phones the free-form canvas is impractical: stored coordinates from a wide
// desktop layout fall off the narrow canvas, and dragging 64px chips is fiddly.
// Below this width we lay every table out in a tidy auto-grid (so all stay
// visible + tappable), disable drag-repositioning, and turn the edit popover
// into a bottom-sheet. Mirrors the shell's sidebar→drawer breakpoint.
const isMobile = ref(false)
let mql: MediaQueryList | null = null
function onMqlChange(e: MediaQueryListEvent) {
  isMobile.value = e.matches
}

watch(canvasEl, (el) => {
  if (resizeObs) {
    resizeObs.disconnect()
    resizeObs = null
  }
  if (el) {
    canvasWidth.value = el.clientWidth || canvasWidth.value
    resizeObs = new ResizeObserver((entries) => {
      const w = entries[0]?.contentRect.width
      if (w) canvasWidth.value = w
    })
    resizeObs.observe(el)
  }
})

const columns = computed(() => Math.max(1, Math.floor((canvasWidth.value - PAD) / (CHIP + GAP))))

function gridSlot(i: number): { x: number; y: number } {
  const cols = columns.value
  const col = i % cols
  const row = Math.floor(i / cols)
  return { x: PAD + col * (CHIP + GAP), y: PAD + row * (CHIP + GAP) }
}

// ── Derived collections ──────────────────────────────────────────────────────
const tablesInZone = computed<TableSummary[]>(() =>
  store.tables.filter((t) => t.zoneId === store.selectedZoneId),
)

// Base render positions. Tables that were never placed (0,0) are tucked into a
// tidy grid so they don't all stack at the origin — purely a render fallback,
// the real coordinate is only persisted once the admin actually drags one.
const basePositions = computed<Record<string, { x: number; y: number }>>(() => {
  const map: Record<string, { x: number; y: number }> = {}
  let gridIdx = 0
  for (const t of tablesInZone.value) {
    // On mobile, ignore persisted coordinates entirely and grid-place every
    // table, so none can sit off the narrow canvas.
    if (isMobile.value || (t.positionX === 0 && t.positionY === 0)) {
      map[t.id] = gridSlot(gridIdx++)
    } else {
      map[t.id] = { x: t.positionX, y: t.positionY }
    }
  }
  return map
})

// Absolutely-positioned chips don't stretch the canvas, so on mobile (where the
// grid can run past the 60vh floor) we set an explicit min-height that fits all
// rows — otherwise overflow:hidden would clip the lower tables.
const canvasStyle = computed<Record<string, string>>(() => {
  const style: Record<string, string> = {}
  if (!isMobile.value) return style
  const cols = columns.value
  const rows = Math.max(1, Math.ceil(tablesInZone.value.length / cols))
  style.minHeight = `${PAD * 2 + rows * (CHIP + GAP)}px`
  return style
})

function posFor(t: TableSummary): { x: number; y: number } {
  if (drag.id === t.id) return { x: drag.x, y: drag.y }
  return basePositions.value[t.id] ?? { x: t.positionX, y: t.positionY }
}

function chipStyle(t: TableSummary): Record<string, string> {
  const p = posFor(t)
  return { left: `${p.x}px`, top: `${p.y}px` }
}

// ── Pointer drag ─────────────────────────────────────────────────────────────
// A press that moves < MOVE_THRESHOLD px is treated as a click (opens the edit
// popover); anything further is a free-form reposition. We update a local
// reactive position on every move for a smooth drag and commit once on release.
const drag = reactive({
  id: null as string | null,
  x: 0,
  y: 0,
  offsetX: 0,
  offsetY: 0,
  startClientX: 0,
  startClientY: 0,
  moved: false,
})

function onPointerDown(e: PointerEvent, t: TableSummary) {
  if (e.pointerType === 'mouse' && e.button !== 0) return
  if (editId.value) closeEdit()
  const canvas = canvasEl.value
  if (!canvas) return
  const rect = canvas.getBoundingClientRect()
  const base = posFor(t)
  drag.id = t.id
  drag.x = base.x
  drag.y = base.y
  drag.offsetX = e.clientX - rect.left - base.x
  drag.offsetY = e.clientY - rect.top - base.y
  drag.startClientX = e.clientX
  drag.startClientY = e.clientY
  drag.moved = false
  ;(e.currentTarget as HTMLElement).setPointerCapture?.(e.pointerId)
}

function onPointerMove(e: PointerEvent) {
  if (drag.id === null) return
  // Waiters get a read-only floor — tables can be tapped (to claim) but not moved.
  if (!canEditFloor.value) return
  // On mobile the layout is a fixed auto-grid; presses are taps only, never drags.
  if (isMobile.value) return
  const canvas = canvasEl.value
  if (!canvas) return
  const rect = canvas.getBoundingClientRect()
  drag.x = e.clientX - rect.left - drag.offsetX
  drag.y = e.clientY - rect.top - drag.offsetY
  if (
    Math.abs(e.clientX - drag.startClientX) > MOVE_THRESHOLD ||
    Math.abs(e.clientY - drag.startClientY) > MOVE_THRESHOLD
  ) {
    drag.moved = true
  }
}

async function onPointerUp(e: PointerEvent, t: TableSummary) {
  if (drag.id !== t.id) return
  ;(e.currentTarget as HTMLElement).releasePointerCapture?.(e.pointerId)

  // A non-move press is a click. A table with active orders opens the order modal
  // ("Preia comanda"); an empty table opens the edit popover (managers only).
  if (!drag.moved) {
    drag.id = null
    if (store.activeOrderTableIds.has(t.id)) openOrderModal(t.id)
    else if (canEditFloor.value) openEdit(t)
    return
  }

  // Clamp to the canvas bounds before persisting.
  const canvas = canvasEl.value
  let x = drag.x
  let y = drag.y
  if (canvas) {
    const maxX = Math.max(0, canvas.clientWidth - CHIP)
    const maxY = Math.max(0, canvas.clientHeight - CHIP)
    x = Math.min(Math.max(0, x), maxX)
    y = Math.min(Math.max(0, y), maxY)
  } else {
    x = Math.max(0, x)
    y = Math.max(0, y)
  }
  x = Math.round(x)
  y = Math.round(y)
  // Keep showing the dropped position while the PATCH is in flight, then let the
  // store-backed coordinate take over (no snap-back flicker).
  drag.x = x
  drag.y = y
  try {
    await store.updateTablePosition(t.id, x, y)
  } catch (err) {
    toast.error(err instanceof Error ? err.message : 'Mutarea mesei a eșuat.')
  } finally {
    if (drag.id === t.id) drag.id = null
  }
}

// ── Zones: select / add / rename / delete ────────────────────────────────────
const busy = ref(false)
const addingZone = ref(false)
const newZoneName = ref('')
const editingZoneId = ref<string | null>(null)
const zoneNameDraft = ref('')

function selectZone(id: string) {
  store.selectedZoneId = id
  closeEdit()
}

async function submitNewZone() {
  if (busy.value) return
  const name = newZoneName.value.trim()
  if (!name) {
    cancelAddZone()
    return
  }
  busy.value = true
  try {
    const z = await store.createZone(name)
    store.selectedZoneId = z.id
    toast.success(`Zona „${z.name}” a fost creată.`)
  } catch (err) {
    toast.error(err instanceof Error ? err.message : 'Crearea zonei a eșuat.')
  } finally {
    busy.value = false
    addingZone.value = false
    newZoneName.value = ''
  }
}

function cancelAddZone() {
  addingZone.value = false
  newZoneName.value = ''
}

function startRenameZone(z: ZoneSummary) {
  editingZoneId.value = z.id
  zoneNameDraft.value = z.name
}

async function commitRenameZone(z: ZoneSummary) {
  if (editingZoneId.value !== z.id) return
  editingZoneId.value = null
  const name = zoneNameDraft.value.trim()
  if (!name || name === z.name) return
  try {
    await store.renameZone(z.id, name)
    toast.success('Zonă redenumită.')
  } catch (err) {
    toast.error(err instanceof Error ? err.message : 'Redenumirea zonei a eșuat.')
  }
}

async function removeZone(z: ZoneSummary) {
  if (!confirm(`Ștergi zona „${z.name}”? Mesele rămân, dar nu vor mai fi atribuite acestei zone.`)) return
  try {
    await store.deleteZone(z.id)
    // The store re-selects the first zone on refetch; guard for safety.
    if (!store.selectedZoneId) {
      const first = store.zones[0]
      store.selectedZoneId = first ? first.id : null
    }
    toast.success('Zonă ștearsă.')
  } catch (err) {
    toast.error(err instanceof Error ? err.message : 'Ștergerea zonei a eșuat.')
  }
}

// ── Tables: add ──────────────────────────────────────────────────────────────
const addingTable = ref(false)
const newTableNumber = ref('')

async function submitNewTable() {
  if (busy.value) return
  const n = newTableNumber.value.trim()
  const zoneId = store.selectedZoneId
  if (!n || !zoneId) {
    cancelAddTable()
    return
  }
  busy.value = true
  try {
    const slot = gridSlot(tablesInZone.value.length)
    await store.createTable({ tableNumber: n, zoneId, positionX: slot.x, positionY: slot.y })
    toast.success(`Masa ${n} a fost adăugată.`)
  } catch (err) {
    toast.error(err instanceof Error ? err.message : 'Adăugarea mesei a eșuat.')
  } finally {
    busy.value = false
    addingTable.value = false
    newTableNumber.value = ''
  }
}

function cancelAddTable() {
  addingTable.value = false
  newTableNumber.value = ''
}

// ── Edit popover ─────────────────────────────────────────────────────────────
const editId = ref<string | null>(null)
const editNumber = ref('')
const editMoveZoneId = ref('')

const editTable = computed<TableSummary | null>(
  () => store.tables.find((t) => t.id === editId.value) ?? null,
)

const editPanelStyle = computed<Record<string, string>>(() => {
  // On mobile the panel is a CSS-driven bottom-sheet — no inline anchoring.
  if (isMobile.value) return {}
  const style: Record<string, string> = { width: `${PANEL_W}px` }
  const t = editTable.value
  if (!t) return style
  const p = posFor(t)
  let left = p.x + CHIP + 12
  if (left + PANEL_W > canvasWidth.value) left = p.x - PANEL_W - 12
  if (left < PAD) left = PAD
  style.left = `${left}px`
  style.top = `${Math.max(PAD, p.y)}px`
  return style
})

function openEdit(t: TableSummary) {
  editId.value = t.id
  editNumber.value = t.tableNumber ?? ''
  editMoveZoneId.value = t.zoneId ?? ''
}

function closeEdit() {
  editId.value = null
}

async function commitRenameTable() {
  const t = editTable.value
  if (!t) return
  const n = editNumber.value.trim()
  if (!n || n === t.tableNumber) return
  try {
    await store.renameTable(t.id, n)
    toast.success('Masă redenumită.')
  } catch (err) {
    toast.error(err instanceof Error ? err.message : 'Redenumirea mesei a eșuat.')
  }
}

async function commitMoveZone() {
  const t = editTable.value
  if (!t) return
  const zid = editMoveZoneId.value
  // Contract: only ever move a table to a real zone id, never null.
  if (!zid || zid === t.zoneId) return
  try {
    await store.moveTableToZone(t.id, zid)
    toast.success('Masă mutată în altă zonă.')
    closeEdit()
  } catch (err) {
    toast.error(err instanceof Error ? err.message : 'Mutarea mesei a eșuat.')
  }
}

async function removeTable() {
  const t = editTable.value
  if (!t) return
  if (!confirm(`Ștergi masa ${t.tableNumber ?? ''}?`)) return
  try {
    await store.deleteTable(t.id)
    closeEdit()
    toast.success('Masă ștearsă.')
  } catch (err) {
    toast.error(err instanceof Error ? err.message : 'Ștergerea mesei a eșuat.')
  }
}

// ── Order modal ("Preia comanda") ────────────────────────────────────────────
const orderModalTableId = ref<string | null>(null)
const claimingId = ref<string | null>(null)
const canEditFloor = computed(() => !auth.isWaiter)
const isManager = computed(() => auth.canManageWaitersOfActive)
const myUserId = computed(() => auth.user?.id ?? null)

const orderModalTable = computed(
  () => store.tables.find((t) => t.id === orderModalTableId.value) ?? null,
)
const orderModalOrders = computed(() =>
  orderModalTableId.value ? store.ordersForTable(orderModalTableId.value) : [],
)

function openOrderModal(tableId: string) {
  orderModalTableId.value = tableId
}
function closeOrderModal() {
  orderModalTableId.value = null
}

async function claimOrder(orderId: string) {
  claimingId.value = orderId
  try {
    await store.claim(orderId)
    toast.success('Ai preluat comanda.')
  } catch (err) {
    toast.error(err instanceof Error ? err.message : 'Preluarea comenzii a eșuat.')
    await store.refreshActiveOrders()
  } finally {
    claimingId.value = null
  }
}

async function releaseOrder(orderId: string) {
  claimingId.value = orderId
  try {
    await store.release(orderId)
    toast.success('Comanda a fost eliberată.')
  } catch (err) {
    toast.error(err instanceof Error ? err.message : 'Eliberarea comenzii a eșuat.')
    await store.refreshActiveOrders()
  } finally {
    claimingId.value = null
  }
}

function editTableFromModal() {
  const t = orderModalTable.value
  closeOrderModal()
  if (t) openEdit(t)
}

function statusLabel(status: string): string {
  if (status === 'submitted') return 'Comandă nouă'
  if (status === 'preparing') return 'În pregătire'
  if (status === 'ready') return 'Finalizată'
  return status
}

// ── Lifecycle ────────────────────────────────────────────────────────────────
function onKeydown(e: KeyboardEvent) {
  if (e.key !== 'Escape') return
  if (orderModalTableId.value) closeOrderModal()
  else if (editId.value) closeEdit()
}

onMounted(async () => {
  mql = window.matchMedia('(max-width: 767.98px)')
  isMobile.value = mql.matches
  mql.addEventListener('change', onMqlChange)

  try {
    await store.fetchZones()
    await store.fetchTables()
    await store.refreshActiveOrders()
  } catch (err) {
    toast.error(err instanceof Error ? err.message : 'Încărcarea planului de sală a eșuat.')
  }
  window.addEventListener('keydown', onKeydown)
})

onUnmounted(() => {
  // The active-order realtime connection is owned by App.vue (so the sidebar dot
  // and Comenzi board stay live everywhere) — we don't tear it down here.
  window.removeEventListener('keydown', onKeydown)
  mql?.removeEventListener('change', onMqlChange)
  if (resizeObs) {
    resizeObs.disconnect()
    resizeObs = null
  }
})

// Auto-focus directive for revealed inline inputs.
const vFocus = {
  mounted: (el: HTMLElement) => el.focus(),
}
</script>

<template>
  <div class="max-w-6xl mx-auto relative">
    <!-- Header -->
    <div class="flex items-start justify-between gap-4 flex-wrap mb-8">
      <div>
        <div class="eyebrow mb-4">Sală &amp; mese</div>
        <h1 class="display-title text-[44px] md:text-[56px]">Plan sală</h1>
        <p class="text-ink-70 text-base mt-2 max-w-xl">
          <template v-if="canEditFloor">Grupează mesele pe zone și aranjează-le liber prin tragere. </template>
          Punctul roșu arată o comandă nepreluată — apasă pe masă ca să vezi comanda și
          să o preiei. Clopoțelul arată o chemare de ospătar.
        </p>
      </div>
      <div class="tag-pill" role="status" aria-live="polite">
        <span
          class="live-dot"
          :style="store.isConnected ? '' : 'background: var(--ink40); box-shadow: none;'"
          aria-hidden="true"
        ></span>
        <span>{{ store.isConnected ? 'Live' : 'Offline' }}</span>
      </div>
    </div>

    <!-- Empty state: no zones yet -->
    <section v-if="store.zones.length === 0" class="card-paper p-8 text-center">
      <div class="eyebrow mb-3">Începe aici</div>
      <h2 class="display-title text-3xl mb-2">Nicio zonă încă</h2>
      <template v-if="canEditFloor">
        <p class="text-ink-70 mb-5">
          Creează prima zonă (ex: Terasă, Salon, Bar) ca să începi să aranjezi mesele.
        </p>
        <form class="flex gap-2 justify-center max-w-md mx-auto" @submit.prevent="submitNewZone">
          <input
            v-focus
            v-model="newZoneName"
            placeholder="Nume zonă (ex: Terasă)"
            class="field-input"
            required
          />
          <button type="submit" class="pill-btn" :disabled="busy">Creează zonă</button>
        </form>
      </template>
      <p v-else class="text-ink-70">
        Sala nu a fost încă configurată. Roagă un administrator să adauge zone și mese.
      </p>
    </section>

    <template v-else>
      <!-- Zone tab bar -->
      <div class="zone-bar">
        <div class="zone-tabs">
          <div
            v-for="z in store.zones"
            :key="z.id"
            class="zone-tab"
            :class="{ 'zone-tab-active': z.id === store.selectedZoneId }"
          >
            <span
              v-if="store.zoneIdsWithUnclaimed.has(z.id)"
              class="zone-dot"
              title="Comandă nepreluată în această zonă"
              aria-label="Comandă nepreluată în această zonă"
            ></span>
            <input
              v-if="editingZoneId === z.id"
              v-focus
              v-model="zoneNameDraft"
              class="zone-edit-input"
              @keyup.enter="commitRenameZone(z)"
              @keyup.esc="editingZoneId = null"
              @blur="commitRenameZone(z)"
            />
            <template v-else>
              <button type="button" class="zone-tab-label" @click="selectZone(z.id)">
                {{ z.name }}
              </button>
              <span v-if="canEditFloor && z.id === store.selectedZoneId" class="zone-tab-actions">
                <button
                  type="button"
                  class="zone-mini"
                  title="Redenumește zona"
                  aria-label="Redenumește zona"
                  @click.stop="startRenameZone(z)"
                >
                  ✎
                </button>
                <button
                  type="button"
                  class="zone-mini zone-mini-danger"
                  title="Șterge zona"
                  aria-label="Șterge zona"
                  @click.stop="removeZone(z)"
                >
                  ✕
                </button>
              </span>
            </template>
          </div>

          <!-- Add zone (managers only) -->
          <template v-if="canEditFloor">
            <input
              v-if="addingZone"
              v-focus
              v-model="newZoneName"
              placeholder="Nume zonă"
              class="zone-edit-input"
              @keyup.enter="submitNewZone"
              @keyup.esc="cancelAddZone"
              @blur="submitNewZone"
            />
            <button v-else type="button" class="zone-tab zone-add-btn" @click="addingZone = true">
              + Zonă nouă
            </button>
          </template>
        </div>
      </div>

      <!-- Toolbar (managers only) -->
      <div v-if="canEditFloor" class="flex items-center gap-3 flex-wrap mb-4">
        <input
          v-if="addingTable"
          v-focus
          v-model="newTableNumber"
          placeholder="Număr masă (ex: 12)"
          class="field-input"
          style="max-width: 220px"
          @keyup.enter="submitNewTable"
          @keyup.esc="cancelAddTable"
          @blur="submitNewTable"
        />
        <button
          v-else
          type="button"
          class="pill-btn"
          :disabled="!store.selectedZoneId"
          @click="addingTable = true"
        >
          + Masă
        </button>
        <span v-if="!store.selectedZoneId" class="field-hint">
          Creează mai întâi o zonă pentru a adăuga mese.
        </span>
      </div>

      <!-- Canvas -->
      <div ref="canvasEl" class="canvas card-paper" :style="canvasStyle">
        <div v-if="tablesInZone.length === 0" class="canvas-empty">
          Nicio masă în această zonă încă — adaugă una cu „+ Masă”.
        </div>

        <button
          v-for="t in tablesInZone"
          :key="t.id"
          type="button"
          class="chip"
          :class="{ 'chip-dragging': drag.id === t.id, 'chip-editing': editId === t.id, 'chip-readonly': !canEditFloor }"
          :style="chipStyle(t)"
          @pointerdown="onPointerDown($event, t)"
          @pointermove="onPointerMove($event)"
          @pointerup="onPointerUp($event, t)"
        >
          <span class="seat seat-top" aria-hidden="true"></span>
          <span class="seat seat-bottom" aria-hidden="true"></span>
          <span class="seat seat-left" aria-hidden="true"></span>
          <span class="seat seat-right" aria-hidden="true"></span>
          <span class="table-top">
            <span class="chip-num">{{ t.tableNumber || '—' }}</span>
            <span
              v-if="store.unclaimedTableIds.has(t.id)"
              class="chip-badge chip-badge-order"
              title="Comandă nepreluată"
              aria-label="Comandă nepreluată"
            ></span>
            <span
              v-else-if="store.activeOrderTableIds.has(t.id)"
              class="chip-badge chip-badge-claimed"
              title="Comandă preluată"
              aria-label="Comandă preluată"
            ></span>
            <span
              v-if="store.waiterCallTableIds.has(t.id)"
              class="chip-badge chip-badge-call"
              title="Cheamă ospătar"
              aria-label="Cheamă ospătar"
            >🔔</span>
          </span>
        </button>

        <!-- Edit popover (click-away backdrop + anchored card) -->
        <div v-if="editId" class="edit-backdrop" @pointerdown="closeEdit"></div>
        <div
          v-if="editTable"
          ref="panelEl"
          class="edit-panel card-white"
          :style="editPanelStyle"
          @pointerdown.stop
        >
          <div class="flex items-center justify-between mb-3">
            <div class="eyebrow">Masa {{ editTable.tableNumber || '—' }}</div>
            <button type="button" class="icon-btn" aria-label="Închide" @click="closeEdit">✕</button>
          </div>

          <label class="field-label">Număr masă</label>
          <div class="flex gap-2 mt-1 mb-4">
            <input
              v-model="editNumber"
              class="field-input"
              @keyup.enter="commitRenameTable"
            />
            <button type="button" class="btn-ghost" @click="commitRenameTable">Salvează</button>
          </div>

          <label class="field-label">Mută în zonă</label>
          <select v-model="editMoveZoneId" class="field-input mt-1 mb-4" @change="commitMoveZone">
            <option v-for="z in store.zones" :key="z.id" :value="z.id">{{ z.name }}</option>
          </select>

          <button type="button" class="btn-danger w-full" @click="removeTable">Șterge masa</button>
        </div>
      </div>
    </template>

    <!-- Order modal ("Preia comanda") — opens when a table with orders is tapped -->
    <div
      v-if="orderModalTable"
      class="order-modal-backdrop"
      @pointerdown.self="closeOrderModal"
    >
      <div class="order-modal card-white" role="dialog" aria-modal="true">
        <div class="order-modal-head">
          <div>
            <div class="eyebrow">Masa {{ orderModalTable.tableNumber || '—' }}</div>
            <div class="display-title text-2xl">
              {{ orderModalOrders.length }}
              {{ orderModalOrders.length === 1 ? 'comandă' : 'comenzi' }}
            </div>
          </div>
          <button type="button" class="icon-btn" aria-label="Închide" @click="closeOrderModal">✕</button>
        </div>

        <div v-if="orderModalOrders.length === 0" class="order-empty">
          Nicio comandă activă la această masă.
        </div>

        <div v-for="o in orderModalOrders" :key="o.id" class="order-card">
          <div class="order-card-top">
            <span class="order-num">#{{ o.orderNumber ?? '—' }}</span>
            <span class="order-status" :class="`order-status--${o.status}`">{{ statusLabel(o.status) }}</span>
          </div>

          <ul class="order-items">
            <li v-for="it in o.items" :key="it.id">
              <span class="oi-qty">×{{ it.quantity }}</span>
              <span class="oi-name">{{ it.productName }}</span>
              <span class="oi-price">{{ it.subtotal.toFixed(2) }} RON</span>
            </li>
          </ul>

          <div class="order-card-bot">
            <span class="order-total">Total · {{ o.totalAmount.toFixed(2) }} RON</span>
          </div>

          <!-- Claim controls -->
          <div class="order-actions">
            <button
              v-if="!o.claimedByWaiterId"
              type="button"
              class="pill-btn-ink"
              :disabled="claimingId === o.id"
              @click="claimOrder(o.id)"
            >
              {{ claimingId === o.id ? 'Se preia…' : 'Preia comanda' }}
            </button>

            <template v-else-if="o.claimedByWaiterId === myUserId">
              <div class="claimed-by claimed-by--me"><span aria-hidden="true">✓</span> Ai preluat această comandă</div>
              <button
                type="button"
                class="btn-ghost"
                :disabled="claimingId === o.id"
                @click="releaseOrder(o.id)"
              >
                Eliberează
              </button>
            </template>

            <template v-else>
              <div class="claimed-by">Preluată de {{ o.claimedByName || 'alt coleg' }}</div>
              <div v-if="isManager" class="order-actions-row">
                <button
                  type="button"
                  class="pill-btn-ink"
                  :disabled="claimingId === o.id"
                  @click="claimOrder(o.id)"
                >
                  Preia de la {{ o.claimedByName || 'coleg' }}
                </button>
                <button
                  type="button"
                  class="btn-ghost"
                  :disabled="claimingId === o.id"
                  @click="releaseOrder(o.id)"
                >
                  Eliberează
                </button>
              </div>
            </template>
          </div>
        </div>

        <button
          v-if="canEditFloor && orderModalTable"
          type="button"
          class="btn-ghost order-edit-link"
          @click="editTableFromModal"
        >
          Editează masa
        </button>
      </div>
    </div>
  </div>
</template>

<style scoped>
/* ── Zone tab bar ───────────────────────────────────────────────────────────── */
.zone-bar {
  margin-bottom: 18px;
  padding-bottom: 14px;
  border-bottom: 1px solid var(--ink12);
}
.zone-tabs {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}
.zone-tab {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 8px 14px;
  min-height: var(--tap-min);
  border-radius: 999px;
  background: var(--ink06);
  border: 1px solid transparent;
  color: var(--ink);
  font-weight: 600;
  font-size: 15px;
  transition: background 0.15s ease, color 0.15s ease, border-color 0.15s ease;
}
.zone-tab-label {
  background: transparent;
  border: none;
  color: inherit;
  font: inherit;
  cursor: pointer;
  padding: 0;
}
.zone-tab-active {
  background: var(--ink);
  color: var(--paperHi);
  border-color: var(--ink);
  box-shadow: var(--shadow-soft);
}
.zone-tab-active .zone-tab-label {
  color: var(--paperHi);
}
.zone-tab-actions {
  display: inline-flex;
  gap: 2px;
  margin-left: 4px;
}
.zone-mini {
  width: 24px;
  height: 24px;
  border-radius: 50%;
  display: grid;
  place-items: center;
  background: transparent;
  border: none;
  color: rgba(245, 240, 232, 0.85);
  cursor: pointer;
  font-size: 12px;
  line-height: 1;
  opacity: 0.9;
  transition: opacity 0.15s ease, background 0.15s ease, color 0.15s ease;
}
.zone-mini:hover {
  opacity: 1;
  color: #fff;
  background: rgba(255, 255, 255, 0.14);
}
.zone-mini-danger:hover {
  color: #fff;
  background: var(--accent);
}
.zone-edit-input {
  padding: 8px 14px;
  min-height: var(--tap-min);
  width: 170px;
  max-width: 200px;
  border-radius: 999px;
  border: 1px solid var(--ink12);
  background: var(--card);
  color: var(--ink);
  font-size: 15px;
  font-weight: 600;
}
.zone-add-btn {
  border: 1px dashed var(--ink12);
  background: transparent;
  color: var(--ink70);
  cursor: pointer;
}
.zone-add-btn:hover {
  border-color: var(--ink);
  color: var(--ink);
}

/* ── Canvas ─────────────────────────────────────────────────────────────────── */
.canvas {
  position: relative;
  min-height: 60vh;
  overflow: hidden;
}
.canvas-empty {
  position: absolute;
  inset: 0;
  display: grid;
  place-items: center;
  text-align: center;
  padding: 24px;
  color: var(--ink40);
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 13px;
  text-transform: uppercase;
  letter-spacing: 0.05em;
}

/* ── Table icon ─────────────────────────────────────────────────────────────── */
/* The .chip is a transparent 64px hit area; the visible table is a rounded
   tabletop with four chairs around it. */
.chip {
  position: absolute;
  width: 64px;
  height: 64px;
  border: none;
  background: transparent;
  box-shadow: none;
  padding: 0;
  display: grid;
  place-items: center;
  color: var(--ink);
  cursor: grab;
  touch-action: none;
  user-select: none;
  -webkit-user-select: none;
  z-index: 1;
}
.chip:active {
  cursor: grabbing;
}
.chip-dragging {
  z-index: 4;
  cursor: grabbing;
}
.chip-editing {
  z-index: 3;
}

/* Tabletop — the surface that carries the number and the live badges. */
.table-top {
  position: relative;
  width: 42px;
  height: 42px;
  border-radius: 13px;
  background: var(--card);
  border: 1.5px solid var(--ink12);
  box-shadow: var(--shadow-soft);
  display: grid;
  place-items: center;
  transition: box-shadow 0.15s ease, border-color 0.15s ease;
}
.chip:hover .table-top {
  border-color: var(--ink);
}
.chip-dragging .table-top {
  box-shadow: var(--shadow-pop);
  border-color: var(--ink);
  transition: none;
}
.chip-editing .table-top {
  border-color: var(--accent);
  box-shadow: 0 0 0 3px var(--accentSoft);
}

/* Chairs — one seat on each side of the table. */
.seat {
  position: absolute;
  background: var(--ink40);
  border-radius: 4px;
  transition: background 0.15s ease;
}
.seat-top,
.seat-bottom {
  width: 22px;
  height: 7px;
  left: 50%;
  transform: translateX(-50%);
}
.seat-left,
.seat-right {
  width: 7px;
  height: 22px;
  top: 50%;
  transform: translateY(-50%);
}
.seat-top {
  top: 1px;
}
.seat-bottom {
  bottom: 1px;
}
.seat-left {
  left: 1px;
}
.seat-right {
  right: 1px;
}
.chip:hover .seat,
.chip-dragging .seat {
  background: var(--ink70);
}

.chip-num {
  font-family: 'Inter Tight', sans-serif;
  font-weight: 700;
  font-size: 16px;
  line-height: 1;
  pointer-events: none;
}

/* Live overlays — both can show at once (order top-right, bell top-left). */
.chip-badge {
  position: absolute;
  pointer-events: none;
}
.chip-badge-order {
  top: -6px;
  right: -6px;
  width: 16px;
  height: 16px;
  border-radius: 50%;
  background: var(--accent);
  border: 2px solid var(--card);
  animation: chip-pulse 1.6s ease-in-out infinite;
}
/* Claimed order — calmer dot (taken / in progress), no pulse. */
.chip-badge-claimed {
  top: -6px;
  right: -6px;
  width: 14px;
  height: 14px;
  border-radius: 50%;
  background: var(--lime);
  border: 2px solid var(--card);
}
.chip-badge-call {
  top: -12px;
  left: -10px;
  font-size: 16px;
  line-height: 1;
  filter: drop-shadow(0 1px 2px rgba(0, 0, 0, 0.3));
  animation: chip-bell 1.2s ease-in-out infinite;
  transform-origin: 50% 0;
}
@keyframes chip-pulse {
  0%,
  100% {
    box-shadow: 0 0 0 0 var(--accentBorder);
  }
  50% {
    box-shadow: 0 0 0 7px transparent;
  }
}
@keyframes chip-bell {
  0%,
  100% {
    transform: rotate(0deg);
  }
  20% {
    transform: rotate(14deg);
  }
  60% {
    transform: rotate(-12deg);
  }
}

/* ── Edit popover ───────────────────────────────────────────────────────────── */
.edit-backdrop {
  position: absolute;
  inset: 0;
  z-index: 5;
}
.edit-panel {
  position: absolute;
  z-index: 6;
  padding: 16px;
  box-shadow: var(--shadow-pop);
}

/* Mobile: the anchored popover would be clipped by the canvas's overflow:hidden,
   so it becomes a bottom-sheet escaping to the viewport (fixed). The backdrop
   covers the whole screen. Inline anchoring is dropped (editPanelStyle → {}). */
@media (max-width: 767.98px) {
  .edit-backdrop {
    position: fixed;
    z-index: 80;
    background: rgba(0, 0, 0, 0.5);
    -webkit-backdrop-filter: blur(3px);
    backdrop-filter: blur(3px);
  }
  .edit-panel {
    position: fixed;
    z-index: 81;
    left: 0;
    right: 0;
    bottom: 0;
    top: auto;
    width: auto;
    padding: 20px;
    max-height: 86vh;
    overflow-y: auto;
    border-radius: 20px 20px 0 0;
  }
  /* No drag on touch — let presses on a chip scroll the page instead of locking it. */
  .chip {
    touch-action: manipulation;
    cursor: pointer;
  }
}

/* Read-only floor (waiters): tables can be tapped to claim, not dragged. */
.chip-readonly {
  cursor: pointer;
}

/* Red dot on a zone tab that holds an unclaimed order. */
.zone-dot {
  width: 9px;
  height: 9px;
  border-radius: 50%;
  background: var(--accent);
  flex-shrink: 0;
  animation: chip-pulse 1.6s ease-in-out infinite;
}
.zone-tab-active .zone-dot {
  background: var(--lime);
}

/* ── Order modal ("Preia comanda") ──────────────────────────────────────────── */
.order-modal-backdrop {
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
.order-modal {
  width: min(92vw, 460px);
  max-height: 86vh;
  overflow-y: auto;
  padding: 22px;
  box-shadow: var(--shadow-pop);
}
.order-modal-head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 12px;
  margin-bottom: 16px;
}
.order-empty {
  padding: 20px;
  text-align: center;
  color: var(--ink40);
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 13px;
}
.order-card {
  border: 1px solid var(--ink12);
  border-radius: 16px;
  padding: 14px;
  margin-bottom: 14px;
  background: var(--card);
}
.order-card-top {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  margin-bottom: 10px;
}
.order-num {
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-weight: 800;
  font-size: 15px;
  background: var(--ink);
  color: var(--paperHi);
  padding: 4px 10px;
  border-radius: 10px;
}
.order-status {
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 11px;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  padding: 4px 10px;
  border-radius: 999px;
  background: var(--ink06);
  color: var(--ink);
}
.order-status--submitted { background: var(--lime); color: var(--onLime); }
.order-status--preparing { background: var(--accentSoft); color: var(--accentDk); }
.order-status--ready { background: var(--ink); color: var(--lime); }
.order-items {
  list-style: none;
  margin: 0 0 10px;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 6px;
}
.order-items li {
  display: flex;
  align-items: baseline;
  gap: 8px;
  font-size: 14px;
  color: var(--ink);
}
.oi-qty {
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-weight: 700;
  color: var(--ink70);
  min-width: 30px;
}
.oi-name { flex: 1; }
.oi-price {
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 12px;
  color: var(--ink70);
}
.order-card-bot {
  border-top: 1px solid var(--ink06);
  padding-top: 10px;
  margin-bottom: 12px;
}
.order-total {
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-weight: 700;
  font-size: 13px;
}
.order-actions {
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.order-actions > .pill-btn-ink { width: 100%; }
.order-actions-row {
  display: flex;
  gap: 8px;
}
.order-actions-row > * { flex: 1; }
.claimed-by {
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 12px;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  color: var(--ink70);
}
.claimed-by--me {
  color: var(--onLime);
  background: var(--lime);
  border-radius: 999px;
  text-align: center;
  padding: 9px;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
}
.order-edit-link {
  width: 100%;
  margin-top: 2px;
}

@media (prefers-reduced-motion: reduce) {
  .chip-badge-order,
  .chip-badge-call,
  .zone-dot {
    animation: none;
  }
}
</style>
