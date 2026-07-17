<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import BrandControls from '@/components/branding/BrandControls.vue'
import BrandPreview from '@/components/branding/BrandPreview.vue'
import { BRAND_PRESETS, type BrandDraft } from '@/components/branding/presets'
import { useBrandingStore } from '@/stores/useBrandingStore'
import { useBrandTheme } from '@/composables/useBrandTheme'
import { useToast } from '@/composables/useToast'
import { computeLogoIsDark } from '@/utils/logoLuminance'
import type { BrandingAssetKind } from '@/services/brandingUpload'

const store = useBrandingStore()
const brandTheme = useBrandTheme()
const toast = useToast()

const classicPreset = BRAND_PRESETS.find((p) => p.key === 'classic')!

function defaultDraft(): BrandDraft {
  return {
    enabled: false,
    primary: classicPreset.primary,
    secondary: classicPreset.secondary,
    background: classicPreset.background,
    presetKey: classicPreset.key,
    logoUrl: null,
    photoUrl: null,
    menuBackgroundUrl: null,
    menuBackgroundStyle: null,
    logoBackground: null,
    logoIsDark: null,
  }
}

const draft = ref<BrandDraft>(defaultDraft())
const loading = ref(true)
const saving = ref(false)

const previewColors = computed(() => ({
  primary: draft.value.primary,
  secondary: draft.value.secondary,
  background: draft.value.background,
}))

onMounted(async () => {
  try {
    await store.load()
    const b = store.branding
    if (b) {
      draft.value = {
        enabled: b.enabled,
        primary: b.primary ?? classicPreset.primary,
        secondary: b.secondary ?? classicPreset.secondary,
        background: b.background ?? classicPreset.background,
        presetKey: b.presetKey ?? null,
        logoUrl: b.logoUrl,
        photoUrl: b.photoUrl,
        menuBackgroundUrl: b.menuBackgroundUrl ?? null,
        menuBackgroundStyle: b.menuBackgroundStyle ?? null,
        logoBackground: b.logoBackground ?? null,
        logoIsDark: b.logoIsDark ?? null,
      }
    }
  } catch (e) {
    toast.error(e instanceof Error ? e.message : 'Eroare la încărcarea brandului.')
  } finally {
    loading.value = false
  }
})

async function onLogo(file: File) {
  try {
    const url = await store.uploadLogo(file)
    // Detect the logo's own lightness so Auto can pick a legible plate.
    const isDark = await computeLogoIsDark(file)
    draft.value = { ...draft.value, logoUrl: url, logoIsDark: isDark }
    toast.success('Logo încărcat.')
  } catch (e) {
    toast.error(e instanceof Error ? e.message : 'Eroare la încărcarea logo-ului.')
  }
}

async function onPhoto(file: File) {
  try {
    const url = await store.uploadPhoto(file)
    draft.value = { ...draft.value, photoUrl: url }
    toast.success('Fotografie încărcată.')
  } catch (e) {
    toast.error(e instanceof Error ? e.message : 'Eroare la încărcarea fotografiei.')
  }
}

async function onMenuBackground(file: File) {
  try {
    const url = await store.uploadMenuBackground(file)
    draft.value = { ...draft.value, menuBackgroundUrl: url }
    toast.success('Fundal de meniu încărcat.')
  } catch (e) {
    toast.error(e instanceof Error ? e.message : 'Eroare la încărcarea fundalului de meniu.')
  }
}

// Remove a stored asset (X button on a dropzone). Removing the logo also drops
// its "is dark" hint, mirroring the backend.
async function onRemoveAsset(kind: BrandingAssetKind) {
  try {
    await store.removeAsset(kind)
    if (kind === 'logo') draft.value = { ...draft.value, logoUrl: null, logoIsDark: null }
    else if (kind === 'photo') draft.value = { ...draft.value, photoUrl: null }
    else if (kind === 'menu-background') draft.value = { ...draft.value, menuBackgroundUrl: null }
    toast.success('Imagine eliminată.')
  } catch (e) {
    toast.error(e instanceof Error ? e.message : 'Eroare la eliminarea imaginii.')
  }
}

// Choosing a preset or editing a colour is a customization — it implies the user
// wants branding active, so flip the master toggle on for them (they can still turn
// it off explicitly). Without this, a fresh restaurant's scheme edits would save with
// enabled:false and never apply, which reads as "the theme doesn't change".
function onDraftUpdate(next: BrandDraft) {
  const schemeChanged =
    next.presetKey !== draft.value.presetKey ||
    next.primary !== draft.value.primary ||
    next.secondary !== draft.value.secondary ||
    next.background !== draft.value.background
  draft.value = schemeChanged ? { ...next, enabled: true } : next
}

