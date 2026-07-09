<script setup lang="ts">
import { computed } from 'vue'
import { useTheme } from '@/composables/useTheme'
import { BLOB_PATHS } from '@/design/tokens'
import { lighten } from '@shared/branding/derive'

const props = withDefaults(defineProps<{ size?: number; onDark?: boolean }>(), {
  size: 22,
  onDark: false,
})

const { tokens: t } = useTheme()

// Same pebble outline as the admin lockup / BrandLogo — kept in sync verbatim.
const PEBBLE_PATH = BLOB_PATHS[0]

// Lightened accent for "Dine" when rendered over a dark/photo background,
// so it stays legible instead of using the raw (often dark-on-dark) accent.
const dineColor = computed(() => (props.onDark ? lighten(t.value.accent, 0.2) : t.value.accent))
const byColor = computed(() => (props.onDark ? 'rgba(255,255,255,0.62)' : t.value.ink40))
const vibeColor = computed(() => (props.onDark ? '#fff' : t.value.ink70))
</script>

<template>
  <span class="lockup" :style="{ fontFamily: t.sans }">
    <span class="lockup-by" :style="{ color: byColor }">by</span>
    <svg class="lockup-pebble" viewBox="0 0 100 100" :width="size" :height="size" aria-hidden="true">
      <defs>
        <linearGradient id="vibelockup-grad" x1="0" y1="0" x2="0.4" y2="1">
          <stop offset="0%" :stop-color="t.accent" />
          <stop offset="100%" :stop-color="t.accentDk" />
        </linearGradient>
        <radialGradient id="vibelockup-spec" cx="0.32" cy="0.28" r="0.5">
          <stop offset="0%" stop-color="rgba(255,255,255,0.55)" />
          <stop offset="100%" stop-color="rgba(255,255,255,0)" />
        </radialGradient>
        <clipPath id="vibelockup-clip">
          <path :d="PEBBLE_PATH" />
        </clipPath>
      </defs>
      <path :d="PEBBLE_PATH" fill="url(#vibelockup-grad)" />
      <ellipse cx="32" cy="28" rx="32" ry="22" fill="url(#vibelockup-spec)" clip-path="url(#vibelockup-clip)" />
      <path :d="PEBBLE_PATH" fill="none" :stroke="t.lime" stroke-width="2.6" />
    </svg>
    <span class="lockup-text">
      <span :style="{ color: vibeColor }">vibe</span><span :style="{ color: dineColor }">Dine</span>
    </span>
  </span>
</template>

<style scoped>
.lockup {
  font-weight: 500;
  display: inline-flex;
  align-items: center;
  gap: 7px;
  letter-spacing: -0.03em;
  user-select: none;
}
.lockup-by {
  font-size: 12px;
  font-weight: 500;
}
.lockup-pebble {
  flex-shrink: 0;
  filter: drop-shadow(0 3px 6px rgba(21, 20, 15, 0.2));
}
.lockup-text {
  font-size: 16px;
  line-height: 1;
}
</style>
