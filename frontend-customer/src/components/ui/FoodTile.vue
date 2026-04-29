<script setup lang="ts">
import { computed } from 'vue'
import { FOOD_PALETTES, type FoodTone } from '@/design/tokens'

const props = withDefaults(
  defineProps<{
    tone?: FoodTone
    label?: string
    image?: string | null
    radius?: number
  }>(),
  { tone: 'terra', label: '', image: null, radius: 14 },
)

const palette = computed(() => FOOD_PALETTES[props.tone] ?? FOOD_PALETTES.terra)
</script>

<template>
  <div
    class="food-tile"
    :style="{
      background: palette.c,
      borderRadius: `${radius}px`,
    }"
  >
    <img
      v-if="image"
      :src="image"
      :alt="label || ''"
      class="photo"
    />
    <div
      v-else
      class="gradient"
      :style="{
        background:
          `radial-gradient(60% 50% at 30% 30%, ${palette.a} 0%, transparent 60%),` +
          ` radial-gradient(50% 40% at 75% 75%, ${palette.b} 0%, transparent 60%)`,
      }"
    />
    <span v-if="label && !image" class="label">{{ label }}</span>
  </div>
</template>

<style scoped>
.food-tile {
  position: relative;
  overflow: hidden;
  width: 100%;
  height: 100%;
}
.gradient {
  position: absolute;
  inset: -20%;
  filter: blur(2px);
}
.photo {
  position: absolute;
  inset: 0;
  width: 100%;
  height: 100%;
  object-fit: cover;
}
.label {
  position: absolute;
  left: 8px;
  bottom: 8px;
  font-size: 10px;
  font-weight: 700;
  letter-spacing: 0.08em;
  color: rgba(21, 20, 15, 0.55);
  text-transform: uppercase;
}
</style>
