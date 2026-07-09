<script setup lang="ts">
import { computed, ref } from 'vue'
import BrandPreview from './BrandPreview.vue'
import { BRAND_PRESETS, type BrandDraft, type BrandPreset } from './presets'
import { resolveMediaUrl } from '@/services/mediaUrl'

const props = defineProps<{ modelValue: BrandDraft }>()
const emit = defineEmits<{
  (e: 'update:modelValue', v: BrandDraft): void
  (e: 'upload-logo', f: File): void
  (e: 'upload-photo', f: File): void
}>()

const logoInput = ref<HTMLInputElement | null>(null)
const photoInput = ref<HTMLInputElement | null>(null)

function emitDraft(patch: Partial<BrandDraft>) {
  emit('update:modelValue', { ...props.modelValue, ...patch })
}

function selectPreset(preset: BrandPreset) {
  emitDraft({
    primary: preset.primary,
    secondary: preset.secondary,
    background: preset.background,
    presetKey: preset.key,
  })
}

function onColorInput(field: 'primary' | 'secondary' | 'background', event: Event) {
  const target = event.target as HTMLInputElement
  emitDraft({ [field]: target.value, presetKey: null } as Partial<BrandDraft>)
}

const previewColors = computed(() => ({
  primary: props.modelValue.primary,
  secondary: props.modelValue.secondary,
  background: props.modelValue.background,
}))

// ── Logo background ("Fundal logo") ──────────────────────────────────────
type LogoBgMode = 'auto' | 'white' | 'transparent' | 'custom'
const LOGO_BG_LABELS: Record<LogoBgMode, string> = {
  auto: 'Auto', white: 'Alb', transparent: 'Transparent', custom: 'Personalizat',
}
const LOGO_BG_MODES = ['auto', 'white', 'transparent', 'custom'] as const

const logoBgMode = computed<LogoBgMode>(() => {
  const v = props.modelValue.logoBackground
  if (v === null) return 'auto'
  if (v === 'transparent') return 'transparent'
  if (v.toUpperCase() === '#FFFFFF') return 'white'
  return 'custom'
})

function setLogoBgMode(mode: LogoBgMode) {
  const current = props.modelValue.logoBackground
  const map: Record<LogoBgMode, string | null> = {
    auto: null,
    white: '#FFFFFF',
    transparent: 'transparent',
    custom: current && current.startsWith('#') ? current : '#FFFFFF',
  }
  emitDraft({ logoBackground: map[mode] })
}

function onLogoBgColor(event: Event) {
  emitDraft({ logoBackground: (event.target as HTMLInputElement).value })
}

const logoSrc = computed(() => resolveMediaUrl(props.modelValue.logoUrl))
const photoSrc = computed(() => resolveMediaUrl(props.modelValue.photoUrl))

function fileNameFromUrl(url: string | null): string | null {
  if (!url) return null
  const clean = url.split('?')[0].split('#')[0]
  const last = clean.substring(clean.lastIndexOf('/') + 1)
  try {
    return decodeURIComponent(last) || null
  } catch {
    return last || null
  }
}

const logoFileName = computed(() => fileNameFromUrl(props.modelValue.logoUrl))
const photoFileName = computed(() => fileNameFromUrl(props.modelValue.photoUrl))

function pickLogo() {
  logoInput.value?.click()
}
function pickPhoto() {
  photoInput.value?.click()
}

function onLogoFile(event: Event) {
  const target = event.target as HTMLInputElement
  const file = target.files?.[0]
  if (file) emit('upload-logo', file)
  target.value = ''
}

function onPhotoFile(event: Event) {
  const target = event.target as HTMLInputElement
  const file = target.files?.[0]
  if (file) emit('upload-photo', file)
  target.value = ''
}
</script>

