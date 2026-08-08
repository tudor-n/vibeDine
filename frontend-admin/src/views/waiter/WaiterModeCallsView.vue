<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import type { WaiterCall } from '@/services/api-client'
import { useAuthStore } from '@/stores/useAuthStore'
import { useWaiterCallsStore } from '@/stores/useWaiterCallsStore'

/**
 * "Apeluri" inside Waiter Mode, split by table ownership:
 *
 *  - "Chemările mele" — calls from tables the caller has claimed. Routed: only
 *    the owning waiter's phone holds (and chimes for) these.
 *  - "Chemări fără ospătar" — calls from unclaimed tables; anyone can accept.
 *  - "La colegi" — calls owned by other waiters. Waiters never receive these
 *    (the store and the API both filter), so the section renders for managers.
 *
 * The store owns the StaffHub connection app-wide and re-seeds itself on every
 * session change, so a claim/transfer/close moves calls between phones live.
 */
const store = useWaiterCallsStore()
const auth = useAuthStore()

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

    <div v-if="!store.count" class="calls-empty">
      <div class="calls-empty-mark" aria-hidden="true">✓</div>
      <p class="calls-empty-title">Nicio chemare activă</p>
      <p class="calls-empty-text">Când o masă cheamă un ospătar, apare instant aici.</p>
    </div>

    <template v-else>
      <!-- ── Chemările mele — the caller's own claimed tables ─────────────── -->
      <section v-if="myCalls.length" class="calls-section">
        <h2 class="section-head section-head--mine">
          Chemările mele
          <span class="section-count">{{ myCalls.length }}</span>
        </h2>
        <ul class="calls-list">
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

      <!-- ── Chemări fără ospătar — unclaimed tables, anyone can take them ── -->
      <section v-if="unassignedCalls.length" class="calls-section">
        <h2 class="section-head">
          Chemări fără ospătar
          <span class="section-count">{{ unassignedCalls.length }}</span>
        </h2>
        <p class="section-hint">De la mese nepreluate — oricine poate accepta.</p>
        <ul class="calls-list">
          <li v-for="c in unassignedCalls" :key="c.requestId" class="card-white call-card">
            <div class="call-meta">
              <span class="call-eyebrow">Masa</span>
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

      <!-- ── La colegi — only managers ever hold these in the store ───────── -->
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
    </template>
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

.calls-empty {
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
.calls-empty-mark {
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
.calls-empty-title {
  font-size: 18px;
  font-weight: 700;
  color: var(--ink);
}
.calls-empty-text {
  font-size: 13.5px;
  color: var(--ink70);
  max-width: 30ch;
}

/* ── Sections ────────────────────────────────────────────────────────────── */
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
</style>
