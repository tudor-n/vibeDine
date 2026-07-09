<script setup lang="ts">
import { computed } from 'vue'
import { useTheme } from '@/composables/useTheme'

const props = withDefaults(
  defineProps<{
    theme?: 'light' | 'dark'
    bg?: string
    color?: string
    /** Optional override for the inline arrow circle background. */
    arrowBg?: string
    /** Optional override for the inline arrow glyph colour. */
    arrowColor?: string
    arrow?: boolean
    disabled?: boolean
    fontSize?: number
  }>(),
  { theme: 'light', arrow: true, disabled: false, fontSize: 19 },
)

const emit = defineEmits<{ (e: 'click', ev: MouseEvent): void }>()

const { tokens: t, mode } = useTheme()
const bg = computed(() => props.bg ?? t.value.accent)
const color = computed(() => props.color ?? t.value.onAccent)
// When the button is the brand accent, the arrow circle uses the on-accent
// foreground; otherwise we default to the foreground colour the caller chose
// (which works for the lime "Revendică oferta" CTA in the flash deal screen).
const arrowBg = computed(() => {
  if (props.arrowBg) return props.arrowBg
  if (!props.bg) {
    // Default brand button — same logic as before.
    return mode.value === 'dark' ? t.value.ink : t.value.onAccent
  }
  return color.value
})
const arrowColor = computed(() => props.arrowColor ?? bg.value)

function onClick(ev: MouseEvent) {
  if (props.disabled) return
  emit('click', ev)
}
</script>

<template>
  <button
    class="big-btn"
    :disabled="disabled"
    :style="{
      background: bg,
      color,
      fontSize: `${fontSize}px`,
      boxShadow: disabled ? 'none' : `0 10px 24px -8px ${bg}66`,
      opacity: disabled ? 0.55 : 1,
      cursor: disabled ? 'not-allowed' : 'pointer',
      fontFamily: t.sans,
    }"
    @click="onClick"
  >
    <span class="label"><slot /></span>
    <span
      v-if="arrow"
      class="arrow"
      :style="{ background: arrowBg, color: arrowColor }"
      aria-hidden="true"
    >→</span>
  </button>
</template>

<style scoped>
.big-btn {
  width: 100%;
  padding: clamp(15px, 2.2vh, 20px) clamp(18px, 6vw, 24px);
  border: none;
  border-radius: 999px;
  font-weight: 700;
  letter-spacing: -0.01em;
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 12px;
  transition: transform 0.18s ease, box-shadow 0.18s ease;
  min-height: 56px;
}
.big-btn:not(:disabled):active {
  transform: scale(0.985);
}
.label {
  flex: 1;
  text-align: left;
}
.arrow {
  width: 38px;
  height: 38px;
  border-radius: 50%;
  display: grid;
  place-items: center;
  font-size: 20px;
  flex-shrink: 0;
  font-weight: 700;
}
</style>