<template>
  <div class="brand-controls card-white">
    <div class="brand-controls-form">
      <section>
        <div class="field-label brand-section-label">Schemă de culori</div>
        <div class="preset-grid">
          <button
            v-for="preset in BRAND_PRESETS"
            :key="preset.key"
            type="button"
            class="preset-card"
            :class="{ 'is-selected': modelValue.presetKey === preset.key }"
            :style="modelValue.presetKey === preset.key ? { outlineColor: preset.primary } : {}"
            @click="selectPreset(preset)"
          >
            <span class="preset-swatches">
              <span class="preset-swatch" :style="{ background: preset.primary }" />
              <span class="preset-swatch" :style="{ background: preset.secondary }" />
              <span class="preset-swatch preset-swatch-bg" :style="{ background: preset.background }" />
            </span>
            <span class="preset-name">{{ preset.name }}</span>
          </button>
        </div>

        <div class="brand-divider">
          <span class="field-label">Sau personalizat</span>
          <span class="brand-divider-line" />
        </div>

        <div class="color-grid">
          <label class="color-field">
            <input
              type="color"
              class="color-field-input"
              :value="modelValue.primary"
              @input="onColorInput('primary', $event)"
            />
            <span class="color-field-meta">
              <span class="color-field-name">Principal</span>
              <span class="color-field-hex">{{ modelValue.primary }}</span>
            </span>
          </label>
          <label class="color-field">
            <input
              type="color"
              class="color-field-input"
              :value="modelValue.secondary"
              @input="onColorInput('secondary', $event)"
            />
            <span class="color-field-meta">
              <span class="color-field-name">Secundar</span>
              <span class="color-field-hex">{{ modelValue.secondary }}</span>
            </span>
          </label>
          <label class="color-field">
            <input
              type="color"
              class="color-field-input"
              :value="modelValue.background"
              @input="onColorInput('background', $event)"
            />
            <span class="color-field-meta">
              <span class="color-field-name">Fundal</span>
              <span class="color-field-hex">{{ modelValue.background }}</span>
            </span>
          </label>
        </div>
      </section>

      <section class="upload-grid">
        <div class="upload-field">
          <div class="field-label brand-section-label">Logo</div>
          <div class="dropzone-row" role="button" tabindex="0" @click="pickLogo" @keydown.enter="pickLogo">
            <div class="dropzone-thumb">
              <img v-if="logoSrc" :src="logoSrc" alt="" />
              <span v-else class="dropzone-thumb-empty" aria-hidden="true">+</span>
            </div>
            <div class="dropzone-meta">
              <div class="dropzone-filename">{{ logoFileName ?? 'Niciun fișier încărcat' }}</div>
              <div class="dropzone-hint">PNG, JPG sau WebP</div>
            </div>
            <span class="dropzone-action">{{ logoSrc ? 'Înlocuiește' : 'Adaugă' }}</span>
          </div>
          <input
            ref="logoInput"
            type="file"
            accept="image/png,image/jpeg,image/webp"
            class="hidden-input"
            @change="onLogoFile"
          />

          <div class="logo-bg-row">
            <span class="field-label">Fundal logo</span>
            <div class="logo-bg-modes" role="group" aria-label="Fundal logo">
              <button
                v-for="m in LOGO_BG_MODES"
                :key="m"
                type="button"
                class="logo-bg-chip"
                :class="{ 'is-active': logoBgMode === m }"
                @click="setLogoBgMode(m)"
              >{{ LOGO_BG_LABELS[m] }}</button>
              <input
                v-if="logoBgMode === 'custom'"
                type="color"
                class="color-field-input logo-bg-color"
                :value="modelValue.logoBackground?.startsWith('#') ? modelValue.logoBackground : '#FFFFFF'"
                aria-label="Culoare fundal logo"
                @input="onLogoBgColor"
              />
            </div>
          </div>
        </div>

        <div class="upload-field">
          <div class="field-label brand-section-label">Fotografie de prezentare</div>
          <div class="dropzone-row" role="button" tabindex="0" @click="pickPhoto" @keydown.enter="pickPhoto">
            <div class="dropzone-thumb dropzone-thumb-photo" :style="photoSrc ? { backgroundImage: `url('${photoSrc}')` } : {}">
              <span v-if="!photoSrc" class="dropzone-thumb-empty" aria-hidden="true">+</span>
            </div>
            <div class="dropzone-meta">
              <div class="dropzone-filename">{{ photoFileName ?? 'Niciun fișier încărcat' }}</div>
              <div class="dropzone-hint">JPG, PNG sau WebP</div>
            </div>
            <span class="dropzone-action">{{ photoSrc ? 'Înlocuiește' : 'Adaugă' }}</span>
          </div>
          <input
            ref="photoInput"
            type="file"
            accept="image/png,image/jpeg,image/webp"
            class="hidden-input"
            @change="onPhotoFile"
          />
        </div>
      </section>
    </div>

    <BrandPreview
      class="brand-controls-preview"
      :colors="previewColors"
      :logo-url="modelValue.logoUrl"
      :photo-url="modelValue.photoUrl"
      :logo-background="modelValue.logoBackground"
      :logo-is-dark="modelValue.logoIsDark"
    />
  </div>
</template>

<style scoped>
.brand-controls {
  display: grid;
  grid-template-columns: 1.15fr 0.85fr;
  gap: 28px;
  padding: 30px;
}

.brand-controls-form {
  display: flex;
  flex-direction: column;
  gap: 28px;
  min-width: 0;
}

.brand-controls-preview {
  align-self: start;
  position: sticky;
  top: 16px;
}

.brand-section-label { margin-bottom: 12px; }

