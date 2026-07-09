<script setup lang="ts">
import { onMounted, ref } from 'vue'
import BrandControls from '@/components/branding/BrandControls.vue'
import { BRAND_PRESETS, type BrandDraft } from '@/components/branding/presets'
import { useBrandingStore } from '@/stores/useBrandingStore'
import { useBrandTheme } from '@/composables/useBrandTheme'
import { useToast } from '@/composables/useToast'

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
  }
}

const draft = ref<BrandDraft>(defaultDraft())
const loading = ref(true)
const saving = ref(false)

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
    draft.value = { ...draft.value, logoUrl: url }
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

async function onSave() {
  saving.value = true
  try {
    await store.save({
      enabled: draft.value.enabled,
      primary: draft.value.primary,
      secondary: draft.value.secondary,
      background: draft.value.background,
      presetKey: draft.value.presetKey,
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
  <div>
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
    <BrandControls v-else v-model="draft" @upload-logo="onLogo" @upload-photo="onPhoto" />
  </div>
</template>

<style scoped>
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
