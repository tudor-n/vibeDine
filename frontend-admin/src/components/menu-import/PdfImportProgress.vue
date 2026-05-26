<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'

// ── Props & emits ─────────────────────────────────────────────────────────────

interface Job {
  id: string
  status: 'uploading' | 'pending' | 'extracting' | 'complete' | 'failed' | 'cancelled'
  phase: 'parsing' | 'classifying' | 'extracting' | 'finalizing'
  fileName: string
  fileSizeBytes: number
  processedPages: number
  totalPages: number
  failedPages: number
  etaSeconds?: number
  errorMessage?: string
  // Anchored on the store; survives navigation so the time-based curve doesn't restart.
  startedAt: number
}

const props = defineProps<{ job: Job }>()
const emit = defineEmits<{ (e: 'cancel'): void }>()

// ── Derived state ─────────────────────────────────────────────────────────────

const showCancel = computed(() =>
  props.job.status !== 'uploading' &&
  props.job.status !== 'complete' &&
  props.job.status !== 'failed' &&
  props.job.status !== 'cancelled',
)

const isUploading = computed(() => props.job.status === 'uploading')
const isFailed   = computed(() => props.job.status === 'failed')
const isComplete = computed(() => props.job.status === 'complete')

// File size: <1 MB → "X KB", else "X.X MB"
const fileSizeLabel = computed(() => {
  const bytes = props.job.fileSizeBytes
  const kb = bytes / 1024
  if (kb < 1024) return `${Math.round(kb)} KB`
  return `${(kb / 1024).toFixed(1)} MB`
})

// Real progress — used as the floor; the displayed bar can creep ahead of it
// while we wait for the next real update, but never below it.
const realProgressPct = computed(() => {
  if (!props.job.totalPages) return 0
  return Math.min(100, (props.job.processedPages / props.job.totalPages) * 100)
})

// Displayed progress: smoothly animated, with creep between real updates so the
// bar never looks stalled. Snaps to 100% on completion, stays at 0 on failure.
const displayedPct = ref(0)
let creepTimer: ReturnType<typeof setInterval> | null = null

// Cost model for PDF imports: parsing + OCR + AI extraction is dominated by
// page count, but at the very start we only know file size. Rough rule of
// thumb on commodity hardware: ~8s per MB end-to-end, clamped to a sane window.
function estimatePdfDurationMs(): number {
  const mb = props.job.fileSizeBytes / 1_000_000
  const est = 6000 + mb * 8000
  return Math.max(8000, Math.min(120_000, est))
}

// Same ease-out cubic + asymptote curve used by the photo scanner — fills
// confidently to ~80% in the estimated window, then creeps toward 92%.
function timeBasedTarget(elapsedMs: number, estimatedMs: number): number {
  if (elapsedMs <= estimatedMs) {
    const t = elapsedMs / estimatedMs
    return 80 * (1 - Math.pow(1 - t, 3))
  }
  const over = elapsedMs - estimatedMs
  const decay = Math.max(3000, estimatedMs * 0.6)
  return 80 + 12 * (1 - Math.exp(-over / decay))
}

function isJobActive(): boolean {
  const s = props.job.status
  return s !== 'complete' && s !== 'failed' && s !== 'cancelled'
}

function stopCreep() {
  if (creepTimer) {
    clearInterval(creepTimer)
    creepTimer = null
  }
}

function startCreep() {
  stopCreep()
  // Anchor the curve to the store-owned startedAt so re-mounting (e.g. after
  // navigating away and back) resumes at the right position instead of restarting.
  const estimated = estimatePdfDurationMs()
  const tick = () => {
    if (props.job.status === 'complete') {
      displayedPct.value = 100
      stopCreep()
      return
    }
    if (props.job.status === 'failed' || props.job.status === 'cancelled') {
      stopCreep()
      return
    }
    const real = realProgressPct.value
    const elapsed = Date.now() - props.job.startedAt
    const timeTarget = timeBasedTarget(elapsed, estimated)
    // Never fall below real backend progress; never exceed real + 8% once real
    // data lands. Time-based target acts as a floor while real is still 0.
    const ceiling = props.job.totalPages
      ? Math.min(96, Math.max(real + 8, timeTarget))
      : Math.min(96, timeTarget)
    const next = Math.max(real, Math.min(ceiling, timeTarget))
    const jitter = (Math.random() - 0.5) * 0.4
    displayedPct.value = Math.max(displayedPct.value, next + jitter)
  }
  // Compute once immediately so a remount snaps the bar to the right spot.
  tick()
  creepTimer = setInterval(tick, 150)
}

