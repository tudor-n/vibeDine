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

const emit = defineEmits<{ (e: 'click'): void }>()

const t = computed<ThemeTokens>(() => (props.theme === 'dark' ? TD : T))
const path = computed(() => BLOB_PATHS[props.pathIndex % BLOB_PATHS.length])
const uid = computed(() => Math.random().toString(36).slice(2, 9))

const dropShadow = computed(() => {
  if (props.active) {
    return props.theme === 'dark'
      ? 'drop-shadow(0 16px 22px rgba(232,37,90,0.45)) drop-shadow(0 4px 8px rgba(0,0,0,0.5))'
      : 'drop-shadow(0 16px 22px rgba(180,40,10,0.35)) drop-shadow(0 4px 8px rgba(180,40,10,0.16))'
  }
  return props.theme === 'dark'
    ? 'drop-shadow(0 8px 14px rgba(0,0,0,0.55)) drop-shadow(0 2px 4px rgba(0,0,0,0.35))'
    : 'drop-shadow(0 8px 14px rgba(21,20,15,0.18)) drop-shadow(0 2px 4px rgba(21,20,15,0.08))'
})

const fallbackBg = computed(() => (props.theme === 'dark' ? '#3a2418' : '#e8d5b9'))
</script>

<template>
  <button class="pebble" :aria-pressed="active" @click="emit('click')">
    <div
      class="blob-wrap"
      :style="{
        transform: active ? 'scale(1.06) translateY(-4px)' : 'scale(1) translateY(0)',
        filter: dropShadow,
      }"
    >
      <svg :viewBox="'0 0 100 100'" :width="size" :height="size" style="display:block;overflow:visible">
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
        <rect x="0" y="0" width="100" height="100" :fill="fallbackBg" :clip-path="`url(#clip-${uid})`" />
        <image
          v-if="image"
          :href="image"
          x="0" y="0" width="100" height="100"
          preserveAspectRatio="xMidYMid slice"
          :clip-path="`url(#clip-${uid})`"
        />
        <path :d="path" :fill="`url(#sh-${uid})`" :opacity="active ? 0.3 : 0.5" />
        <path :d="path" :fill="`url(#hi-${uid})`" />
        <path
          :d="path"
          fill="none"
          :stroke="t.accent"
          :stroke-width="active ? 2.5 : 1.6"
        />
        <path
          v-if="active"
          :d="path"
          fill="none"
          :stroke="theme === 'dark' ? 'rgba(245,240,232,0.55)' : 'rgba(255,244,236,0.55)'"
          stroke-width="3.5"
        />
      </svg>
    </div>

    <div class="caption">
      <div
        class="label"
        :style="{ color: t.accent }"
      >{{ label }}</div>
      <div v-if="sublabel" class="sublabel" :style="{ color: t.ink40 }">{{ sublabel }}</div>
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
}
.blob-wrap {
  transition: transform 0.45s cubic-bezier(0.34, 1.4, 0.64, 1), filter 0.3s ease;
}
.caption {
  text-align: center;
  padding-bottom: 2px;
}
.label {
  font-size: 16px;
  font-weight: 700;
  letter-spacing: -0.01em;
  line-height: 1.2;
  transition: color 0.3s;
}
.sublabel {
  font-size: 14px;
  font-weight: 500;
  margin-top: 3px;
}
</style>
