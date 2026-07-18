<script setup lang="ts">
import { computed } from 'vue'
import type { CSSProperties } from 'vue'
import { useBrandTheme } from '@/composables/useBrandTheme'
import { resolveMediaUrl } from '@/services/mediaUrl'
import { resolveLogoBackground, type BrandInput } from '@shared/branding/derive'
import { menuBackgroundScrimOpacity } from '@shared/branding/menuBackground'

const props = defineProps<{
  colors: BrandInput
  logoUrl?: string | null
  photoUrl?: string | null
  logoBackground?: string | null
  logoIsDark?: boolean | null
  menuBackgroundUrl?: string | null
  menuBackgroundStyle?: string | null
}>()

const { brandVarMap } = useBrandTheme()

const plate = computed(() => resolveLogoBackground(props.logoBackground ?? null, props.logoIsDark ?? null))

// Scoped to this wrapper only — never touches document.documentElement, so
// the mini mock re-tints live from the DRAFT colours without affecting the
// real applied admin theme.
const varMap = computed(() => brandVarMap(props.colors) as CSSProperties)

const logoSrc = computed(() => resolveMediaUrl(props.logoUrl))
const photoSrc = computed(() => resolveMediaUrl(props.photoUrl))
const menuBgSrc = computed(() => resolveMediaUrl(props.menuBackgroundUrl))

const PHOTO_OVERLAY = 'linear-gradient(180deg, rgba(0,0,0,.15) 0%, rgba(0,0,0,.35) 55%, rgba(0,0,0,.82) 100%)'
const phoneScreenStyle = computed<CSSProperties>(() => ({
  backgroundImage: photoSrc.value
    ? `${PHOTO_OVERLAY}, url('${photoSrc.value}')`
    : 'linear-gradient(165deg, var(--accentSoft) 0%, var(--paper) 100%)',
}))

// Menu-background wash: same paper scrim + style-driven opacity the diner sees.
const menuScrimOpacity = computed(() => menuBackgroundScrimOpacity(props.menuBackgroundStyle))
</script>

<template>
  <div class="brand-preview" :style="varMap">
    <div class="brand-preview-label">
      <span class="live-dot" />
      <span class="eyebrow">Previzualizare live</span>
    </div>

    <!-- two mini phones side by side: the QR welcome screen + the menu -->
    <div class="preview-phones">
      <div class="preview-phone-col">
        <div class="preview-phone">
          <div class="preview-phone-screen" :style="phoneScreenStyle">
            <span class="preview-phone-eyebrow">Bine ai venit la</span>
            <div
              class="preview-phone-plate"
              :class="{ 'is-bare': !plate.plate }"
              :style="{ background: plate.plate ? plate.background : 'transparent' }"
            >
              <img v-if="logoSrc" :src="logoSrc" alt="" class="preview-phone-logo" />
              <span v-else class="preview-phone-logo-placeholder">LOGO</span>
            </div>
            <div class="preview-phone-cta">Vezi meniul</div>
          </div>
        </div>
        <span class="preview-caption">Întâmpinare</span>
      </div>

      <div class="preview-phone-col">
        <div class="preview-phone">
          <div class="preview-phone-screen preview-menu">
            <template v-if="menuBgSrc">
              <div class="preview-menu-photo" :style="{ backgroundImage: `url('${menuBgSrc}')` }" />
              <div class="preview-menu-scrim" :style="{ opacity: menuScrimOpacity }" />
            </template>
            <div class="preview-menu-body">
              <div class="preview-menu-head">
                <span class="preview-menu-back">‹</span>
                <span class="preview-menu-title">Meniu</span>
              </div>
              <div v-for="i in 3" :key="i" class="preview-menu-card">
                <span class="preview-menu-thumb" />
                <span class="preview-menu-info">
                  <span class="preview-menu-l1" />
                  <span class="preview-menu-l2" />
                </span>
                <span class="preview-menu-add">+</span>
              </div>
            </div>
          </div>
        </div>
        <span class="preview-caption">Meniu</span>
      </div>
    </div>

    <!-- mini dashboard — shows the palette applied to the admin console -->
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
  padding: 28px 16px 30px;
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

