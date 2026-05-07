<script setup lang="ts">

import { computed } from 'vue'

const props = defineProps<{
    metrics: any
    isLoading: boolean
}>()

const claimRate = computed(() => {
    if(!props.metrics || !props.metrics.flashDealsShown) return 0

    const rate = (props.metrics.flashDealsClaimed / props.metrics.flashDealsShown) * 100;
    return rate.toFixed(1);
})

</script>

<template>
  <div class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-3.5">

    <!-- Oferte afișate -->
    <div class="metric-card">
      <div class="metric-eyebrow">Oferte afișate · azi</div>
      <div v-if="isLoading && !metrics" class="metric-skeleton"></div>
      <div v-else class="metric-value">
        {{ metrics?.flashDealsShown || 0 }}
      </div>
      <div class="metric-foot">
        <span class="metric-dot metric-dot-ink"></span>
        <span>impressions · table-side</span>
      </div>
    </div>

    <!-- Oferte revendicate -->
    <div class="metric-card metric-card-accent">
      <div class="metric-eyebrow">Oferte revendicate</div>
      <div v-if="isLoading && !metrics" class="metric-skeleton metric-skeleton-on-dark"></div>
      <div v-else class="metric-value">
        {{ metrics?.flashDealsClaimed || 0 }}
      </div>
      <div class="metric-foot">
        <span class="metric-dot metric-dot-lime"></span>
        <span>conversii · cu confirmare</span>
      </div>
    </div>

    <!-- Conversie -->
    <div class="metric-card metric-card-lime">
      <div class="metric-eyebrow">Rata de conversie</div>
      <div v-if="isLoading && !metrics" class="metric-skeleton"></div>
      <div v-else class="metric-value">
        {{ claimRate }}<span class="metric-unit ml-1">%</span>
      </div>
      <div class="metric-foot">
        <span class="metric-dot metric-dot-ink"></span>
        <span>claim / shown</span>
      </div>
    </div>

    <!-- Venit upsell -->
    <div class="metric-card">
      <div class="metric-eyebrow">Venit extra · AI upsell</div>
      <div v-if="isLoading && !metrics" class="metric-skeleton"></div>
      <div v-else class="metric-value">
        {{ metrics?.upsellRevenue || 0 }}
        <span class="metric-unit ml-1">RON</span>
      </div>
      <div class="metric-foot">
        <span class="metric-dot metric-dot-accent"></span>
        <span>recomandări acceptate</span>
      </div>
    </div>

    <!-- Venit flash -->
    <div class="metric-card">
      <div class="metric-eyebrow">Venit oferte fulger</div>
      <div v-if="isLoading && !metrics" class="metric-skeleton"></div>
      <div v-else class="metric-value">
        {{ metrics?.flashDealRevenue || 0 }}
        <span class="metric-unit ml-1">RON</span>
      </div>
      <div class="metric-foot">
        <span class="metric-dot metric-dot-accent"></span>
        <span>preparate cu reducere</span>
      </div>
    </div>

  </div>
</template>

<style scoped>
.metric-card {
  background: var(--paperHi);
  border: 1px solid var(--ink12);
  border-radius: 24px;
  padding: 22px 24px 20px;
  display: flex;
  flex-direction: column;
  gap: 10px;
  position: relative;
  overflow: hidden;
  transition: transform 0.18s ease, box-shadow 0.18s ease;
}
.metric-card:hover {
  transform: translateY(-2px);
  box-shadow: var(--shadow-soft);
}
.metric-card-accent {
  background: var(--ink);
  border-color: var(--ink);
  color: var(--paperHi);
}
.metric-card-accent .metric-eyebrow {
  color: var(--paperHi);
  opacity: 0.7;
}
.metric-card-accent .metric-value { color: var(--paperHi); }
.metric-card-accent .metric-unit { color: var(--paperHi); opacity: 0.7; }
.metric-card-accent .metric-foot { color: var(--paperHi); opacity: 0.7; }

.metric-card-lime {
  background: var(--lime);
  border-color: var(--ink12);
}
.metric-card-lime .metric-eyebrow,
.metric-card-lime .metric-value,
.metric-card-lime .metric-foot {
  color: var(--onLime);
}

.metric-eyebrow {
  font-family: 'JetBrains Mono', ui-monospace, Menlo, monospace;
  font-size: 11px;
  font-weight: 500;
  letter-spacing: 0.18em;
  text-transform: uppercase;
  color: var(--ink70);
}
.metric-value {
  font-family: 'Inter Tight', sans-serif;
  font-size: 48px;
  line-height: 1;
  font-weight: 600;
  letter-spacing: -0.03em;
  color: var(--ink);
}
.metric-unit {
  font-size: 24px;
  font-weight: 600;
  color: var(--ink70);
}
.metric-foot {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 12.5px;
  color: var(--ink70);
  margin-top: 4px;
}
.metric-dot {
  width: 7px;
  height: 7px;
  border-radius: 50%;
  display: inline-block;
  flex-shrink: 0;
}
.metric-dot-ink { background: var(--ink); }
.metric-dot-accent { background: var(--accent); }
.metric-dot-lime {
  background: var(--lime);
  box-shadow: 0 0 10px var(--lime);
}

.metric-skeleton {
  width: 60%;
  height: 44px;
  border-radius: 10px;
  background: var(--ink12);
  animation: pulse 1.4s ease-in-out infinite;
}
.metric-skeleton-on-dark {
  background: rgba(245,240,232,0.12);
}
@keyframes pulse {
  0%, 100% { opacity: 0.55; }
  50% { opacity: 0.85; }
}
</style>
