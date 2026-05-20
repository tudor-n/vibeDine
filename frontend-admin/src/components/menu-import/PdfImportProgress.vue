<script setup lang="ts">
import { computed } from 'vue'

// ── Props & emits ─────────────────────────────────────────────────────────────

interface Job {
  id: string
  status: 'pending' | 'extracting' | 'complete' | 'failed' | 'cancelled'
  phase: 'parsing' | 'classifying' | 'extracting' | 'finalizing'
  fileName: string
  fileSizeBytes: number
  processedPages: number
  totalPages: number
  failedPages: number
  etaSeconds?: number
  errorMessage?: string
}

const props = defineProps<{ job: Job }>()
const emit = defineEmits<{ (e: 'cancel'): void }>()

// ── Derived state ─────────────────────────────────────────────────────────────

const showCancel = computed(() =>
  props.job.status !== 'complete' &&
  props.job.status !== 'failed' &&
  props.job.status !== 'cancelled',
)

const isFailed   = computed(() => props.job.status === 'failed')
const isComplete = computed(() => props.job.status === 'complete')

// File size: <1 MB → "X KB", else "X.X MB"
const fileSizeLabel = computed(() => {
  const bytes = props.job.fileSizeBytes
  const kb = bytes / 1024
  if (kb < 1024) return `${Math.round(kb)} KB`
  return `${(kb / 1024).toFixed(1)} MB`
})

