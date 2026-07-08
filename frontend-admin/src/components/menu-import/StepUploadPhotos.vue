<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'

import { useMenuImportStore } from '@/stores/useMenuImportStore'
import { MENU_VALIDATION } from '@shared/types/menu-validation'

const store = useMenuImportStore()
const dragOver = ref(false)
const fileInput = ref<HTMLInputElement | null>(null)
const lastRejection = ref<string | null>(null)
const PDF_MAX_FILE_BYTES = 110_000_000

const scanProgress = ref(0)
const scanPhases = [
    'pregătesc fotografiile',
    'analizez compoziția',
    'extrag denumiri și prețuri',
    'recunosc categorii',
    'finalizez',
]
let progressTimer: ReturnType<typeof setInterval> | null = null

// Cost model calibrated to actual AI scan times (~2-4s per photo on commodity
// hardware). Keep it on the aggressive side so the curve always finishes near
// the 90% mark instead of stalling at 30%.
function estimateScanDurationMs(): number {
    if (store.files.length === 0) return 4000
    const total = store.files.reduce(
        (sum, f) => sum + 1800 + (f.size / 1_000_000) * 200,
        2000, // baseline latency for the API round-trip
    )
    return Math.max(3000, Math.min(20_000, total))
}

// Aggressive ease-out cubic that reaches ~90% by the estimated duration, then
// asymptotes toward 95%. Matches the PDF feel — when the real work completes,
// the jump to 100% is small (5-10%) rather than a 30→100 leap.
function targetProgress(elapsedMs: number, estimatedMs: number): number {
    if (elapsedMs <= estimatedMs) {
        const t = elapsedMs / estimatedMs
        return 90 * (1 - Math.pow(1 - t, 3))
    }
    const over = elapsedMs - estimatedMs
    const decay = Math.max(1500, estimatedMs * 0.5)
    return 90 + 5 * (1 - Math.exp(-over / decay))
}

function startFakeProgress() {
    stopFakeProgress()
    // Anchor to the store-owned start time so re-mounting (e.g. after navigating
    // away and back) resumes at the right point on the curve instead of restarting.
    const startedAt = store.scanStartedAt
    if (!startedAt) return
    const estimated = estimateScanDurationMs()
    const tick = () => {
        const elapsed = Date.now() - startedAt
        const target = targetProgress(elapsed, estimated)
        const jitter = (Math.random() - 0.5) * 0.4
        scanProgress.value = Math.min(95, Math.max(scanProgress.value, target + jitter))
    }
    // Compute once immediately so a remount snaps to the correct position before the first interval fires.
    tick()
    progressTimer = setInterval(tick, 120)
}

function stopFakeProgress() {
    if (progressTimer) {
        clearInterval(progressTimer)
        progressTimer = null
    }
}

function completeFakeProgress() {
    stopFakeProgress()
    scanProgress.value = 100
}

function resetFakeProgress() {
    stopFakeProgress()
    scanProgress.value = 0
}

watch(
    () => store.isScanning,
    (now, prev) => {
        if (now && !prev) {
            scanProgress.value = 0
            startFakeProgress()
        } else if (!now && prev) {
            if (store.scanError) {
                resetFakeProgress()
            } else {
                completeFakeProgress()
            }
        }
    },
)

onMounted(() => {
    // If we navigated back into the import page mid-scan, the watcher above
    // won't fire (no change in isScanning), so kick the animation off manually.
    if (store.isScanning && store.scanStartedAt) startFakeProgress()
})

onBeforeUnmount(() => stopFakeProgress())

const progressPct = computed(() => Math.round(scanProgress.value))
const progressLabel = computed(() => `${progressPct.value}%`)
// Drive the phase label off the progress percentage so the wording always
// matches what the user is seeing, regardless of how long the scan actually takes.
const currentPhase = computed(() => {
    const p = scanProgress.value
    const idx =
        p < 20 ? 0 :
        p < 45 ? 1 :
        p < 75 ? 2 :
        p < 95 ? 3 : 4
    return scanPhases[idx]
})

const acceptAttr = computed(() => MENU_VALIDATION.IMPORT_ALLOWED_MIME.join(','))
const photoMaxMb = computed(() => MENU_VALIDATION.IMPORT_MAX_FILE_BYTES / 1024 / 1024)
const pdfMaxMb = computed(() => PDF_MAX_FILE_BYTES / 1_000_000)

function onPickClick() {
  fileInput.value?.click()
}