async function onSave() {
  saving.value = true
  try {
    await store.save({
      enabled: draft.value.enabled,
      primary: draft.value.primary,
      secondary: draft.value.secondary,
      background: draft.value.background,
      presetKey: draft.value.presetKey,
      logoBackground: draft.value.logoBackground,
      logoIsDark: draft.value.logoIsDark,
      menuBackgroundStyle: draft.value.menuBackgroundStyle,
    })
    if (draft.value.enabled) {
      brandTheme.applyBrand({
        primary: draft.value.primary,
        secondary: draft.value.secondary,
        background: draft.value.background,
      })
    } else {
      brandTheme.clearBrand()
    }
    toast.success('Brandul a fost salvat.')
  } catch (e) {
    toast.error(e instanceof Error ? e.message : 'Eroare la salvarea brandului.')
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <div class="brand-tab">
    <div class="brand-tab-main">
      <div class="page-head">
        <div>
          <div class="page-head-eyebrow">Setări</div>
          <h1 class="page-head-title">Brand</h1>
        </div>
        <div class="page-head-actions">
          <button type="button" class="pill-btn" :disabled="loading || saving" @click="onSave">
            {{ saving ? 'Se salvează…' : 'Salvează' }}
          </button>
        </div>
      </div>

      <label class="brand-master-toggle card-white">
        <input v-model="draft.enabled" type="checkbox" class="brand-master-toggle-input" />
        <span class="brand-master-toggle-switch" aria-hidden="true">
          <span class="brand-master-toggle-knob" />
        </span>
        <span class="brand-master-toggle-text">
          <span class="brand-master-toggle-title">Personalizare brand</span>
          <span class="brand-master-toggle-desc">
            Când e activă, culorile, logo-ul și fotografia de mai jos înlocuiesc tema standard vibeDine
            peste tot — în consola echipei și pe ecranele clienților din sală.
          </span>
        </span>
      </label>

      <p v-if="loading" class="brand-loading">Se încarcă…</p>
      <BrandControls
        v-else
        :model-value="draft"
        @update:model-value="onDraftUpdate"
        @upload-logo="onLogo"
        @upload-photo="onPhoto"
        @upload-menu-background="onMenuBackground"
        @remove="onRemoveAsset"
      />
    </div>

    <!-- Sticky live preview: pinned beside the (taller) options column so it
         stays in place while the manager scrolls through all the settings. -->
    <BrandPreview
      class="brand-tab-aside"
      :colors="previewColors"
      :logo-url="draft.logoUrl"
      :photo-url="draft.photoUrl"
      :logo-background="draft.logoBackground"
      :logo-is-dark="draft.logoIsDark"
      :menu-background-url="draft.menuBackgroundUrl"
      :menu-background-style="draft.menuBackgroundStyle"
    />
  </div>
</template>

<style scoped>
/* Two columns: a tall options column and a preview that sticks beside it. The
   options column carries the title, toggle and every control, so it's always
   much taller than the preview — which gives the sticky element the room it
   needs to stay pinned for the whole scroll instead of drifting off. */
.brand-tab {
  display: grid;
  grid-template-columns: minmax(0, 1fr) 380px;
  gap: 24px;
  align-items: start;
}
.brand-tab-main {
  min-width: 0;
  display: flex;
  flex-direction: column;
}
.brand-tab-aside {
  position: sticky;
  top: 16px;
  align-self: start;
  /* Safety net for short windows: scroll inside rather than overflow the screen. */
  max-height: calc(100vh - 32px);
  overflow-y: auto;
}
/* Stack on narrower screens; the preview drops below and stops sticking. */
@media (max-width: 1024px) {
  .brand-tab {
    grid-template-columns: 1fr;
  }
  .brand-tab-aside {
    position: static;
    max-height: none;
    justify-self: center;
    margin-top: 20px;
  }
}

.brand-master-toggle {
  display: flex;
  align-items: flex-start;
  gap: 14px;
  padding: 18px 20px;
  margin-bottom: 20px;
  cursor: pointer;
}

.brand-master-toggle-input {
  position: absolute;
  width: 1px;
  height: 1px;
  overflow: hidden;
  clip: rect(0 0 0 0);
}

.brand-master-toggle-switch {
  flex-shrink: 0;
  width: 40px;
  height: 24px;
  border-radius: 999px;
  background: var(--ink12);
  position: relative;
  margin-top: 2px;
  transition: background 0.15s ease;
}
.brand-master-toggle-knob {
  position: absolute;
  top: 3px;
  left: 3px;
  width: 18px;
  height: 18px;
  border-radius: 50%;
  background: var(--card);
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.3);
  transition: transform 0.15s ease;
}
.brand-master-toggle-input:checked + .brand-master-toggle-switch {
  background: var(--accent);
}
.brand-master-toggle-input:checked + .brand-master-toggle-switch .brand-master-toggle-knob {
  transform: translateX(16px);
}
.brand-master-toggle-input:focus-visible + .brand-master-toggle-switch {
  outline: 2px solid var(--accent);
  outline-offset: 2px;
}

.brand-master-toggle-text {
  display: flex;
  flex-direction: column;
  gap: 4px;
  min-width: 0;
}
.brand-master-toggle-title {
  font-weight: 700;
  font-size: 15px;
  color: var(--ink);
}
.brand-master-toggle-desc {
  font-size: 13px;
  line-height: 1.5;
  color: var(--ink70);
  max-width: 60ch;
}

.brand-loading {
  font-size: 14px;
  color: var(--ink70);
}
</style>