/* ── two mini phones ────────────────────────────────────────────────── */
.preview-phones {
  display: flex;
  gap: 14px;
  align-items: flex-start;
  justify-content: center;
  flex-wrap: wrap;
}
.preview-phone-col {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 8px;
}
.preview-caption {
  font-size: 11px;
  font-weight: 600;
  color: var(--ink70);
}
.preview-phone {
  width: 158px;
  height: 316px;
  border-radius: 30px;
  background: #111;
  padding: 6px;
  box-shadow: 0 24px 48px -28px rgba(21, 20, 15, 0.6);
  flex-shrink: 0;
}
.preview-phone-screen {
  position: relative;
  width: 100%;
  height: 100%;
  border-radius: 23px;
  overflow: hidden;
  background-size: cover;
  background-position: center;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 16px 12px;
}
.preview-phone-eyebrow {
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 8px;
  letter-spacing: 0.22em;
  text-transform: uppercase;
  color: rgba(255, 255, 255, 0.9);
  margin-bottom: 10px;
}
.preview-phone-plate {
  border-radius: 13px;
  padding: 10px 12px;
  backdrop-filter: blur(6px);
  display: grid;
  place-items: center;
  min-width: 48px;
  min-height: 48px;
}
.preview-phone-plate.is-bare {
  backdrop-filter: none;
}
.preview-phone-logo {
  height: 42px;
  max-width: 96px;
  object-fit: contain;
}
.preview-phone-logo-placeholder {
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 9px;
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
  padding: 8px;
  text-align: center;
  font-size: 10px;
  font-weight: 700;
}

/* ── menu phone ─────────────────────────────────────────────────────── */
.preview-menu {
  background: var(--paper);
  padding: 0;
  align-items: stretch;
  justify-content: flex-start;
}
.preview-menu-photo {
  position: absolute;
  inset: 0;
  background-size: cover;
  background-position: center;
}
.preview-menu-scrim {
  position: absolute;
  inset: 0;
  background: var(--paper);
}
.preview-menu-body {
  position: relative;
  z-index: 1;
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding: 14px 11px;
}
.preview-menu-head {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 2px;
}
.preview-menu-back {
  width: 22px;
  height: 22px;
  border-radius: 50%;
  background: var(--card);
  border: 1px solid var(--ink12);
  display: grid;
  place-items: center;
  font-size: 14px;
  color: var(--ink);
  flex-shrink: 0;
}
.preview-menu-title {
  font-size: 13px;
  font-weight: 700;
  color: var(--ink);
  letter-spacing: -0.01em;
}
.preview-menu-card {
  display: flex;
  align-items: center;
  gap: 8px;
  background: var(--card);
  border: 1px solid var(--ink12);
  border-radius: 12px;
  padding: 8px;
  box-shadow: 0 6px 16px -12px rgba(21, 20, 15, 0.4);
}
.preview-menu-thumb {
  width: 34px;
  height: 34px;
  border-radius: 8px;
  background: linear-gradient(135deg, var(--accent), var(--lime));
  flex-shrink: 0;
}
.preview-menu-info {
  flex: 1;
  display: flex;
  flex-direction: column;
  gap: 5px;
  min-width: 0;
}
.preview-menu-l1 {
  height: 6px;
  width: 68%;
  border-radius: 3px;
  background: var(--ink);
}
.preview-menu-l2 {
  height: 5px;
  width: 90%;
  border-radius: 3px;
  background: var(--ink40);
}
.preview-menu-add {
  width: 22px;
  height: 22px;
  border-radius: 50%;
  background: var(--accent);
  color: var(--onAccent);
  display: grid;
  place-items: center;
  font-size: 14px;
  font-weight: 700;
  flex-shrink: 0;
}

/* ── mini dashboard ─────────────────────────────────────────────────── */
.preview-dash {
  width: 100%;
  max-width: 300px;
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
