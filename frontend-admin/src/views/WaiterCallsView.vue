<script setup lang="ts">
import { ref, computed, onMounted, onBeforeUnmount } from 'vue'
import { useWaiterCallsStore } from '@/stores/useWaiterCallsStore'

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
  const hrs = Math.floor(mins / 60)
  return `acum ${hrs} h`
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
  <div class="max-w-5xl mx-auto">
    <header class="mb-6 flex items-end justify-between gap-4">
      <div>
        <div class="eyebrow mb-2">SALĂ</div>
        <h1 class="display-title text-[34px] leading-[0.95]">
          Chemări <span class="text-accent">ospătar.</span>
        </h1>
        <p class="subtitle mt-2">Mesele care cer un ospătar apar instant aici.</p>
      </div>
      <div v-if="store.count" class="calls-count" aria-live="polite" aria-label="Chemări active">
        {{ store.count }}
      </div>
    </header>

    <div v-if="!calls.length" class="empty-card">
      <div class="empty-mark" aria-hidden="true">✓</div>
      <p class="empty-title">Nicio chemare activă</p>
      <p class="empty-text">Când o masă cheamă un ospătar, vei vedea aici masa și ora.</p>
    </div>

    <ul v-else class="calls-grid">
      <li v-for="c in calls" :key="c.requestId" class="call-card">
        <div class="call-meta">
          <div class="call-label">MASA</div>
          <div class="call-table">{{ c.tableNumber || '—' }}</div>
          <div class="call-ago">{{ ago(c.createdAt) }}</div>
        </div>
        <button
          type="button"
          class="accept-btn"
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
.subtitle {
  color: var(--ink70, var(--ink));
  opacity: 0.7;
  font-size: 14px;
}

.calls-count {
  min-width: 56px;
  height: 56px;
  padding: 0 14px;
  border-radius: 18px;
  background: var(--accent);
  color: var(--onAccent);
  display: grid;
  place-items: center;
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 28px;
  font-weight: 800;
  box-shadow: 0 12px 28px -12px rgba(255, 74, 28, 0.7);
}

.empty-card {
  margin-top: 8px;
  padding: 48px 24px;
  border-radius: 24px;
  background: var(--card, var(--paperHi));
  border: 1px dashed var(--ink12);
  display: grid;
  place-items: center;
  text-align: center;
  gap: 6px;
}
.empty-mark {
  width: 56px;
  height: 56px;
  border-radius: 50%;
  display: grid;
  place-items: center;
  background: var(--lime);
  color: var(--onLime, #15140f);
  font-size: 26px;
  font-weight: 800;
  margin-bottom: 8px;
}
.empty-title {
  font-size: 20px;
  font-weight: 700;
  color: var(--ink);
}
.empty-text {
  font-size: 14px;
  color: var(--ink70, var(--ink));
  opacity: 0.7;
  max-width: 32ch;
}

.calls-grid {
  list-style: none;
  margin: 0;
  padding: 0;
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(220px, 1fr));
  gap: 14px;
}
.call-card {
  display: flex;
  flex-direction: column;
  gap: 16px;
  padding: 20px;
  border-radius: 22px;
  background: var(--card, var(--paperHi));
  border: 1px solid var(--ink12);
  box-shadow: var(--shadow-soft);
}
.call-meta {
  display: grid;
  gap: 2px;
}
.call-label {
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 11px;
  letter-spacing: 0.12em;
  color: var(--ink70, var(--ink));
  opacity: 0.7;
}
.call-table {
  font-size: 40px;
  font-weight: 800;
  line-height: 1;
  color: var(--ink);
}
.call-ago {
  margin-top: 6px;
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 12px;
  color: var(--ink70, var(--ink));
  opacity: 0.75;
}
.accept-btn {
  min-height: var(--tap-min, 44px);
  border-radius: 999px;
  border: 1px solid var(--ink);
  background: var(--ink);
  color: var(--paperHi);
  font-family: inherit;
  font-size: 16px;
  font-weight: 700;
  cursor: pointer;
  transition: transform 0.12s ease, opacity 0.15s ease, background 0.15s ease;
}
.accept-btn:hover {
  background: var(--accent);
  border-color: var(--accent);
}
.accept-btn:active {
  transform: scale(0.97);
}
.accept-btn:disabled {
  opacity: 0.6;
  cursor: default;
}
</style>
