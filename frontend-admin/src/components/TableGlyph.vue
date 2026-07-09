<script setup lang="ts">
import { computed } from 'vue'
import { seatSlots, tabletopDims, type TableShape } from '@/utils/tableSeats'

// A static preview of a floor-plan table: the tabletop in its chosen shape ringed
// with one chair per seat, and the seat count in the middle. Used in the add /
// edit dialogs (and the shape picker) so the icon visibly adapts to the choices.
const props = withDefaults(defineProps<{ capacity: number; shape?: TableShape; size?: number }>(), {
  shape: 'square',
  size: 72,
})

const dims = computed(() => tabletopDims(props.size, props.shape))
const seats = computed(() => seatSlots(props.capacity, props.shape))
const topStyle = computed(() => ({
  width: `${dims.value.w}px`,
  height: `${dims.value.h}px`,
  borderRadius: dims.value.radius,
  fontSize: `${Math.round(props.size * 0.34)}px`,
}))
// Leave room for the chairs that sit outside the tabletop edges.
const boxStyle = computed(() => ({
  width: `${dims.value.w + 30}px`,
  height: `${dims.value.h + 30}px`,
}))
</script>

<template>
  <div class="glyph" :style="boxStyle">
    <div class="glyph-top" :style="topStyle">
      <span v-for="s in seats" :key="s.key" class="glyph-seat" :style="s.style"></span>
      <span class="glyph-num">{{ capacity }}</span>
    </div>
  </div>
</template>

<style scoped>
.glyph {
  display: grid;
  place-items: center;
}
.glyph-top {
  position: relative;
  background: var(--card);
  border: 1.5px solid var(--ink12);
  box-shadow: var(--shadow-soft);
  display: grid;
  place-items: center;
  color: var(--ink);
}
.glyph-seat {
  position: absolute;
  background: var(--ink40);
  border-radius: 3px;
}
.glyph-num {
  font-family: 'Inter Tight', sans-serif;
  font-weight: 700;
  line-height: 1;
}
</style>