/* ── preset grid ─────────────────────────────────────────────────────── */
.preset-grid {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
}
.preset-card {
  cursor: pointer;
  background: var(--card);
  border: 1px solid var(--ink12);
  border-radius: 16px;
  padding: 12px 14px 10px;
  display: flex;
  flex-direction: column;
  gap: 9px;
  align-items: center;
  outline: 2px solid transparent;
  outline-offset: 2px;
  transition: transform 0.15s ease, outline-color 0.15s ease;
}
.preset-card:hover { transform: translateY(-2px); }
.preset-card.is-selected { outline-width: 2px; }
.preset-swatches { display: flex; gap: 5px; }
.preset-swatch {
  width: 22px;
  height: 22px;
  border-radius: 6px;
}
.preset-swatch-bg { border: 1px solid var(--ink12); }
.preset-name { font-size: 12px; font-weight: 600; color: var(--ink); }

.brand-divider {
  display: flex;
  align-items: center;
  gap: 10px;
  margin: 16px 0 12px;
}
.brand-divider-line { flex: 1; height: 1px; background: var(--ink12); }

/* ── custom colour fields ───────────────────────────────────────────── */
.color-grid {
  display: flex;
  gap: 10px;
  flex-wrap: wrap;
}
.color-field {
  flex: 1;
  min-width: 130px;
  background: var(--card);
  border: 1px solid var(--ink12);
  border-radius: 12px;
  padding: 8px 12px;
  display: flex;
  align-items: center;
  gap: 10px;
  cursor: pointer;
}
.color-field-input {
  width: 28px;
  height: 28px;
  padding: 0;
  border: 1px solid var(--ink12);
  border-radius: 7px;
  background: none;
  cursor: pointer;
  flex-shrink: 0;
}
/* Native colour-input chrome swatch fill */
.color-field-input::-webkit-color-swatch-wrapper { padding: 2px; }
.color-field-input::-webkit-color-swatch { border: none; border-radius: 5px; }
.color-field-input::-moz-color-swatch { border: none; border-radius: 5px; }

.color-field-meta { display: flex; flex-direction: column; min-width: 0; }
.color-field-name { font-size: 11px; color: var(--ink70); }
.color-field-hex {
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 12px;
  font-weight: 600;
  color: var(--ink);
}

/* ── logo / photo dropzones ─────────────────────────────────────────── */
.upload-grid {
  display: flex;
  gap: 14px;
  flex-wrap: wrap;
}
.upload-field { flex: 1; min-width: 220px; }

.dropzone-row {
  cursor: pointer;
  background: var(--card);
  border: 1.5px dashed var(--ink12);
  border-radius: 16px;
  padding: 14px;
  display: flex;
  align-items: center;
  gap: 14px;
  transition: border-color 0.15s ease, background 0.15s ease;
}
.dropzone-row:hover { border-color: var(--ink40); }

.dropzone-thumb {
  width: 56px;
  height: 56px;
  border-radius: 12px;
  background: var(--ink06);
  display: grid;
  place-items: center;
  flex-shrink: 0;
  overflow: hidden;
  padding: 4px;
}
.dropzone-thumb img { max-width: 100%; max-height: 100%; object-fit: contain; }
.dropzone-thumb-photo { background-size: cover; background-position: center; padding: 0; }
.dropzone-thumb-empty { font-size: 18px; color: var(--ink40); }

.dropzone-meta { min-width: 0; flex: 1; }
.dropzone-filename {
  font-size: 13px;
  font-weight: 600;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  color: var(--ink);
}
.dropzone-hint { font-size: 11px; color: var(--ink40); margin-top: 2px; }
.dropzone-action {
  font-size: 12px;
  font-weight: 600;
  color: var(--accent);
  flex-shrink: 0;
}

.hidden-input { display: none; }

/* ── logo background control ─────────────────────────────────────────── */
.logo-bg-row {
  margin-top: 12px;
  display: flex;
  flex-direction: column;
  gap: 7px;
}
.logo-bg-modes {
  display: flex;
  gap: 6px;
  flex-wrap: wrap;
  align-items: center;
}
.logo-bg-chip {
  font-size: 12px;
  font-weight: 600;
  padding: 5px 11px;
  border-radius: 999px;
  border: 1px solid var(--ink12);
  background: var(--card);
  color: var(--ink70);
  cursor: pointer;
  transition: border-color 0.15s ease, color 0.15s ease;
}
.logo-bg-chip:hover { border-color: var(--ink40); }
.logo-bg-chip.is-active { border-color: var(--accent); color: var(--accent); }
.logo-bg-color { width: 32px; height: 28px; }

@media (max-width: 860px) {
  .brand-controls { grid-template-columns: 1fr; }
  .brand-controls-preview { position: static; justify-self: center; }
}
</style>