function isSinglePdf(files: File[]): File | null {
  if (files.length !== 1) return null
  const file = files[0]
  if (file.type === 'application/pdf') return file
  if (file.name.toLowerCase().endsWith('.pdf')) return file
  return null
}

async function onFilesSelected(event: Event) {
  const input = event.target as HTMLInputElement
  if (!input.files) return
  const fileList = Array.from(input.files)
  input.value = ''
  const pdf = isSinglePdf(fileList)
  if (pdf) {
    await routeToPdfFlow(pdf)
    return
  }
  const { rejected } = store.addFiles(fileList)
  reportRejected(rejected)
}

async function onDrop(event: DragEvent) {
  event.preventDefault()
  dragOver.value = false
  if (!event.dataTransfer) return
  const fileList = Array.from(event.dataTransfer.files)
  const pdf = isSinglePdf(fileList)
  if (pdf) {
    await routeToPdfFlow(pdf)
    return
  }
  const { rejected } = store.addFiles(fileList)
  reportRejected(rejected)
}

async function routeToPdfFlow(file: File) {
  lastRejection.value = null
  if (file.size > PDF_MAX_FILE_BYTES) {
    lastRejection.value = `Maxim ${pdfMaxMb.value} MB / PDF.`
    return
  }
  try {
    await store.startPdfImport(file)
  } catch {
    // store.importError is already set by the action
  }
}

function reportRejected(rejected: { reason: string }[]) {
  if (rejected.length === 0) {
    lastRejection.value = null
    return
  }
  const reason = rejected[0].reason
  lastRejection.value =
    reason === 'too_many'
      ? `Limită ${MENU_VALIDATION.IMPORT_MAX_FILES} fotografii.`
      : reason === 'too_large'
        ? `Maxim ${photoMaxMb.value} MB / fotografie.`
        : 'Tipul de fișier nu este suportat (jpeg/png/webp/heic sau PDF unic).'
}

async function startScan() {
  try {
    await store.scan()
  } catch {
    // store.scanError is already set
  }
}

function thumbUrl(file: File): string {
  return URL.createObjectURL(file)
}
</script>

<template>
  <section class="space-y-4">
    <!-- Opt-in BEFORE any file is added: a dropped PDF auto-starts the import,
         so the choice must be made first. -->
    <label class="photo-toggle">
      <input
        type="checkbox"
        data-testid="extract-photos-toggle"
        v-model="store.extractItemPhotos"
      />
      <span class="photo-toggle-text">
        Extrage și fotografiile preparatelor (se aplică și la PDF)
      </span>
    </label>

    <div
      class="dropzone"
      :class="{ 'is-drag': dragOver, 'has-files': store.files.length > 0 }"
      @dragover.prevent="dragOver = true"
      @dragleave="dragOver = false"
      @drop="onDrop"
    >
      <div class="dropzone-icon" aria-hidden="true">⊕</div>
      <div class="dropzone-body">
        <h2 class="section-title">Trage fotografiile sau alege fișiere</h2>
        <p class="dropzone-hint">
          Până la {{ MENU_VALIDATION.IMPORT_MAX_FILES }} fotografii ({{ photoMaxMb }} MB fiecare)
          sau un PDF unic ({{ pdfMaxMb }} MB) · jpeg / png / webp / heic / pdf
        </p>
      </div>
      <button class="btn-ghost dropzone-pick" type="button" @click="onPickClick">
        Alege fișiere
      </button>
      <input
        ref="fileInput"
        type="file"
        :accept="acceptAttr"
        multiple
        class="hidden-input"
        @change="onFilesSelected"
      />
    </div>

    <div v-if="lastRejection" class="alert alert-warning">
      <span class="alert-icon" aria-hidden="true">⚠</span>
      <div class="alert-body">{{ lastRejection }}</div>
    </div>
    <div v-if="store.importError" class="alert alert-error">
      <span class="alert-icon" aria-hidden="true">⚠</span>
      <div class="alert-body">{{ store.importError }}</div>
    </div>

    <div v-if="store.files.length" class="files-block">
      <div class="files-head">
        <span class="field-label">{{ store.files.length }} {{ store.files.length === 1 ? 'fotografie' : 'fotografii' }}</span>
        <button class="text-link" type="button" @click="store.files.forEach(f => store.removeFile(f.name))">
          Șterge tot
        </button>
      </div>
      <div class="thumbs">
        <figure v-for="file in store.files" :key="file.name" class="thumb">
          <img :src="thumbUrl(file)" :alt="file.name" />
          <button
            type="button"
            class="thumb-remove"
            @click="store.removeFile(file.name)"
            :aria-label="`Elimină ${file.name}`"
            title="Elimină"
          >×</button>
          <figcaption class="thumb-name">{{ file.name }}</figcaption>
        </figure>
      </div>
    </div>

    <div v-if="store.scanError" class="alert alert-error">
      <span class="alert-icon" aria-hidden="true">⚠</span>
      <div class="alert-body">{{ store.scanError }}</div>
    </div>

    <div
      v-if="store.isScanning || (scanProgress > 0 && !store.scanError)"
      class="scan-progress"
      role="status"
      aria-live="polite"
    >
      <div class="scan-progress-head">
        <span class="field-label">{{ currentPhase }}</span>
        <span class="scan-progress-counter">{{ progressLabel }}</span>
      </div>
      <div
        class="progress-track"
        role="progressbar"
        :aria-valuenow="progressPct"
        aria-valuemin="0"
        aria-valuemax="100"
      >
        <div class="progress-fill" :style="{ width: progressPct + '%' }"></div>
      </div>
    </div>

    <div class="form-footer form-footer-end">
      <button
        class="pill-btn"
        type="button"
        :disabled="!store.canAdvanceFromStep1"
        @click="startScan"
      >
        <template v-if="store.isScanning">Scanez…</template>
        <template v-else>Scanează meniul</template>
      </button>
    </div>
  </section>
