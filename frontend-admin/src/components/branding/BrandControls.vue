<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { BRAND_PRESETS, type BrandDraft, type BrandPreset } from './presets'
import { resolveMediaUrl } from '@/services/mediaUrl'
import type { BrandingAssetKind } from '@/services/brandingUpload'
import { deriveBrandPalette } from '@shared/branding/derive'
import {
  MENU_BACKGROUND_STYLES,
  DEFAULT_MENU_BACKGROUND_STYLE,
  menuBackgroundScrimOpacity,
  type MenuBackgroundStyle,
} from '@shared/branding/menuBackground'

const props = defineProps<{ modelValue: BrandDraft }>()
const emit = defineEmits<{
  (e: 'update:modelValue', v: BrandDraft): void
  (e: 'upload-logo', f: File): void
  (e: 'upload-photo', f: File): void
  (e: 'upload-menu-background', f: File): void
  (e: 'remove', kind: BrandingAssetKind): void
}>()

const logoInput = ref<HTMLInputElement | null>(null)
const photoInput = ref<HTMLInputElement | null>(null)
const menuBgInput = ref<HTMLInputElement | null>(null)

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

// Mode is LOCAL UI state, not derived from the value: a custom colour of
// #FFFFFF is indistinguishable from the "white" preset, so deriving the mode
// from the value would snap "Personalizat" back to "Alb" and hide the picker.
function modeFromValue(v: string | null): LogoBgMode {
  if (v === null) return 'auto'
  if (v === 'transparent') return 'transparent'
  if (v.toUpperCase() === '#FFFFFF') return 'white'
  return 'custom'
}

const logoBgMode = ref<LogoBgMode>(modeFromValue(props.modelValue.logoBackground))

// Resync when the value changes from outside (e.g. a draft reload), but never
// clobber an explicit "custom" selection that happens to hold a hex colour.
watch(() => props.modelValue.logoBackground, (v) => {
  if (logoBgMode.value === 'custom' && !!v && v.startsWith('#')) return
  logoBgMode.value = modeFromValue(v)
})

