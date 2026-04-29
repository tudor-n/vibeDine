<script setup lang="ts">
import { computed } from 'vue'
import { T, TD, type ThemeTokens } from '@/design/tokens'

const props = withDefaults(
  defineProps<{
    theme?: 'light' | 'dark'
    bg?: string
    /** Disable the inner padding-top so the screen can render edge-to-edge. */
    flush?: boolean
  }>(),
  { theme: 'light', flush: false },
)

const t = computed<ThemeTokens>(() => (props.theme === 'dark' ? TD : T))

const noiseSvg = computed(() => {
  if (props.theme === 'dark') {
    return `url("data:image/svg+xml;utf8,<svg xmlns='http://www.w3.org/2000/svg' width='180' height='180'><filter id='n'><feTurbulence type='fractalNoise' baseFrequency='1.1' numOctaves='2' stitchTiles='stitch'/><feColorMatrix values='0 0 0 0 0.9  0 0 0 0 0.88  0 0 0 0 0.85  0 0 0 0.06 0'/></filter><rect width='100%25' height='100%25' filter='url(%23n)'/></svg>")`
  }
  return `url("data:image/svg+xml;utf8,<svg xmlns='http://www.w3.org/2000/svg' width='180' height='180'><filter id='n'><feTurbulence type='fractalNoise' baseFrequency='1.1' numOctaves='2' stitchTiles='stitch'/><feColorMatrix values='0 0 0 0 0.08  0 0 0 0 0.07  0 0 0 0 0.06  0 0 0 0.05 0'/></filter><rect width='100%25' height='100%25' filter='url(%23n)'/></svg>")`
})
</script>

<template>
  <div
    class="phone-screen"
    :style="{
      background: props.bg ?? t.paper,
      color: t.ink,
      fontFamily: t.sans,
      paddingTop: flush ? 0 : `${t.statusClear}px`,
    }"
  >
    <div
      class="noise"
      :style="{
        backgroundImage: noiseSvg,
        opacity: theme === 'dark' ? 0.2 : 0.28,
        mixBlendMode: theme === 'dark' ? 'screen' : 'multiply',
      }"
    />
    <slot />
  </div>
</template>

<style scoped>
.phone-screen {
  position: relative;
  width: 100%;
  height: 100%;
  overflow: hidden;
  letter-spacing: -0.005em;
  display: flex;
  flex-direction: column;
}
.noise {
  position: absolute;
  inset: 0;
  pointer-events: none;
}
</style>
