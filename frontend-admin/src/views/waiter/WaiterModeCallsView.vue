<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { useWaiterCallsStore } from '@/stores/useWaiterCallsStore'

/**
 * "Apeluri" inside Waiter Mode. Same live queue as the admin "Chemări" page
 * (useWaiterCallsStore owns the StaffHub connection app-wide), but rendered in
 * the waiter shell — before this view, the tab jumped into the admin console
 * and dumped the waiter out of Mod ospătar.
 */
const store = useWaiterCallsStore()

// Live "time ago" — a coarse 15s tick is plenty for minute-level labels.
const now = ref(Date.now())
let tick: number | null = null
onMounted(() => {
  tick = window.setInterval(() => (now.value = Date.now()), 15000)
})
onBeforeUnmount(() => {
  if (tick) window.clearInterval(tick)
})

// Oldest first — the table that has been waiting longest sits on top.
const calls = computed(() =>
  [...store.calls].sort(
    (a, b) => new Date(a.createdAt).getTime() - new Date(b.createdAt).getTime(),
  ),
)

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

    <div v-if="!calls.length" class="calls-empty">
      <div class="calls-empty-mark" aria-hidden="true">✓</div>
      <p class="calls-empty-title">Nicio chemare activă</p>
      <p class="calls-empty-text">Când o masă cheamă un ospătar, apare instant aici.</p>
    </div>

    <ul v-else class="calls-list">
      <li v-for="c in calls" :key="c.requestId" class="card-white call-card">
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
  border: 1px solid var(--accentBorder);
}
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
}
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
