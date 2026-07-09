<script setup lang="ts">
withDefaults(defineProps<{ size?: number; onDark?: boolean }>(), {
  size: 22,
  onDark: false,
})

// Same pebble outline as BrandLogo.vue's PEBBLE_PATH, kept in sync verbatim.
const PEBBLE_PATH =
  'M50,5 C68,3 88,16 92,36 C97,58 84,82 64,92 C44,102 18,94 9,74 C0,54 8,26 24,14 C36,5 42,6 50,5 Z'
</script>

<template>
  <span class="lockup" :class="{ 'on-dark': onDark }">
    <span class="lockup-by">by</span>
    <svg class="lockup-pebble" viewBox="0 0 100 100" :width="size" :height="size" aria-hidden="true">
      <defs>
        <linearGradient id="lockup-grad" x1="0" y1="0" x2="0.4" y2="1">
          <stop offset="0%" stop-color="var(--accent)" />
          <stop offset="100%" stop-color="var(--accentDk)" />
        </linearGradient>
        <radialGradient id="lockup-spec" cx="0.32" cy="0.28" r="0.5">
          <stop offset="0%" stop-color="rgba(255,255,255,0.55)" />
          <stop offset="100%" stop-color="rgba(255,255,255,0)" />
        </radialGradient>
        <clipPath id="lockup-clip">
          <path :d="PEBBLE_PATH" />
        </clipPath>
      </defs>
      <path :d="PEBBLE_PATH" fill="url(#lockup-grad)" />
      <ellipse cx="32" cy="28" rx="32" ry="22" fill="url(#lockup-spec)" clip-path="url(#lockup-clip)" />
      <path :d="PEBBLE_PATH" fill="none" stroke="var(--lime)" stroke-width="2.6" />
    </svg>
    <span class="lockup-text">
      <span class="lockup-vibe">vibe</span><span class="lockup-dine">Dine</span>
    </span>
  </span>
</template>

<style scoped>
.lockup {
  font-family: 'Inter Tight', -apple-system, system-ui, sans-serif;
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
  color: var(--ink40);
}
.lockup-pebble {
  flex-shrink: 0;
  filter: drop-shadow(0 3px 6px rgba(21, 20, 15, 0.2));
}
.lockup-text {
  font-size: 16px;
  line-height: 1;
}
.lockup-vibe {
  color: var(--ink70);
}
.lockup-dine {
  color: var(--accent);
}
.lockup.on-dark .lockup-by {
  color: rgba(255, 255, 255, 0.62);
}
.lockup.on-dark .lockup-vibe {
  color: #fff;
}
.lockup.on-dark .lockup-dine {
  color: var(--accent-light, #ff5c7e);
}
</style>
