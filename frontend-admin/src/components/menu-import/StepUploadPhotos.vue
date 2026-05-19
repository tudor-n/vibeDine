<script setup lang="ts">
import { computed, ref } from 'vue'

import { useMenuImportStore } from '@/stores/useMenuImportStore'
import { MENU_VALIDATION } from '@shared/types/menu-validation'

const store = useMenuImportStore()
const dragOver = ref(false)
const fileInput = ref<HTMLInputElement | null>(null)
const lastRejection = ref<string | null>(null)

const acceptAttr = computed(() => MENU_VALIDATION.IMPORT_ALLOWED_MIME.join(','))

function onPickClick() {
  fileInput.value?.click()
}

function onFilesSelected(event: Event) {
  const input = event.target as HTMLInputElement
  if (!input.files) return
  const { rejected } = store.addFiles(Array.from(input.files))
  reportRejected(rejected)
  input.value = ''
}

function onDrop(event: DragEvent) {
  event.preventDefault()
  dragOver.value = false
  if (!event.dataTransfer) return
  const { rejected } = store.addFiles(Array.from(event.dataTransfer.files))
  reportRejected(rejected)
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
        ? `Maxim ${MENU_VALIDATION.IMPORT_MAX_FILE_BYTES / 1024 / 1024} MB / fotografie.`
        : 'Tipul de fișier nu este suportat (jpeg/png/webp/heic).'
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
  <section>
    <div
      class="dropzone card-paper"
      :class="{ 'is-drag': dragOver }"
      @dragover.prevent="dragOver = true"
      @dragleave="dragOver = false"
      @drop="onDrop"
    >
      <div class="eyebrow mb-3">pasul 1 · încărcare</div>
      <h2 class="display-title text-2xl mb-2">Trage fotografiile aici</h2>
      <p class="text-ink-70 text-sm mb-4">
        Maxim {{ MENU_VALIDATION.IMPORT_MAX_FILES }} pagini ·
        {{ MENU_VALIDATION.IMPORT_MAX_FILE_BYTES / 1024 / 1024 }} MB pe fotografie ·
        jpeg / png / webp / heic
      </p>
      <button class="pill-btn pill-btn-ghost" type="button" @click="onPickClick">
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

    <p v-if="lastRejection" class="reject-note">⚠ {{ lastRejection }}</p>

    <div v-if="store.files.length" class="thumbs">
      <figure v-for="file in store.files" :key="file.name" class="thumb card-white">
        <img :src="thumbUrl(file)" :alt="file.name" />
        <figcaption>
          <span class="thumb-name">{{ file.name }}</span>
          <button type="button" class="thumb-remove" @click="store.removeFile(file.name)" aria-label="Elimină">
            ×
          </button>
        </figcaption>
      </figure>
    </div>

    <div v-if="store.scanError" class="card-paper p-4 mt-6" style="border-color: var(--accentBorder);">
      <span class="font-mono text-xs uppercase tracking-[0.18em] text-accent font-bold">
        ⚠ {{ store.scanError }}
      </span>
    </div>

    <div class="footer-row">
      <button
        class="pill-btn"
        type="button"
        :disabled="!store.canAdvanceFromStep1"
        @click="startScan"
      >
        <template v-if="store.isScanning">Scanez {{ store.files.length }} fotografii…</template>
        <template v-else>Scanează meniul</template>
      </button>
    </div>
  </section>
</template>

<style scoped>
.dropzone {
  padding: 32px;
  text-align: left;
  transition: border-color 0.15s ease, background 0.15s ease;
}
.dropzone.is-drag {
  border-color: var(--ink);
  background: var(--paperHi);
}
.hidden-input { display: none; }
.thumbs {
  display: grid;
  gap: 12px;
  grid-template-columns: repeat(auto-fill, minmax(160px, 1fr));
  margin-top: 24px;
}
.thumb {
  padding: 8px;
  position: relative;
}
.thumb img {
  width: 100%;
  height: 110px;
  object-fit: cover;
  border-radius: 8px;
  background: var(--ink06);
}
.thumb figcaption {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-top: 6px;
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 11px;
  color: var(--ink70);
}
.thumb-name { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; max-width: 80%; }
.thumb-remove {
  border: none;
  background: var(--ink06);
  color: var(--ink);
  width: 24px;
  height: 24px;
  border-radius: 50%;
  cursor: pointer;
  line-height: 1;
}
.reject-note { margin-top: 12px; color: var(--accent); font-size: 13px; }
.footer-row {
  margin-top: 28px;
  display: flex;
  justify-content: flex-end;
}
</style>
