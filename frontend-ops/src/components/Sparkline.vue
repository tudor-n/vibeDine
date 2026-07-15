<script setup lang="ts">
import { computed } from 'vue'

const props = withDefaults(
  defineProps<{
    values: number[]
    color?: string
    height?: number
  }>(),
  { color: 'var(--accent)', height: 48 },
)

const W = 240

const path = computed(() => {
  const v = props.values
  if (v.length === 0) return ''
  const min = Math.min(...v)
  const max = Math.max(...v)
  const range = max - min || 1
  const step = v.length > 1 ? W / (v.length - 1) : W
  return v
    .map((val, i) => {
      const x = i * step
      const y = props.height - ((val - min) / range) * (props.height - 6) - 3
      return `${i === 0 ? 'M' : 'L'}${x.toFixed(1)},${y.toFixed(1)}`
    })
    .join(' ')
})

const areaPath = computed(() => (path.value ? `${path.value} L${W},${props.height} L0,${props.height} Z` : ''))
</script>

<template>
  <svg
    :viewBox="`0 0 ${W} ${height}`"
    preserveAspectRatio="none"
    class="w-full"
    :style="{ height: height + 'px' }"
  >
    <path v-if="areaPath" :d="areaPath" :fill="color" opacity="0.12" />
    <path v-if="path" :d="path" :stroke="color" fill="none" stroke-width="1.5" vector-effect="non-scaling-stroke" />
  </svg>
</template>