function setLogoBgMode(mode: LogoBgMode) {
  logoBgMode.value = mode
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
const menuBgSrc = computed(() => resolveMediaUrl(props.modelValue.menuBackgroundUrl))

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
const menuBgFileName = computed(() => fileNameFromUrl(props.modelValue.menuBackgroundUrl))

function pickLogo() {
  logoInput.value?.click()
}
function pickPhoto() {
  photoInput.value?.click()
}
function pickMenuBg() {
  menuBgInput.value?.click()
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

function onMenuBgFile(event: Event) {
  const target = event.target as HTMLInputElement
  const file = target.files?.[0]
  if (file) emit('upload-menu-background', file)
  target.value = ''
}

// ── Menu background visibility ("Vizibilitate fundal") ───────────────────
const MENU_BG_LABELS: Record<MenuBackgroundStyle, string> = {
  visible: 'Vizibil', medium: 'Mediu', subtle: 'Estompat',
}
const menuBgStyle = computed<MenuBackgroundStyle>(
  () => (props.modelValue.menuBackgroundStyle as MenuBackgroundStyle) ?? DEFAULT_MENU_BACKGROUND_STYLE,
)
function setMenuBgStyle(style: MenuBackgroundStyle) {
  emitDraft({ menuBackgroundStyle: style })
}
// Derive the branded palette so the preview scrim + text match what the diner sees.
const menuBgPalette = computed(() => {
  try { return deriveBrandPalette(previewColors.value) } catch { return null }
})
const menuBgScrimColor = computed(() => menuBgPalette.value?.paper ?? props.modelValue.background)
const menuBgInkColor = computed(() => menuBgPalette.value?.ink ?? '#15140F')
const menuBgScrimOpacity = computed(() => menuBackgroundScrimOpacity(menuBgStyle.value))
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
          <div class="dropzone-wrap">
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
            <button
              v-if="logoSrc"
              type="button"
              class="dropzone-remove"
              aria-label="Elimină logo-ul"
              @click="emit('remove', 'logo')"
            >×</button>
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
          <div class="dropzone-wrap">
            <div class="dropzone-row" role="button" tabindex="0" @click="pickPhoto" @keydown.enter="pickPhoto">
              <div class="dropzone-thumb dropzone-thumb-photo" :style="photoSrc ? { backgroundImage: `url('${photoSrc}')` } : {}">
                <span v-if="!photoSrc" class="dropzone-thumb-empty" aria-hidden="true">+</span>
              </div>
              <div class="dropzone-meta">
                <div class="dropzone-filename">{{ photoFileName ?? 'Niciun fișier încărcat' }}</div>
                <div class="dropzone-hint">Ecranul de întâmpinare · JPG, PNG sau WebP</div>
              </div>
              <span class="dropzone-action">{{ photoSrc ? 'Înlocuiește' : 'Adaugă' }}</span>
            </div>
            <button
              v-if="photoSrc"
              type="button"
              class="dropzone-remove"
              aria-label="Elimină fotografia"
              @click="emit('remove', 'photo')"
            >×</button>
          </div>
          <input
            ref="photoInput"
            type="file"
            accept="image/png,image/jpeg,image/webp"
            class="hidden-input"
            @change="onPhotoFile"
          />
        </div>

        <div class="upload-field">
          <div class="field-label brand-section-label">Fundal meniu</div>
          <div class="dropzone-wrap">
            <div class="dropzone-row" role="button" tabindex="0" @click="pickMenuBg" @keydown.enter="pickMenuBg">
              <div class="dropzone-thumb dropzone-thumb-photo" :style="menuBgSrc ? { backgroundImage: `url('${menuBgSrc}')` } : {}">
                <span v-if="!menuBgSrc" class="dropzone-thumb-empty" aria-hidden="true">+</span>
              </div>
              <div class="dropzone-meta">
                <div class="dropzone-filename">{{ menuBgFileName ?? 'Niciun fișier încărcat' }}</div>
                <div class="dropzone-hint">În spatele meniului · JPG, PNG sau WebP</div>
              </div>
              <span class="dropzone-action">{{ menuBgSrc ? 'Înlocuiește' : 'Adaugă' }}</span>
            </div>
            <button
              v-if="menuBgSrc"
              type="button"
              class="dropzone-remove"
              aria-label="Elimină fundalul de meniu"
              @click="emit('remove', 'menu-background')"
            >×</button>
          </div>
          <input
            ref="menuBgInput"
            type="file"
            accept="image/png,image/jpeg,image/webp"
            class="hidden-input"
            @change="onMenuBgFile"
          />

          <div v-if="menuBgSrc" class="logo-bg-row">
            <span class="field-label">Vizibilitate fundal</span>
            <div class="logo-bg-modes" role="group" aria-label="Vizibilitate fundal meniu">
              <button
                v-for="m in MENU_BACKGROUND_STYLES"
                :key="m"
                type="button"
                class="logo-bg-chip"
                :class="{ 'is-active': menuBgStyle === m }"
                @click="setMenuBgStyle(m)"
              >{{ MENU_BG_LABELS[m] }}</button>
            </div>
            <div class="menu-bg-preview">
              <div class="menu-bg-preview-photo" :style="{ backgroundImage: `url('${menuBgSrc}')` }" />
              <div class="menu-bg-preview-scrim" :style="{ background: menuBgScrimColor, opacity: menuBgScrimOpacity }" />
              <span class="menu-bg-preview-label" :style="{ color: menuBgInkColor }">Meniu</span>
            </div>
          </div>
        </div>
      </section>
    </div>
  </div>
</template>

<style scoped>
.brand-controls {
  padding: 30px;
}

.brand-controls-form {
  display: flex;
  flex-direction: column;
  gap: 28px;
  min-width: 0;
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

.dropzone-wrap { position: relative; }
/* Small floating "remove" badge on the dropzone's top-right corner. Rendered as
   a sibling of the role="button" row (not a child) so it never triggers the
   file picker and avoids nesting interactive controls. */
.dropzone-remove {
  position: absolute;
  top: -8px;
  right: -8px;
  width: 24px;
  height: 24px;
  border-radius: 50%;
  border: 1px solid var(--ink12);
  background: var(--card);
  color: var(--ink70);
  font-size: 16px;
  line-height: 1;
  cursor: pointer;
  display: grid;
  place-items: center;
  box-shadow: 0 2px 6px -2px rgba(0, 0, 0, 0.25);
  transition: color 0.15s ease, border-color 0.15s ease, transform 0.15s ease;
  z-index: 2;
}
.dropzone-remove:hover { color: var(--accent); border-color: var(--accent); transform: scale(1.05); }

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

/* Live preview of the menu background with the selected scrim, so the manager
   sees "visible vs faded" (and text legibility) without leaving the page. */
.menu-bg-preview {
  position: relative;
  margin-top: 4px;
  height: 64px;
  border-radius: 12px;
  overflow: hidden;
  border: 1px solid var(--ink12);
}
.menu-bg-preview-photo {
  position: absolute;
  inset: 0;
  background-size: cover;
  background-position: center;
}
.menu-bg-preview-scrim { position: absolute; inset: 0; }
.menu-bg-preview-label {
  position: absolute;
  left: 12px;
  bottom: 9px;
  font-size: 15px;
  font-weight: 700;
  letter-spacing: -0.01em;
}
</style>
