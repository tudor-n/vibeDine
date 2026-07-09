<script setup lang="ts">
// CSS/markup-only Vibey avatar — no image asset. Palette-driven via the
// `tokens` prop (not useTheme() internally) so it stays a pure presentational
// piece the parent can re-skin. Every dimension scales off the `size` prop
// relative to the 44px baseline from the design handoff, so the same markup
// reproduces both the cart-block avatar (44) and the sheet-header avatar (40)
// pixel-for-pixel.
import { computed } from 'vue'
import type { ThemeTokens } from '@/design/tokens'

const props = withDefaults(
  defineProps<{
    size?: number
    tokens: ThemeTokens
  }>(),
  { size: 44 },
)

const scale = computed(() => props.size / 44)
const bodyRadius = computed(() => Math.round(14 * scale.value))
const eyeSize = computed(() => Math.round(8 * scale.value))
const eyeGap = computed(() => Math.round(6 * scale.value))
const stemHeight = computed(() => Math.round(7 * scale.value))
const stemTop = computed(() => -stemHeight.value)
const tipTop = computed(() => -(stemHeight.value + 3))
</script>

<template>
  <div class="vibey-avatar" :style="{ width: `${size}px`, height: `${size}px` }">
    <div
      class="vibey-antenna-stem"
      :style="{ height: `${stemHeight}px`, top: `${stemTop}px`, background: tokens.ink40 }"
    />
    <div
      class="vibey-antenna-tip"
      :style="{ top: `${tipTop}px`, background: tokens.lime }"
    />
    <div
      class="vibey-body"
      :style="{
        width: `${size}px`,
        height: `${size}px`,
        borderRadius: `${bodyRadius}px`,
        gap: `${eyeGap}px`,
        background: tokens.accent,
        boxShadow: `0 8px 22px -6px color-mix(in srgb, ${tokens.accent} 70%, transparent)`,
      }"
    >
      <span
        v-for="i in 2"
        :key="i"
        class="vibey-eye"
        :style="{ width: `${eyeSize}px`, height: `${eyeSize}px`, background: tokens.onAccent }"
      />
    </div>
  </div>
</template>

<style scoped>
.vibey-avatar {
  position: relative;
  flex-shrink: 0;
  animation: vibey-botbob 3.4s ease-in-out infinite;
}
.vibey-antenna-stem {
  position: absolute;
  left: 50%;
  transform: translateX(-50%);
  width: 2px;
}
.vibey-antenna-tip {
  position: absolute;
  left: 50%;
  transform: translateX(-50%);
  width: 6px;
  height: 6px;
  border-radius: 50%;
  animation: vibey-ant 2.4s ease-in-out infinite;
}
.vibey-body {
  display: flex;
  align-items: center;
  justify-content: center;
}
.vibey-eye {
  border-radius: 50%;
  animation: vibey-blink 4s ease-in-out infinite;
}

@keyframes vibey-botbob {
  0%, 100% { transform: translateY(0); }
  50% { transform: translateY(-2.5px); }
}
@keyframes vibey-blink {
  0%, 90%, 100% { transform: scaleY(1); }
  95% { transform: scaleY(0.12); }
}
@keyframes vibey-ant {
  0%, 100% { opacity: 0.5; transform: scale(0.8); }
  50% { opacity: 1; transform: scale(1.25); }
}

@media (prefers-reduced-motion: reduce) {
  .vibey-avatar,
  .vibey-antenna-tip,
  .vibey-eye {
    animation: none;
  }
}
</style>