// Progress bar fill (guard /0)
const progressPct = computed(() => {
  if (!props.job.totalPages) return 0
  return Math.min(100, (props.job.processedPages / props.job.totalPages) * 100)
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

// Status line
const statusLine = computed(() => {
  if (isComplete.value) {
    const ok = props.job.totalPages - props.job.failedPages
    return `Import finalizat · ${ok} pagini procesate.`
  }
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
  <article class="card-paper pdf-progress" role="region" aria-label="Progres import PDF">

    <!-- ── Header row ───────────────────────────────────────────────────── -->
    <div class="header-row">
      <span class="eyebrow">Import Meniu PDF</span>
      <button
        v-if="showCancel"
        type="button"
        class="cancel-btn"
        data-testid="cancel-btn"
        aria-label="Anulează importul"
        @click="emit('cancel')"
      >
        <span aria-hidden="true">✕</span> Anulează
      </button>
    </div>

    <!-- ── File meta ────────────────────────────────────────────────────── -->
    <div class="file-section">
      <div class="display-title file-name">{{ job.fileName }}</div>
      <div class="file-meta">{{ fileSizeLabel }} · {{ job.totalPages }} pagini</div>
    </div>

    <!-- ── Progress bar ─────────────────────────────────────────────────── -->
    <div class="progress-row">
      <div class="progress-track" role="progressbar"
           :aria-valuenow="job.processedPages"
           :aria-valuemin="0"
           :aria-valuemax="job.totalPages"
           :aria-label="`${job.processedPages} din ${job.totalPages} pagini procesate`">
        <div class="progress-fill" :style="{ width: progressPct + '%' }"></div>
      </div>
      <span class="progress-counter">{{ job.processedPages }} / {{ job.totalPages }}</span>
    </div>

    <!-- ── Phase / ETA line ─────────────────────────────────────────────── -->
    <div class="phase-line eyebrow">{{ statusLine }}</div>

    <!-- ── Error state ──────────────────────────────────────────────────── -->
    <div v-if="isFailed" class="error-box" role="alert">
      <span class="eyebrow error-label">⚠ Eroare</span>
      <p class="error-message">{{ job.errorMessage }}</p>
    </div>

    <!-- ── Dot grid ─────────────────────────────────────────────────────── -->
    <template v-else>
      <div class="section-label eyebrow">Pagini</div>
      <div class="dot-grid" aria-hidden="true">
        <span
          v-for="(state, idx) in dots"
          :key="idx"
          class="dot"
          :class="`dot-${state}`"
          data-testid="page-dot"
        ></span>
      </div>

      <!-- Legend -->
      <div class="legend" aria-hidden="true">
        <span class="legend-item">
          <span class="dot dot-done legend-dot"></span> gata
        </span>
        <span class="legend-item">
          <span class="dot dot-inProgress legend-dot"></span> în lucru
        </span>
        <span class="legend-item">
          <span class="dot dot-pending legend-dot"></span> în așteptare
        </span>
        <span class="legend-item">
          <span class="dot dot-failed legend-dot"></span> eșuat
        </span>
      </div>
    </template>

  </article>
</template>

<style scoped>
/* ── Card container ──────────────────────────────────────────────────────── */
.pdf-progress {
  max-width: 640px;
  margin: 0 auto;
  padding: 28px;
  display: flex;
  flex-direction: column;
  gap: 18px;
}

/* ── Header ──────────────────────────────────────────────────────────────── */
.header-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}

/* Cancel button — mirrors .pill-btn-ink but compact */
.cancel-btn {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 8px 14px;
  border-radius: 999px;
  border: none;
  background: var(--ink);
  color: var(--paperHi);
  font-family: 'JetBrains Mono', ui-monospace, Menlo, monospace;
  font-size: 12px;
  font-weight: 700;
  letter-spacing: 0.04em;
  cursor: pointer;
  transition: transform 0.15s ease, opacity 0.15s ease;
  flex-shrink: 0;
}
.cancel-btn:hover  { transform: translateY(-1px); }
.cancel-btn:active { transform: scale(0.97); }
.cancel-btn:focus-visible {
  outline: var(--ring-width) solid var(--accent);
  outline-offset: var(--ring-offset);
}

/* ── File meta ───────────────────────────────────────────────────────────── */
.file-section { display: flex; flex-direction: column; gap: 4px; }

.file-name {
  font-size: 22px;
  letter-spacing: -0.03em;
  line-height: 1.15;
  word-break: break-all;
}

.file-meta {
  font-family: 'JetBrains Mono', ui-monospace, Menlo, monospace;
  font-size: 13px;
  color: var(--ink40);
}

/* ── Progress bar ────────────────────────────────────────────────────────── */
.progress-row {
  display: flex;
  align-items: center;
  gap: 14px;
}

.progress-track {
  flex: 1;
  height: 10px;
  background: var(--paperHi);
  border: 1px solid var(--ink12);
  border-radius: 999px;
  overflow: hidden;
}

.progress-fill {
  height: 100%;
  background: var(--accent);
  border-radius: 999px;
  transition: width 250ms cubic-bezier(0.34, 1.4, 0.64, 1);
  transform-origin: left center;
}

.progress-counter {
  font-family: 'JetBrains Mono', ui-monospace, Menlo, monospace;
  font-size: 14px;
  font-weight: 600;
  color: var(--ink70);
  white-space: nowrap;
  flex-shrink: 0;
}

/* ── Phase line ──────────────────────────────────────────────────────────── */
.phase-line {
  color: var(--ink70);
  font-size: 12px;
}

/* ── Error box ───────────────────────────────────────────────────────────── */
.error-box {
  padding: 16px 18px;
  border-radius: 14px;
  background: var(--accentSoft);
  border: 1px solid var(--accentBorder);
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.error-label {
  color: var(--accent);
  font-size: 11px;
}

.error-message {
  margin: 0;
  font-size: 14px;
  color: var(--accent);
  line-height: 1.45;
}

/* ── Section label ───────────────────────────────────────────────────────── */
.section-label {
  color: var(--ink40);
  font-size: 11px;
  margin-bottom: -8px; /* tighten gap to grid */
}

/* ── Dot grid ────────────────────────────────────────────────────────────── */
.dot-grid {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  display: block;
  flex-shrink: 0;
}

.dot-done {
  background: var(--accent);
}

.dot-inProgress {
  background: radial-gradient(circle at 35% 50%, var(--accent) 45%, var(--paperHi) 46%);
  border: 1px solid var(--accentBorder);
  animation: pulse-soft 1.6s ease-in-out infinite;
}

.dot-pending {
  background: var(--paperHi);
  border: 1px solid var(--ink12);
}

.dot-failed {
  background: var(--accentBorder);
  border: 1px solid var(--accent);
}

/* ── In-progress pulse ───────────────────────────────────────────────────── */
@keyframes pulse-soft {
  0%, 100% { opacity: 0.6; }
  50%       { opacity: 1.0; }
}

/* ── Legend ──────────────────────────────────────────────────────────────── */
.legend {
  display: flex;
  flex-wrap: wrap;
  gap: 12px 20px;
}

.legend-item {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  font-family: 'JetBrains Mono', ui-monospace, Menlo, monospace;
  font-size: 11px;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.06em;
  color: var(--ink40);
}

.legend-dot {
  width: 8px;
  height: 8px;
  /* override animation in legend — static */
  animation: none !important;
}

/* ── Reduced motion ──────────────────────────────────────────────────────── */
@media (prefers-reduced-motion: reduce) {
  .progress-fill {
    transition: none;
  }
  .dot-inProgress {
    animation: none;
    opacity: 0.8;
  }
}
</style>
