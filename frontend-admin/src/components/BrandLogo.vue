<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'

withDefaults(defineProps<{ wordmark?: boolean }>(), { wordmark: true })

// Food photos cycled inside the pebble on hover. Ported verbatim from the
// vibeDine landing page so the mark stays identical across both apps.
const BRAND_FOOD = [
  'https://images.unsplash.com/photo-1551183053-bf91a1d81141?w=600&h=600&fit=crop&auto=format&q=80',
  'https://images.unsplash.com/photo-1565299624946-b28f40a0ae38?w=600&h=600&fit=crop&auto=format&q=80',
  'https://images.unsplash.com/photo-1568901346375-23c9450c58cd?w=600&h=600&fit=crop&auto=format&q=80',
  'https://images.unsplash.com/photo-1546069901-ba9599a7e63c?w=600&h=600&fit=crop&auto=format&q=80',
  'https://images.unsplash.com/photo-1571877227200-a0d98ea607e9?w=600&h=600&fit=crop&auto=format&q=80',
  'https://images.unsplash.com/photo-1495474472287-4d71bcdd2085?w=600&h=600&fit=crop&auto=format&q=80',
  'https://images.unsplash.com/photo-1547592180-85f173990554?w=600&h=600&fit=crop&auto=format&q=80',
  'https://images.unsplash.com/photo-1510812431401-41d2bd2722f3?w=600&h=600&fit=crop&auto=format&q=80',
  'https://images.unsplash.com/photo-1546833999-b9f581a1996d?w=600&h=600&fit=crop&auto=format&q=80',
  'https://images.unsplash.com/photo-1559847844-5315695dadae?w=600&h=600&fit=crop&auto=format&q=80',
  'https://images.unsplash.com/photo-1565958011703-44f9829ba187?w=600&h=600&fit=crop&auto=format&q=80',
  'https://images.unsplash.com/photo-1551024506-0bccd828d307?w=600&h=600&fit=crop&auto=format&q=80',
]

const PEBBLE_PATH =
  'M50,5 C68,3 88,16 92,36 C97,58 84,82 64,92 C44,102 18,94 9,74 C0,54 8,26 24,14 C36,5 42,6 50,5 Z'

const foodIdx = ref<number | null>(null)
let timer: ReturnType<typeof setInterval> | null = null

onMounted(() => {
  // Warm the browser cache so the first hover doesn't flash empty.
  BRAND_FOOD.forEach((u) => {
    const i = new Image()
    i.src = u
  })
})

function onEnter() {
  foodIdx.value = Math.floor(Math.random() * BRAND_FOOD.length)
  timer = setInterval(() => {
    foodIdx.value = foodIdx.value === null ? 0 : (foodIdx.value + 1) % BRAND_FOOD.length
  }, 260)
}

function onLeave() {
  if (timer) {
    clearInterval(timer)
    timer = null
  }
  foodIdx.value = null
}

onBeforeUnmount(() => {
  if (timer) clearInterval(timer)
})
</script>

<template>
  <div class="brand" @mouseenter="onEnter" @mouseleave="onLeave">
    <svg class="brand-pebble" viewBox="0 0 100 100" width="36" height="36">
      <defs>
        <linearGradient id="brand-grad" x1="0" y1="0" x2="0.4" y2="1">
          <stop offset="0%" stop-color="var(--accent)" />
          <stop offset="100%" stop-color="var(--accentDk)" />
        </linearGradient>
        <radialGradient id="brand-spec" cx="0.32" cy="0.28" r="0.5">
          <stop offset="0%" stop-color="rgba(255,255,255,0.55)" />
          <stop offset="100%" stop-color="rgba(255,255,255,0)" />
        </radialGradient>
        <clipPath id="brand-clip">
          <path :d="PEBBLE_PATH" />
        </clipPath>
      </defs>
      <path :d="PEBBLE_PATH" fill="url(#brand-grad)" />
      <image
        v-if="foodIdx !== null"
        x="-4"
        y="-4"
        width="108"
        height="108"
        preserveAspectRatio="xMidYMid slice"
        clip-path="url(#brand-clip)"
        :href="BRAND_FOOD[foodIdx]"
      />
      <ellipse cx="32" cy="28" rx="32" ry="22" fill="url(#brand-spec)" clip-path="url(#brand-clip)" />
      <path :d="PEBBLE_PATH" fill="none" stroke="var(--lime)" stroke-width="2.6" />
      <path
        :d="PEBBLE_PATH"
        fill="none"
        stroke="var(--gold)"
        stroke-width="1.1"
        stroke-dasharray="4 3"
        opacity="0.85"
      />
    </svg>
    <span v-if="wordmark" class="brand-text">
      <span class="vibe">v</span>
      <span class="cutlery knife" />
      <span class="vibe">be</span>
      <span class="dine">D</span>
      <span class="cutlery fork" />
      <span class="dine">ne.</span>
    </span>
  </div>
</template>

<style scoped>
.brand {
  font-family: 'Inter Tight', -apple-system, system-ui, sans-serif;
  font-weight: 500;
  font-size: 22px;
  letter-spacing: -0.03em;
  display: flex;
  align-items: center;
  gap: 10px;
  cursor: pointer;
  user-select: none;
}
.brand-pebble {
  flex-shrink: 0;
  filter: drop-shadow(0 4px 10px rgba(21, 20, 15, 0.18));
  transition: transform 350ms cubic-bezier(0.34, 1.4, 0.64, 1);
}
.brand:hover .brand-pebble {
  transform: scale(1.08);
}
:global([data-theme='dark']) .brand-pebble {
  filter: drop-shadow(0 4px 10px rgba(0, 0, 0, 0.5));
}
.brand .vibe {
  color: var(--ink);
}
.brand .dine {
  color: var(--accent);
}
.brand-text {
  display: inline-flex;
  align-items: baseline;
  line-height: 1;
}
.brand .cutlery {
  display: inline-block;
  vertical-align: baseline;
  -webkit-mask-repeat: no-repeat;
  mask-repeat: no-repeat;
  -webkit-mask-position: center bottom;
  mask-position: center bottom;
  -webkit-mask-size: contain;
  mask-size: contain;
  margin: 0 0.02em;
  transform: translateY(0.04em);
}
.brand .cutlery.knife {
  width: 0.115em;
  height: 0.94em;
  background: var(--ink);
  -webkit-mask-image: url('/images/cutlery-knife.png');
  mask-image: url('/images/cutlery-knife.png');
}
.brand .cutlery.fork {
  width: 0.175em;
  height: 0.94em;
  background: var(--accent);
  -webkit-mask-image: url('/images/cutlery-fork.png');
  mask-image: url('/images/cutlery-fork.png');
}
</style>
