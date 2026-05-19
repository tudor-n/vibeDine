<script setup lang="ts">
defineProps<{
  current: 1 | 2 | 3
}>()

const steps = [
  { num: 1, label: 'Fotografii' },
  { num: 2, label: 'Categorii' },
  { num: 3, label: 'Revizuire' },
] as const
</script>

<template>
  <ol class="step-bar" aria-label="Pași importul meniului">
    <li
      v-for="s in steps"
      :key="s.num"
      class="step-bar-item"
      :class="{
        'is-current': s.num === current,
        'is-done': s.num < current,
      }"
      :aria-current="s.num === current ? 'step' : undefined"
    >
      <span class="step-bar-num">{{ s.num }}</span>
      <span class="step-bar-label">{{ s.label }}</span>
    </li>
  </ol>
</template>

<style scoped>
.step-bar {
  display: flex;
  gap: 12px;
  list-style: none;
  padding: 0;
  margin: 0 0 32px 0;
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 11px;
  text-transform: uppercase;
  letter-spacing: 0.18em;
}
.step-bar-item {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 8px 14px;
  border: 1px solid var(--ink12);
  border-radius: 999px;
  background: var(--paper);
  color: var(--ink40);
}
.step-bar-item.is-done {
  color: var(--ink70);
  background: var(--paperHi);
}
.step-bar-item.is-current {
  color: var(--ink);
  background: var(--paperHi);
  border-color: var(--ink);
  box-shadow: 0 0 0 2px var(--lime) inset;
}
.step-bar-num {
  display: inline-grid;
  place-items: center;
  width: 18px;
  height: 18px;
  border-radius: 50%;
  background: var(--ink06);
  color: inherit;
  font-weight: 600;
}
.is-current .step-bar-num {
  background: var(--lime);
  color: var(--onLime);
}
</style>
