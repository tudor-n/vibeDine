<script setup lang="ts">
import { computed } from 'vue'
import { T, TD, BLOB_PATHS, type ThemeTokens } from '@/design/tokens'

const props = withDefaults(
  defineProps<{
    label: string
    sublabel?: string
    pathIndex?: number
    image?: string | null
    active?: boolean
    theme?: 'light' | 'dark'
    size?: number
  }>(),
  {
    sublabel: '',
    pathIndex: 0,
    image: null,
    active: false,
    theme: 'light',
    size: 158,
  },
)

const emit = defineEmits<{ (e: 'click', ev: MouseEvent): void }>()

const t = computed<ThemeTokens>(() => (props.theme === 'dark' ? TD : T))
const path = computed(() => BLOB_PATHS[props.pathIndex % BLOB_PATHS.length])

// Stable per-instance suffix so SVG <defs> ids never collide between pebbles.
const uid = Math.random().toString(36).slice(2, 9)

// Inactive vs active drop shadow. Active uses a lime glow per the handoff.
const dropShadow = computed(() => {
  if (props.active) {
    // Lime glow + a soft secondary shadow for depth.
    return props.theme === 'dark'
      ? `drop-shadow(0 16px 24px ${t.value.lime}88) drop-shadow(0 4px 8px rgba(0,0,0,0.45))`
      : `drop-shadow(0 16px 24px ${t.value.lime}88) drop-shadow(0 4px 8px rgba(21,20,15,0.14))`
  }
  return props.theme === 'dark'
    ? 'drop-shadow(0 8px 14px rgba(0,0,0,0.45)) drop-shadow(0 2px 4px rgba(0,0,0,0.30))'
    : 'drop-shadow(0 8px 14px rgba(21,20,15,0.22)) drop-shadow(0 2px 4px rgba(21,20,15,0.10))'
})

// Subtle inactive contour stroke per handoff: rgba(ink, 0.12) — not accent.
const inactiveStroke = computed(() =>
  props.theme === 'dark' ? 'rgba(245,240,232,0.12)' : 'rgba(21,20,15,0.12)',
)

// Fallback wash behind the image (only visible until the photo loads).
const fallbackBg = computed(() => (props.theme === 'dark' ? '#3a2418' : '#e8d5b9'))
</script>

<template>
  <button
    class="pebble"
    type="button"
    :aria-pressed="active"
    :aria-label="label"
    @click="emit('click', $event)"
  >
    <div
      class="blob-wrap"
      :style="{
        transform: active ? 'scale(1.06) translateY(-4px)' : 'scale(1) translateY(0)',
        filter: dropShadow,
      }"
    >
      <svg
        viewBox="0 0 100 100"
        :width="size"
        :height="size"
        style="display:block;overflow:visible"
        aria-hidden="true"
      >
        <defs>
          <clipPath :id="`clip-${uid}`"><path :d="path" /></clipPath>
          <radialGradient :id="`hi-${uid}`" cx="32%" cy="25%" r="52%">
            <stop offset="0%" stop-color="rgba(255,255,255,0.75)" />
            <stop offset="60%" stop-color="rgba(255,255,255,0.12)" />
            <stop offset="100%" stop-color="rgba(255,255,255,0)" />
          </radialGradient>
          <linearGradient :id="`sh-${uid}`" x1="50%" y1="0%" x2="50%" y2="100%">
            <stop offset="0%" stop-color="rgba(255,255,255,0.08)" />
            <stop offset="100%" stop-color="rgba(21,20,15,0.28)" />
          </linearGradient>
        </defs>

        <!-- Wash behind everything, clipped to the blob shape. -->
        <rect
          x="0" y="0" width="100" height="100"
          :fill="fallbackBg"
          :clip-path="`url(#clip-${uid})`"
        />
        <!-- Optional photo, clipped to the blob. -->
        <image
          v-if="image"
          :href="image"
          x="0" y="0" width="100" height="100"
          preserveAspectRatio="xMidYMid slice"
          :clip-path="`url(#clip-${uid})`"
        />
        <!-- Top-to-bottom shading. Lighter when the pebble is active. -->
        <path :d="path" :fill="`url(#sh-${uid})`" :opacity="active ? 0.3 : 0.5" />
        <!-- Specular highlight (top-left). -->
        <path :d="path" :fill="`url(#hi-${uid})`" />
        <!-- Contour stroke: subtle ink-tinted when inactive, lime when active. -->
        <path
          :d="path"
          fill="none"
          :stroke="active ? t.lime : inactiveStroke"
          :stroke-width="active ? 2.8 : 1.0"
        />
        <!-- Active-only dashed gold ring. -->
        <path
          v-if="active"
          :d="path"
          fill="none"
          stroke="#D9A520"
          stroke-width="1.2"
          stroke-dasharray="4 3"
          opacity="0.85"
        />
      </svg>
    </div>

    <div class="caption">
      <!-- Inactive: plain ink label. Active: lime square chip with gold border. -->
      <div
        v-if="active"
        class="label-chip"
        :style="{ background: t.lime, border: '2px solid #D9A520' }"
      >{{ label }}</div>
      <div
        v-else
        class="label-text"
        :style="{ color: t.ink }"
      >{{ label }}</div>

      <div v-if="sublabel" class="sublabel" :style="{ color: t.ink70 }">{{ sublabel }}</div>
    </div>
  </button>
</template>

<style scoped>
.pebble {
  cursor: pointer;
  border: none;
  background: transparent;
  padding: 0;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 10px;
  font-family: inherit;
}
.blob-wrap {
  transition: transform 0.45s cubic-bezier(0.34, 1.4, 0.64, 1), filter 0.3s ease;
}
.caption {
  text-align: center;
  padding-bottom: 2px;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 4px;
}
.label-text {
  font-size: 16px;
  font-weight: 700;
  letter-spacing: -0.01em;
  line-height: 1.2;
  transition: color 0.3s;
}
.label-chip {
  display: inline-block;
  border-radius: 6px;
  padding: 4px 10px;
  font-size: 15px;
  font-weight: 800;
  letter-spacing: -0.01em;
  color: #000;
  line-height: 1.25;
  transition: all 0.25s;
}
.sublabel {
  font-size: 14px;
  font-weight: 500;
}
</style>
