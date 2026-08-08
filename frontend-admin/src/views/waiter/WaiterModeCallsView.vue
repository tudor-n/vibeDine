<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import type { WaiterCall } from '@/services/api-client'
import { waiterApi } from '@/services/waiterApi'
import { useAuthStore } from '@/stores/useAuthStore'
import { useWaiterCallsStore } from '@/stores/useWaiterCallsStore'
import { useWaiterRealtimeStore } from '@/stores/useWaiterRealtimeStore'
import { useToast } from '@/composables/useToast'

/**
 * "Apeluri" inside Waiter Mode, split by table ownership:
 *
 *  - "Chemările mele" — calls from tables the caller has claimed. Routed: only
 *    the owning waiter's phone holds (and chimes for) these.
 *  - "Chemări fără ospătar" — calls from unclaimed tables. Accepting one means
 *    taking the table: the button opens a claim sheet, and confirming claims
 *    the table for the caller (adopting its rounds) and then accepts the call.
 *  - "La colegi" — calls owned by other waiters. Waiters never receive these
 *    (the store and the API both filter), so the section renders for managers.
 *
 * The store owns the StaffHub connection app-wide and re-seeds itself on every
 * session change, so a claim/transfer/close moves calls between phones live.
 */
const router = useRouter()
const store = useWaiterCallsStore()
const auth = useAuthStore()
const realtime = useWaiterRealtimeStore()
const toast = useToast()

// Live "time ago" — a coarse 15s tick is plenty for minute-level labels.
const now = ref(Date.now())
let tick: number | null = null
onMounted(() => {
  tick = window.setInterval(() => (now.value = Date.now()), 15000)
})
onBeforeUnmount(() => {
  if (tick) window.clearInterval(tick)
})

// Oldest first inside each section — the table waiting longest sits on top.
function byAge(list: WaiterCall[]): WaiterCall[] {
  return [...list].sort((a, b) => new Date(a.createdAt).getTime() - new Date(b.createdAt).getTime())
}
const myCalls = computed(() => byAge(store.myCalls))
const unassignedCalls = computed(() => byAge(store.unassignedCalls))
const otherCalls = computed(() => byAge(store.otherCalls))

const isManager = computed(() => auth.canManageWaitersOfActive)

function ago(createdAt: string): string {
  const secs = Math.max(0, Math.floor((now.value - new Date(createdAt).getTime()) / 1000))
  if (secs < 60) return 'chiar acum'
  const mins = Math.floor(secs / 60)
  if (mins < 60) return `acum ${mins} min`
  return `acum ${Math.floor(mins / 60)} h`
}

const accepting = ref<Set<string>>(new Set())
async function accept(requestId: string) {
  if (accepting.value.has(requestId)) return
  accepting.value = new Set(accepting.value).add(requestId)
  try {
    await store.acknowledge(requestId)
  } finally {
    const next = new Set(accepting.value)
    next.delete(requestId)
    accepting.value = next
  }
}

// ── Accepting an unassigned call = taking the table ─────────────────────────
// The call's table has no waiter, so answering it is a claim: the sheet says so
// up front, and confirming claims the table (adopting its QR rounds) and only
// then accepts the call.
const claimSheet = ref<WaiterCall | null>(null)
const isClaimingCall = ref(false)

function openClaimSheet(call: WaiterCall) {
  claimSheet.value = call
}
function closeClaimSheet() {
  if (isClaimingCall.value) return
  claimSheet.value = null
}
async function confirmClaimAndAccept() {
  const call = claimSheet.value
  if (!call || isClaimingCall.value) return
  isClaimingCall.value = true
  try {
    const session = await waiterApi.claimTable(call.tableId)
    // The table is theirs; even if the acknowledge below fails, the call has
    // become "a mea" (the store re-seeds on TableSessionOpened) and can be
    // accepted from that section.
    claimSheet.value = null
    void realtime.refreshFloor()
    try {
      await store.acknowledge(call.requestId)
      toast.success(
        call.tableNumber
          ? `Ai preluat masa ${call.tableNumber} și chemarea ei.`
          : 'Ai preluat masa și chemarea ei.',
      )
    } catch {
      toast.error('Masa este a ta, dar chemarea nu s-a putut accepta — încearcă din nou.')
    }
    router.push({ name: 'waiter-table', params: { tableId: session.tableId } })
  } catch (err) {
    // 409: the Romanian message already names the colleague who claimed first;
    // the session event re-seeds the queue, moving the call to their phone.
    toast.error(err instanceof Error ? err.message : 'Masa nu a putut fi preluată.')
    claimSheet.value = null
  } finally {
    isClaimingCall.value = false
  }
}
</script>

