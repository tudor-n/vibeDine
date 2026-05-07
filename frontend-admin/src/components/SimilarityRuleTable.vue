<script setup lang="ts">

import type { SimilarityRule } from '@shared/types/models';

defineProps<{
    rules: SimilarityRule[]
    isLoading: boolean
}>();

const emit = defineEmits<{
    (e: 'delete', id: string): void;
}>();

</script>


<template>
  <div class="rules-shell">

    <!-- Loading -->
    <div v-if="isLoading && rules.length === 0" class="rules-state">
      <div class="eyebrow mb-3">Sincronizare · graf</div>
      <h3 class="display-title text-2xl mb-2">Se încarcă regulile...</h3>
      <p class="text-ink-70">AI-ul construiește harta de potriviri.</p>
    </div>

    <!-- Empty -->
    <div v-else-if="rules.length === 0" class="rules-state relative overflow-hidden">
      <div class="bloom bloom-accent" style="top: -60px; right: -60px; width: 240px; height: 240px; opacity: 0.20;" />
      <div class="bloom bloom-lime" style="bottom: -40px; left: -40px; width: 220px; height: 220px; opacity: 0.20;" />
      <div class="relative z-10">
        <div class="eyebrow mb-3">Goală</div>
        <h3 class="display-title text-3xl mb-3">Nicio regulă încă</h3>
        <p class="text-ink-70 max-w-md mx-auto">
          Mergeți la <span class="font-semibold text-ink">Configurare AI</span> pentru a
          genera reguli automate de recomandare.
        </p>
      </div>
    </div>

    <!-- Cards list -->
    <div v-else class="space-y-3">
      <article
        v-for="rule in rules"
        :key="rule.id"
        class="rule-card"
      >
        <div class="rule-row">
          <!-- Source pebble label -->
          <div class="rule-pebble rule-pebble-source">
            <span class="rule-pebble-eyebrow">când</span>
            <span class="rule-pebble-name">{{ rule.source_item_id }}</span>
          </div>

          <!-- Connector arrow -->
          <div class="rule-connector">
            <span class="rule-connector-line"></span>
            <span class="rule-connector-arrow">→</span>
            <span class="rule-connector-line"></span>
          </div>

          <!-- Target pebble label -->
          <div class="rule-pebble rule-pebble-target">
            <span class="rule-pebble-eyebrow rule-pebble-eyebrow-target">recomandăm</span>
            <span class="rule-pebble-name">+ {{ rule.target_item_id }}</span>
          </div>

          <!-- Score chip -->
          <div class="rule-score">
            <span class="rule-score-eyebrow">scor</span>
            <span class="rule-score-value">{{ (rule.score * 100).toFixed(0) }}%</span>
          </div>

          <!-- Delete -->
          <button
            class="rule-delete"
            type="button"
            aria-label="Șterge regula"
            @click="emit('delete', rule.id)"
          >
            <span>✕</span>
          </button>
        </div>

        <!-- Reason / AI explanation -->
        <div v-if="rule.reason" class="rule-reason">
          <span class="rule-ai-tag">✦ AI</span>
          <p class="rule-reason-text">"{{ rule.reason }}"</p>
        </div>
      </article>
    </div>
  </div>
</template>

<style scoped>
.rules-shell {
  position: relative;
}
.rules-state {
  background: var(--paperHi);
  border: 1px solid var(--ink12);
  border-radius: 28px;
  padding: 60px 24px;
  text-align: center;
}
.rule-card {
  background: var(--paperHi);
  border: 1px solid var(--ink12);
  border-radius: 24px;
  padding: 18px 20px;
  transition: box-shadow 0.18s ease, transform 0.18s ease;
}
.rule-card:hover {
  box-shadow: var(--shadow-soft);
  transform: translateY(-1px);
}
.rule-row {
  display: grid;
  grid-template-columns: 1fr auto 1fr auto auto;
  gap: 14px;
  align-items: center;
}
@media (max-width: 720px) {
  .rule-row {
    grid-template-columns: 1fr auto;
    gap: 10px;
  }
  .rule-connector { display: none; }
}
.rule-pebble {
  display: flex;
  flex-direction: column;
  gap: 4px;
  padding: 12px 16px;
  border-radius: 18px;
  min-width: 0;
}
.rule-pebble-source {
  background: var(--card);
  border: 1px solid var(--ink12);
  color: var(--ink);
}
.rule-pebble-target {
  background: var(--ink);
  color: var(--paperHi);
}
.rule-pebble-eyebrow {
  font-family: 'JetBrains Mono', monospace;
  font-size: 10px;
  text-transform: uppercase;
  letter-spacing: 0.18em;
  opacity: 0.6;
}
.rule-pebble-eyebrow-target {
  opacity: 0.7;
}
.rule-pebble-name {
  font-size: 16px;
  font-weight: 600;
  letter-spacing: -0.01em;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.rule-connector {
  display: flex;
  align-items: center;
  gap: 6px;
  color: var(--accent);
}
.rule-connector-line {
  display: inline-block;
  width: 18px;
  height: 2px;
  background: currentColor;
  border-radius: 2px;
}
.rule-connector-arrow {
  font-family: 'JetBrains Mono', monospace;
  font-size: 18px;
  font-weight: 600;
}
.rule-score {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  gap: 2px;
  padding: 0 6px;
}
.rule-score-eyebrow {
  font-family: 'JetBrains Mono', monospace;
  font-size: 10px;
  text-transform: uppercase;
  letter-spacing: 0.18em;
  color: var(--ink40);
}
.rule-score-value {
  background: var(--lime);
  border: 1px solid var(--ink12);
  color: var(--onLime);
  font-weight: 800;
  font-size: 13px;
  padding: 4px 10px;
  border-radius: 999px;
  font-family: 'JetBrains Mono', monospace;
  letter-spacing: -0.01em;
}
.rule-delete {
  width: 38px;
  height: 38px;
  border-radius: 50%;
  background: var(--accentSoft);
  color: var(--accentDk);
  border: 1px solid var(--accentBorder);
  cursor: pointer;
  display: grid;
  place-items: center;
  font-size: 14px;
  font-weight: 700;
  transition: background 0.18s ease, transform 0.15s ease;
  font-family: inherit;
}
.rule-delete:hover {
  background: var(--accent);
  color: var(--onAccent);
  border-color: var(--accent);
}
.rule-delete:active {
  transform: scale(0.92);
}
.rule-reason {
  margin-top: 14px;
  padding-top: 14px;
  border-top: 1px solid var(--ink06);
  display: flex;
  align-items: flex-start;
  gap: 10px;
}
.rule-ai-tag {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  padding: 4px 10px;
  border-radius: 999px;
  background: var(--ink);
  color: var(--lime);
  border: 1px solid var(--ink);
  font-family: 'JetBrains Mono', monospace;
  font-size: 11px;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.12em;
  flex-shrink: 0;
}
.rule-reason-text {
  font-size: 14px;
  color: var(--ink70);
  font-style: italic;
  line-height: 1.5;
  flex: 1;
}
</style>
