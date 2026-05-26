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
    <template v-for="(s, idx) in steps" :key="s.num">
      <li
        class="step-bar-item"
        :class="{
          'is-current': s.num === current,
          'is-done': s.num < current,
        }"
        :aria-current="s.num === current ? 'step' : undefined"
      >
        <span class="step-bar-num">
          <span v-if="s.num < current" aria-hidden="true">✓</span>
          <span v-else>{{ s.num }}</span>
        </span>
        <span class="step-bar-label">{{ s.label }}</span>
      </li>
      <span v-if="idx < steps.length - 1" class="step-bar-sep" aria-hidden="true"></span>
    </template>
  </ol>
</template>

<style scoped>
.step-bar {
  display: flex;
  align-items: center;
  gap: 8px;
  list-style: none;
  padding: 0;
  margin: 0 0 24px 0;
}
.step-bar-item {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  padding: 6px 12px 6px 6px;
  border-radius: 999px;
  background: transparent;
  color: var(--ink40);
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 11px;
  text-transform: uppercase;
  letter-spacing: 0.14em;
  font-weight: 600;
  transition: color 0.15s ease, background 0.15s ease;
}
.step-bar-item.is-done {
  color: var(--ink70);
}
.step-bar-item.is-current {
  color: var(--ink);
  background: var(--paperHi);
  border: 1px solid var(--ink12);
  padding: 5px 12px 5px 5px;
}
.step-bar-num {
  display: inline-grid;
  place-items: center;
  width: 22px;
  height: 22px;
  border-radius: 50%;
  background: var(--ink06);
  color: inherit;
  font-weight: 700;
  font-size: 11px;
}
.is-current .step-bar-num {
  background: var(--ink);
  color: var(--paperHi);
}
.is-done .step-bar-num {
  background: var(--lime);
  color: var(--onLime);
}
.step-bar-sep {
  flex: 0 0 auto;
  width: 14px;
  height: 1px;
  background: var(--ink12);
}
@media (max-width: 540px) {
  .step-bar-label { display: none; }
  .step-bar-item.is-current { padding: 5px 6px; }
  .step-bar-item { padding: 6px; }
}
</style>