<template>
  <div>
    <header class="calls-head">
      <div class="min-w-0">
        <div class="eyebrow">Sala</div>
        <h1 class="calls-title">Apeluri ospătar</h1>
      </div>
      <div v-if="store.count" class="calls-count" aria-live="polite" aria-label="Chemări active">
        {{ store.count }}
      </div>
    </header>

    <!-- ── Chemările mele — the caller's own claimed tables. Always visible,
         so the split reads even with an empty queue. ─────────────────────── -->
    <section class="calls-section">
      <h2 class="section-head section-head--mine">
        Chemările mele
        <span v-if="myCalls.length" class="section-count">{{ myCalls.length }}</span>
      </h2>
      <p v-if="!myCalls.length" class="section-empty">
        Nicio chemare de la mesele tale.
      </p>
      <ul v-else class="calls-list">
        <li v-for="c in myCalls" :key="c.requestId" class="card-white call-card call-card--mine">
          <div class="call-meta">
            <span class="call-eyebrow">Masa ta</span>
            <span class="call-table">{{ c.tableNumber || '—' }}</span>
            <span class="call-ago">{{ ago(c.createdAt) }}</span>
          </div>
          <button
            type="button"
            class="call-accept"
            :disabled="accepting.has(c.requestId)"
            @click="accept(c.requestId)"
          >
            {{ accepting.has(c.requestId) ? 'Se acceptă…' : 'Acceptă' }}
          </button>
        </li>
      </ul>
    </section>

    <!-- ── Chemări fără ospătar — unclaimed tables, anyone can take them ──── -->
    <section class="calls-section">
      <h2 class="section-head">
        Chemări fără ospătar
        <span v-if="unassignedCalls.length" class="section-count">{{ unassignedCalls.length }}</span>
      </h2>
      <p class="section-hint">De la mese nepreluate — cine acceptă, preia masa.</p>
      <p v-if="!unassignedCalls.length" class="section-empty">
        Nicio chemare de la mese nepreluate.
      </p>
      <ul v-else class="calls-list">
        <li v-for="c in unassignedCalls" :key="c.requestId" class="card-white call-card">
          <div class="call-meta">
            <span class="call-eyebrow">Masa</span>
            <span class="call-table">{{ c.tableNumber || '—' }}</span>
            <span class="call-ago">{{ ago(c.createdAt) }}</span>
          </div>
          <button
            type="button"
            class="call-accept"
            :disabled="isClaimingCall"
            @click="openClaimSheet(c)"
          >
            Acceptă
          </button>
        </li>
      </ul>
    </section>

    <!-- ── La colegi — only managers ever hold these in the store ─────────── -->
    <section v-if="otherCalls.length" class="calls-section">
      <h2 class="section-head">
        La colegi
        <span class="section-count">{{ otherCalls.length }}</span>
      </h2>
      <ul class="calls-list">
        <li v-for="c in otherCalls" :key="c.requestId" class="card-white call-card call-card--other">
          <div class="call-meta">
            <span class="call-eyebrow">Ospătar · {{ c.assignedWaiterName || 'coleg' }}</span>
            <span class="call-table">{{ c.tableNumber || '—' }}</span>
            <span class="call-ago">{{ ago(c.createdAt) }}</span>
          </div>
          <button
            v-if="isManager"
            type="button"
            class="call-accept"
            :disabled="accepting.has(c.requestId)"
            @click="accept(c.requestId)"
          >
            {{ accepting.has(c.requestId) ? 'Se acceptă…' : 'Acceptă' }}
          </button>
        </li>
      </ul>
    </section>

    <!-- Claim-to-accept sheet: an unassigned call is answered by taking the table. -->
    <div v-if="claimSheet" class="sheet-backdrop" @click="closeClaimSheet">
      <div
        class="sheet"
        role="dialog"
        aria-modal="true"
        :aria-label="`Masa ${claimSheet.tableNumber ?? ''}`"
        @click.stop
      >
        <div class="sheet-grip" aria-hidden="true"></div>
        <div class="eyebrow">Chemare fără ospătar</div>
        <h2 class="sheet-title">
          Preia masa {{ claimSheet.tableNumber ?? '—' }} ca să accepți
        </h2>
        <p class="sheet-text">
          Masa nu are încă un ospătar. Ca să accepți chemarea, masa
          {{ claimSheet.tableNumber ?? '' }} îți va fi atribuită ție, până la
          închiderea notei — iar comenzile ei (inclusiv cele de la QR) intră pe
          nota ta.
        </p>
        <div class="sheet-actions">
          <button
            type="button"
            class="btn-ghost"
            :disabled="isClaimingCall"
            @click="closeClaimSheet"
          >
            Renunță
          </button>
          <button
            type="button"
            class="pill-btn"
            :disabled="isClaimingCall"
            @click="confirmClaimAndAccept"
          >
            {{ isClaimingCall ? 'Se preia…' : 'Acceptă și preia masa' }}
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.calls-head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 12px;
  margin-bottom: 14px;
}
.calls-title {
  font-size: 22px;
  font-weight: 800;
  line-height: 1.15;
  color: var(--ink);
}
.calls-count {
  min-width: 46px;
  height: 46px;
  padding: 0 12px;
  border-radius: 15px;
  background: var(--accent);
  color: var(--onAccent);
  display: grid;
  place-items: center;
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 22px;
  font-weight: 800;
  box-shadow: 0 12px 28px -12px rgba(255, 74, 28, 0.7);
}