// React to real updates: snap floor up immediately (never let the bar look behind),
// and ensure the creep loop is running while the job is active.
watch(
  () => [props.job.status, realProgressPct.value] as const,
  ([status, real]) => {
    if (status === 'complete') {
      displayedPct.value = 100
      stopCreep()
      return
    }
    if (status === 'failed' || status === 'cancelled') {
      stopCreep()
      return
    }
    if (real > displayedPct.value) displayedPct.value = real
    if (!creepTimer) startCreep()
  },
  { immediate: true },
)

onMounted(() => {
  // If we navigated back into the import page while the PDF is still running,
  // the watcher above won't fire (no status change), so kick off the creep loop manually.
  if (isJobActive() && !creepTimer) startCreep()
})

onBeforeUnmount(() => stopCreep())

const progressPct = computed(() => displayedPct.value)

const progressCounter = computed(() => {
  if (!props.job.totalPages) return `${Math.round(displayedPct.value)}%`
  return `${props.job.processedPages} / ${props.job.totalPages}`
})

// Phase translation
const phaseLabel = computed(() => {
  const map: Record<Job['phase'], string> = {
    parsing:     'Citesc paginile',
    classifying: 'Sortez paginile',
    extracting:  'Extrag preparate',
    finalizing:  'Finalizez',
  }
  return map[props.job.phase]
})

// ETA formatting
const etaLabel = computed(() => {
  const s = props.job.etaSeconds
  if (s === undefined || s === null) return '—'
  if (s < 60) return `~${Math.round(s)}s`
  const m = Math.floor(s / 60)
  const r = Math.round(s % 60)
  return `~${m}m ${r}s`
})

const fileMeta = computed(() => {
  const pageText = props.job.totalPages > 0 ? `${props.job.totalPages} pagini` : 'se pregătește'
  return `${fileSizeLabel.value} · ${pageText}`
})

// Status line
const statusLine = computed(() => {
  if (isUploading.value) return 'Se încarcă PDF-ul și pornesc importul.'
  if (isComplete.value) {
    const ok = props.job.totalPages - props.job.failedPages
    return `Import finalizat · ${ok} pagini procesate.`
  }
  if (!props.job.totalPages) return 'Importul este în coadă · aștept primele pagini.'
  return `Faza curentă: ${phaseLabel.value} · ETA ${etaLabel.value}`
})

// ── Dot grid ─────────────────────────────────────────────────────────────────

type DotState = 'done' | 'inProgress' | 'pending' | 'failed'

const dots = computed<DotState[]>(() => {
  const total   = props.job.totalPages
  const done    = props.job.processedPages
  const failed  = props.job.failedPages
  const inFlight = Math.min(8, total - done)

  // Indices of failed pages: last `failedPages` in the done range
  const failedStart = done - failed

  return Array.from({ length: total }, (_, i) => {
    if (i >= failedStart && i < done && failed > 0) return 'failed'
    if (i < done) return 'done'
    if (i < done + inFlight) return 'inProgress'
    return 'pending'
  })
})
</script>

