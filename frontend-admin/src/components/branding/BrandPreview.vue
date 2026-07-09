<script setup lang="ts">
import { computed } from 'vue'
import type { CSSProperties } from 'vue'
import { useBrandTheme } from '@/composables/useBrandTheme'
import { resolveMediaUrl } from '@/services/mediaUrl'
import type { BrandInput } from '@shared/branding/derive'

const props = defineProps<{
  colors: BrandInput
  logoUrl?: string | null
  photoUrl?: string | null
}>()

const { brandVarMap } = useBrandTheme()

// Scoped to this wrapper only — never touches document.documentElement, so
// the mini mock re-tints live from the DRAFT colours without affecting the
// real applied admin theme.
const varMap = computed(() => brandVarMap(props.colors) as CSSProperties)

const logoSrc = computed(() => resolveMediaUrl(props.logoUrl))
const photoSrc = computed(() => resolveMediaUrl(props.photoUrl))

const PHOTO_OVERLAY = 'linear-gradient(180deg, rgba(0,0,0,.15) 0%, rgba(0,0,0,.35) 55%, rgba(0,0,0,.82) 100%)'
const phoneScreenStyle = computed<CSSProperties>(() => ({
  backgroundImage: photoSrc.value
    ? `${PHOTO_OVERLAY}, url('${photoSrc.value}')`
    : 'linear-gradient(165deg, var(--accentSoft) 0%, var(--paper) 100%)',
}))
</script>

<template>
  <div class="brand-preview" :style="varMap">
    <div class="brand-preview-label">
      <span class="live-dot" />
      <span class="eyebrow">Previzualizare live</span>
    </div>

    <!-- mini phone welcome -->
    <div class="preview-phone">
      <div class="preview-phone-screen" :style="phoneScreenStyle">
        <span class="preview-phone-eyebrow">Bine ai venit la</span>
        <div class="preview-phone-plate">
          <img v-if="logoSrc" :src="logoSrc" alt="" class="preview-phone-logo" />
          <span v-else class="preview-phone-logo-placeholder">LOGO</span>
        </div>
        <div class="preview-phone-cta">Vezi meniul</div>
      </div>
    </div>

    <!-- mini dashboard -->
    <div class="preview-dash">
      <div class="preview-dash-side">
        <span class="preview-dash-bar" style="width: 70%; background: var(--accent)" />
        <div class="preview-dash-chip">
          <span class="preview-dash-chip-dot" />
          <span class="preview-dash-chip-line" />
        </div>
        <span class="preview-dash-bar preview-dash-bar-soft" style="width: 55%" />
        <span class="preview-dash-bar preview-dash-bar-soft" style="width: 62%" />
      </div>
      <div class="preview-dash-main">
        <span class="preview-dash-bar preview-dash-bar-soft" style="width: 45%" />
        <span class="preview-dash-bar preview-dash-bar-strong" style="width: 78%" />
        <div class="preview-dash-tiles">
          <span class="preview-dash-tile preview-dash-tile-card" />
          <span class="preview-dash-tile preview-dash-tile-ink" />
          <span class="preview-dash-tile preview-dash-tile-lime" />
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.brand-preview {
  background: var(--paperHi);
  border: 1px solid var(--ink12);
  border-radius: 24px;
  padding: 28px 22px 30px;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 18px;
}

.brand-preview-label {
  align-self: stretch;
  display: flex;
  align-items: center;
  gap: 8px;
}

/* ── mini phone ─────────────────────────────────────────────────────── */
.preview-phone {
  width: 196px;
  height: 392px;
  border-radius: 34px;
  background: #111;
  padding: 8px;
  box-shadow: 0 30px 60px -30px rgba(21, 20, 15, 0.6);
  flex-shrink: 0;
}
.preview-phone-screen {
  position: relative;
  width: 100%;
  height: 100%;
  border-radius: 27px;
  overflow: hidden;
  background-size: cover;
  background-position: center;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 20px 16px;
}
.preview-phone-eyebrow {
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 9px;
  letter-spacing: 0.22em;
  text-transform: uppercase;
  color: rgba(255, 255, 255, 0.9);
  margin-bottom: 14px;
}
.preview-phone-plate {
  background: rgba(255, 255, 255, 0.94);
  border-radius: 16px;
  padding: 14px 16px;
  backdrop-filter: blur(6px);
  display: grid;
  place-items: center;
  min-width: 64px;
  min-height: 64px;
}
.preview-phone-logo {
  height: 56px;
  max-width: 120px;
  object-fit: contain;
}
.preview-phone-logo-placeholder {
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 10px;
  font-weight: 700;
  letter-spacing: 0.1em;
  color: rgba(21, 20, 15, 0.35);
}
.preview-phone-cta {
  margin-top: auto;
  width: 100%;
  background: var(--accent);
  color: var(--onAccent);
  border-radius: 999px;
  padding: 9px;
  text-align: center;
  font-size: 11px;
  font-weight: 700;
}

/* ── mini dashboard ─────────────────────────────────────────────────── */
.preview-dash {
  width: 100%;
  max-width: 280px;
  border-radius: 16px;
  overflow: hidden;
  border: 1px solid var(--ink12);
  display: flex;
  background: var(--paper);
  box-shadow: 0 16px 40px -28px rgba(21, 20, 15, 0.5);
}
.preview-dash-side {
  width: 34%;
  background: var(--paperHi);
  padding: 10px 8px;
  display: flex;
  flex-direction: column;
  gap: 6px;
  border-right: 1px solid var(--ink12);
}
.preview-dash-bar {
  height: 5px;
  border-radius: 3px;
  background: var(--ink);
}
.preview-dash-bar-soft { background: var(--ink40); }
.preview-dash-bar-strong { height: 9px; background: var(--ink); }
.preview-dash-chip {
  height: 26px;
  border-radius: 7px;
  background: var(--ink);
  opacity: 0.92;
  display: flex;
  align-items: center;
  padding: 0 7px;
  gap: 5px;
}
.preview-dash-chip-dot {
  width: 9px;
  height: 9px;
  border-radius: 3px;
  background: var(--lime);
  flex-shrink: 0;
}
.preview-dash-chip-line {
  height: 4px;
  width: 40%;
  border-radius: 2px;
  background: var(--onAccent);
  opacity: 0.8;
}
.preview-dash-main {
  flex: 1;
  padding: 11px;
  display: flex;
  flex-direction: column;
  gap: 7px;
  min-width: 0;
}
.preview-dash-tiles {
  display: flex;
  gap: 6px;
  margin-top: 3px;
}
.preview-dash-tile {
  flex: 1;
  height: 40px;
  border-radius: 9px;
}
.preview-dash-tile-card { background: var(--card); border: 1px solid var(--ink12); }
.preview-dash-tile-ink { background: var(--ink); }
.preview-dash-tile-lime { background: var(--lime); }
</style>