/* ── Sections ────────────────────────────────────────────────────────────── */
.section-empty {
  padding: 18px 12px;
  text-align: center;
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 11px;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  color: var(--ink40);
  border: 1px dashed var(--ink12);
  border-radius: 14px;
}
.calls-section { margin-bottom: 20px; }
.section-head {
  display: flex;
  align-items: center;
  gap: 8px;
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 11.5px;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.07em;
  color: var(--ink70);
  margin-bottom: 8px;
}
.section-head--mine { color: var(--accentDk); }
.section-count {
  min-width: 20px;
  height: 20px;
  padding: 0 6px;
  border-radius: 999px;
  background: var(--ink06);
  color: var(--ink70);
  display: inline-grid;
  place-items: center;
  font-size: 11px;
  font-weight: 800;
}
.section-head--mine .section-count {
  background: var(--accent);
  color: var(--onAccent);
}
.section-hint {
  font-size: 12.5px;
  color: var(--ink40);
  margin: -4px 0 8px;
}

.calls-list {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 10px;
}
.call-card {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 14px;
  padding: 14px;
  border: 1px solid var(--ink12);
}
.call-card--mine {
  border-color: var(--accent);
  box-shadow: 0 0 0 3px color-mix(in srgb, var(--accent) 18%, transparent);
}
.call-card--other { opacity: 0.75; }
.call-meta {
  display: grid;
  gap: 1px;
  min-width: 0;
}
.call-eyebrow {
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 10px;
  letter-spacing: 0.1em;
  text-transform: uppercase;
  color: var(--ink70);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.call-card--mine .call-eyebrow { color: var(--accentDk); }
.call-table {
  font-size: 32px;
  font-weight: 800;
  line-height: 1;
  color: var(--ink);
}
.call-ago {
  margin-top: 4px;
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 11px;
  color: var(--ink40);
}
.call-accept {
  flex-shrink: 0;
  min-height: var(--tap-min);
  padding: 0 22px;
  border-radius: 999px;
  border: 1px solid var(--ink);
  background: var(--ink);
  color: var(--paperHi);
  font-family: inherit;
  font-size: 15px;
  font-weight: 700;
  cursor: pointer;
  transition: transform 0.12s ease, opacity 0.15s ease, background 0.15s ease;
}
.call-accept:active { transform: scale(0.97); }
.call-accept:disabled {
  opacity: 0.6;
  cursor: default;
}

/* ── Claim-to-accept sheet ───────────────────────────────────────────────── */
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
.sheet-actions {
  display: flex;
  gap: 10px;
}
.sheet-actions > * {
  flex: 1;
  min-height: var(--tap-min);
}
</style>
