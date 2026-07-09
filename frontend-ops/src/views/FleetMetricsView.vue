<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { opsApi } from '@/services/api-client'
import { useToast } from '@/composables/useToast'
import { groupMetrics, metricColor, metricLabel, roleLabel } from '@/utils/format'
import Sparkline from '@/components/Sparkline.vue'
import type { FleetMetrics } from '@/types/ops'

const toast = useToast()
const data = ref<FleetMetrics | null>(null)
const windowMinutes = ref(60)
const loading = ref(false)

async function load() {
  loading.value = true
  try {
    data.value = await opsApi.getFleetMetrics({ windowMinutes: windowMinutes.value })
  } catch (e) {
    toast.error(e instanceof Error ? e.message : 'Failed to load fleet metrics.')
  } finally {
    loading.value = false
  }
}

onMounted(load)
</script>

<template>
  <div class="max-w-6xl mx-auto">
    <div class="flex items-center justify-between mb-6 gap-3 flex-wrap">
      <div>
        <h1 class="font-display text-2xl font-bold">Server metrics</h1>
        <p class="text-muted text-sm mt-1">Aggregated CPU / RAM / network per server across the whole fleet.</p>
      </div>
      <div class="flex items-end gap-2">
        <div>
          <label class="field-label">Window</label>
          <select v-model.number="windowMinutes" class="field-input" @change="load">
            <option :value="15">Last 15m</option>
            <option :value="60">Last 1h</option>
            <option :value="360">Last 6h</option>
            <option :value="1440">Last 24h</option>
          </select>
        </div>
        <button class="btn-ghost" :disabled="loading" @click="load">{{ loading ? '…' : 'Refresh' }}</button>
      </div>
    </div>

    <div v-if="loading && !data" class="text-muted">Loading…</div>

    <div v-else-if="data && data.servers.length === 0" class="card p-10 text-center">
      <div class="text-lg font-semibold mb-1">No servers connected</div>
      <RouterLink to="/connections" class="pill-btn mt-2 inline-flex">Connect a server</RouterLink>
    </div>

    <div v-else class="space-y-8">
      <section v-for="s in data?.servers" :key="s.connectionId">
        <!-- Server header -->
        <div class="flex items-center justify-between mb-3 gap-2 flex-wrap">
          <div class="flex items-center gap-2">
            <RouterLink :to="`/connections/${s.connectionId}`" class="font-semibold text-lg hover:underline">
              {{ s.name }}
            </RouterLink>
            <span class="badge badge-blue">{{ s.provider }}</span>
            <span class="badge">{{ roleLabel(s.role) }}</span>
          </div>
          <span v-if="s.error" class="badge badge-amber">{{ s.error }}</span>
          <span v-else-if="!s.supported" class="badge">metrics n/a</span>
        </div>

        <!-- One card per metric -->
        <div v-if="s.supported && s.points.length" class="grid sm:grid-cols-2 lg:grid-cols-4 gap-4">
          <div
            v-for="m in groupMetrics(s.points)"
            :key="m.metric"
            class="card p-4 flex flex-col gap-2"
            :style="{ borderTop: `3px solid ${metricColor(m.metric)}` }"
          >
            <div class="eyebrow">{{ metricLabel(m.metric) }}</div>
            <div class="flex items-end justify-between gap-2">
              <span class="font-display text-2xl font-bold leading-none">{{ m.last.toFixed(2) }}</span>
              <span class="text-faint text-xs">peak {{ m.max.toFixed(2) }}</span>
            </div>
            <Sparkline :values="m.values" :color="metricColor(m.metric)" :height="44" />
            <div class="text-faint text-xs">{{ m.values.length }} samples</div>
          </div>
        </div>
        <div v-else-if="s.supported && !s.error" class="card p-6 text-faint text-sm">
          No metric data in this window.
        </div>
      </section>
    </div>

    <p class="text-faint text-xs mt-6">
      Server totals sum CPU / memory / network across every service in the account (service count capped per server
      to stay under Railway's rate limits).
    </p>
  </div>
</template>