</template>

<style scoped>
.dropzone {
  display: grid;
  grid-template-columns: auto 1fr auto;
  align-items: center;
  gap: 18px;
  padding: 20px 22px;
  border: 1px dashed var(--ink12);
  border-radius: 18px;
  background: var(--paperHi);
  transition: border-color 0.15s ease, background 0.15s ease, transform 0.15s ease;
}
.dropzone.is-drag {
  border-color: var(--ink);
  background: var(--paper);
  border-style: solid;
}
.dropzone.has-files { border-style: solid; }
.dropzone-icon {
  width: 48px;
  height: 48px;
  border-radius: 14px;
  background: var(--ink06);
  display: grid;
  place-items: center;
  font-size: 22px;
  color: var(--ink70);
  flex-shrink: 0;
}
.dropzone-body { min-width: 0; }
.dropzone-hint {
  font-size: 13px;
  color: var(--ink70);
  margin-top: 2px;
  line-height: 1.45;
}
.dropzone-pick { flex-shrink: 0; }

@media (max-width: 640px) {
  .dropzone {
    grid-template-columns: auto 1fr;
    grid-template-areas: 'icon body' 'pick pick';
  }
  .dropzone-icon { grid-area: icon; }
  .dropzone-body { grid-area: body; }
  .dropzone-pick { grid-area: pick; justify-self: stretch; }
}

.hidden-input { display: none; }

.files-block { display: flex; flex-direction: column; gap: 10px; }
.files-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.thumbs {
  display: grid;
  gap: 10px;
  grid-template-columns: repeat(auto-fill, minmax(140px, 1fr));
}
.thumb {
  position: relative;
  border-radius: 12px;
  overflow: hidden;
  background: var(--card);
  border: 1px solid var(--ink12);
  margin: 0;
}
.thumb img {
  display: block;
  width: 100%;
  aspect-ratio: 4 / 3;
  object-fit: cover;
  background: var(--ink06);
}
.thumb-remove {
  position: absolute;
  top: 6px;
  right: 6px;
  border: none;
  background: rgba(21, 20, 15, 0.7);
  color: #fff;
  width: 24px;
  height: 24px;
  border-radius: 50%;
  cursor: pointer;
  font-size: 16px;
  line-height: 1;
  display: grid;
  place-items: center;
  opacity: 0;
  transition: opacity 0.15s ease, transform 0.15s ease;
  backdrop-filter: blur(4px);
}
.thumb:hover .thumb-remove { opacity: 1; }
.thumb-remove:hover { transform: scale(1.08); }
/* Touch devices have no hover — keep the remove button visible and tappable. */
@media (hover: none) {
  .thumb-remove {
    opacity: 1;
    width: 32px;
    height: 32px;
    font-size: 18px;
  }
}
.thumb-name {
  display: block;
  padding: 6px 8px;
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 10px;
  color: var(--ink70);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  border-top: 1px solid var(--ink12);
}

.scan-progress {
  padding: 14px 18px;
  border: 1px solid var(--ink12);
  border-radius: 14px;
  background: var(--paperHi);
  display: flex;
  flex-direction: column;
  gap: 10px;
}
.scan-progress-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}
.scan-progress-counter {
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
  transition: width 300ms cubic-bezier(0.34, 1.4, 0.64, 1);
}
</style>
