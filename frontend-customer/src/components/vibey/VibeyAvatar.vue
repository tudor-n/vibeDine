<script setup lang="ts">
// CSS/markup-only Vibey avatar — no image asset. Palette-driven via the
// `tokens` prop (not useTheme() internally) so it stays a pure presentational
// piece the parent can re-skin (white-label). Every dimension scales off the
// `size` prop relative to the 44px baseline, so the same markup reproduces both
// the cart-block avatar (44) and the sheet-header avatar (40).
//
// `celebrating` flips Vibey into a happy state — the eyes curve into smiles and
// the whole avatar hops side to side — used briefly when a recommendation is
// added to the cart.
import { computed } from 'vue'
import type { ThemeTokens } from '@/design/tokens'

const props = withDefaults(
  defineProps<{
    size?: number
    tokens: ThemeTokens
    celebrating?: boolean
  }>(),
  { size: 44, celebrating: false },
)

const scale = computed(() => props.size / 44)
const px = (n: number) => `${Math.round(n * scale.value)}px`

const bodyRadius = computed(() => px(15))
const eyeGap = computed(() => px(7))
const stemHeight = computed(() => Math.round(7 * scale.value))
const stemTop = computed(() => -stemHeight.value)
const tipTop = computed(() => -(stemHeight.value + 3))

// The glossy, brand-tinted body: a top-left white sheen over a vertical
// light-to-dark ramp of the tenant's accent (all derived from `accent`, so it
// re-skins with the palette).
const bodyStyle = computed(() => {
  const a = props.tokens.accent
  return {
    width: px(44),
    height: px(44),
    borderRadius: bodyRadius.value,
    gap: eyeGap.value,
    background: [
      'radial-gradient(115% 85% at 28% 14%, rgba(255,255,255,0.55), rgba(255,255,255,0) 56%)',
      `linear-gradient(158deg, color-mix(in srgb, ${a} 80%, white) 0%, ${a} 50%, color-mix(in srgb, ${a} 82%, black) 100%)`,
    ].join(', '),
    boxShadow: `0 8px 22px -6px color-mix(in srgb, ${a} 70%, transparent), inset 0 1px 0 rgba(255,255,255,0.25)`,
  }
})

// Eyes: tall rounded "pill" capsules at rest; when celebrating they become
// upward "smile" arcs (a stroked half-capsule with no bottom edge). Colour is
// the theme ink so they read dark on a light-theme body and light on a dark one.
const eyeStyle = computed(() => {
  const ink = props.tokens.ink
  if (props.celebrating) {
    const stroke = Math.max(2, Math.round(2.4 * scale.value))
    return {
      width: px(12),
      height: px(7),
      background: 'transparent',
      border: `${stroke}px solid ${ink}`,
      borderBottom: 'none',
      borderRadius: '999px 999px 0 0',
    }
  }
  return {
    width: px(7),
    height: px(13),
    background: ink,
    borderRadius: '999px',
  }
})
</script>

<template>
  <div class="vibey-avatar" :class="{ 'is-celebrating': celebrating }" :style="{ width: `${size}px`, height: `${size}px` }">
    <div
      class="vibey-antenna-stem"
      :style="{ height: `${stemHeight}px`, top: `${stemTop}px`, background: tokens.ink40 }"
    />
    <div
      class="vibey-antenna-tip"
      :style="{ top: `${tipTop}px`, background: tokens.lime }"
    />
    <div class="vibey-body" :style="bodyStyle">
      <span
        v-for="i in 2"
        :key="i"
        class="vibey-eye"
        :style="eyeStyle"
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
/* Happy hop overrides the idle bob for the celebration's duration. */
.vibey-avatar.is-celebrating {
  animation: vibey-jump 1.1s ease-in-out;
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
  animation: vibey-blink 4s ease-in-out infinite;
}
/* A smiling Vibey doesn't blink — the arcs stay put while it hops. */
.vibey-avatar.is-celebrating .vibey-eye {
  animation: none;
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
/* A cheerful side-to-side hop with a little tilt and lift. */
@keyframes vibey-jump {
  0%   { transform: translateX(0) translateY(0) rotate(0deg); }
  14%  { transform: translateX(-4px) translateY(-3px) rotate(-9deg); }
  30%  { transform: translateX(4px) translateY(0) rotate(9deg); }
  46%  { transform: translateX(-3px) translateY(-3px) rotate(-7deg); }
  62%  { transform: translateX(3px) translateY(0) rotate(7deg); }
  78%  { transform: translateX(-2px) translateY(-2px) rotate(-4deg); }
  100% { transform: translateX(0) translateY(0) rotate(0deg); }
}

@media (prefers-reduced-motion: reduce) {
  .vibey-avatar,
  .vibey-avatar.is-celebrating,
  .vibey-antenna-tip,
  .vibey-eye {
    animation: none;
  }
  /* Reduced motion keeps the happy smile (a static expression) but drops the hop. */
}
</style>