<template>
  <article class="pdf-progress" role="region" aria-label="Progres import PDF">

    <!-- File header -->
    <div class="header-row">
      <div class="file-icon" aria-hidden="true">PDF</div>
      <div class="file-section">
        <div class="file-name">{{ job.fileName }}</div>
        <div class="file-meta">{{ fileMeta }}</div>
      </div>
      <button
        v-if="showCancel"
        type="button"
        class="btn-danger cancel-btn"
        data-testid="cancel-btn"
        aria-label="Anulează importul"
        @click="emit('cancel')"
      >
        Anulează
      </button>
    </div>

    <!-- Progress bar -->
    <div class="progress-section">
      <div class="progress-head">
        <span class="field-label">{{ statusLine }}</span>
        <span class="progress-counter">{{ progressCounter }}</span>
      </div>
      <div class="progress-track" role="progressbar"
           :aria-valuenow="Math.round(progressPct)"
           :aria-valuemin="0"
           :aria-valuemax="100"
           :aria-label="job.totalPages ? `${job.processedPages} din ${job.totalPages} pagini procesate` : 'Import PDF în curs de pornire'">
        <div class="progress-fill" :style="{ width: progressPct + '%' }"></div>
      </div>
    </div>

    <!-- Error state -->
    <div v-if="isFailed" class="alert alert-error" role="alert">
      <span class="alert-icon" aria-hidden="true">⚠</span>
      <div class="alert-body">
        <div class="alert-title">Eroare</div>
        <p class="error-message">{{ job.errorMessage }}</p>
      </div>
    </div>

    <!-- Dot grid (compact, only when reasonable) -->
    <details v-else-if="job.totalPages > 0 && job.totalPages <= 80" class="page-details">
      <summary class="page-details-summary">
        <span class="field-label">Pagini · {{ job.processedPages }}/{{ job.totalPages }}</span>
      </summary>
      <div class="dot-grid" aria-hidden="true">
        <span
          v-for="(state, idx) in dots"
          :key="idx"
          class="dot"
          :class="`dot-${state}`"
          data-testid="page-dot"
        ></span>
      </div>
    </details>

  </article>
</template>

<style scoped>
.pdf-progress {
  padding: 20px;
  border: 1px solid var(--ink12);
  border-radius: 16px;
  background: var(--paperHi);
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.header-row {
  display: grid;
  grid-template-columns: auto 1fr auto;
  align-items: center;
  gap: 14px;
}

.file-icon {
  width: 44px;
  height: 44px;
  border-radius: 10px;
  background: var(--ink06);
  border: 1px solid var(--ink12);
  display: grid;
  place-items: center;
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 11px;
  font-weight: 700;
  color: var(--ink70);
  letter-spacing: 0.1em;
}

.file-section { min-width: 0; }
.file-name {
  font-family: 'Inter Tight', sans-serif;
  font-weight: 600;
  font-size: 16px;
  letter-spacing: -0.01em;
  color: var(--ink);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.file-meta {
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 11px;
  color: var(--ink40);
  margin-top: 2px;
}

.cancel-btn {
  padding: 8px 14px;
  min-height: 0;
  font-size: 12px;
}

.progress-section {
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.progress-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}
.progress-counter {
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 14px;
  font-weight: 700;
  color: var(--ink);
}
.progress-track {
  height: 8px;
  background: var(--ink06);
  border-radius: 999px;
  overflow: hidden;
}
.progress-fill {
  height: 100%;
  background: var(--accent);
  border-radius: 999px;
  transition: width 250ms cubic-bezier(0.34, 1.4, 0.64, 1);
}

.error-message {
  margin: 4px 0 0;
  font-size: 13px;
  color: var(--accent);
  line-height: 1.45;
}

.page-details {
  margin-top: -4px;
}
.page-details-summary {
  cursor: pointer;
  list-style: none;
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 2px 0;
}
.page-details-summary::-webkit-details-marker { display: none; }
.page-details-summary::before {
  content: '▸';
  font-size: 10px;
  color: var(--ink40);
  transition: transform 0.15s ease;
}
.page-details[open] .page-details-summary::before { transform: rotate(90deg); }

.dot-grid {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
  margin-top: 10px;
}
.dot {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  flex-shrink: 0;
}
.dot-done { background: var(--accent); }
.dot-inProgress {
  background: var(--accent);
  animation: dot-pulse 1.4s ease-in-out infinite;
}
.dot-pending { background: var(--ink12); }
.dot-failed { background: var(--accentBorder); border: 1px solid var(--accent); }

@keyframes dot-pulse {
  0%, 100% { opacity: 0.4; transform: scale(0.85); }
  50%      { opacity: 1;   transform: scale(1.15); }
}

@media (prefers-reduced-motion: reduce) {
  .progress-fill { transition: none; }
  .dot-inProgress { animation: none; opacity: 0.8; }
}
</style>
