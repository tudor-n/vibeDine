<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { apiClient } from '@/services/api-client'
import { useToast } from '@/composables/useToast'
import type { Metrics } from '@/types/platform'

const metrics = ref<Metrics | null>(null)
const loading = ref(true)
const toast = useToast()

const cards = () => [
  { label: 'Organizations', value: metrics.value?.organizations ?? 0, to: '/organizations' },
  { label: 'Locations', value: metrics.value?.locations ?? 0, to: '/locations' },
  { label: 'Active locations', value: metrics.value?.activeLocations ?? 0, to: '/locations' },
  { label: 'Location admins', value: metrics.value?.admins ?? 0, to: '/locations' },
  { label: 'Hires', value: metrics.value?.hires ?? 0, to: '/locations' },
  { label: 'Pending hires', value: metrics.value?.pendingHires ?? 0, to: '/locations', highlight: true },
]

onMounted(async () => {
  try {
    metrics.value = await apiClient.getMetrics()
  } catch (e) {
    toast.error(e instanceof Error ? e.message : 'Failed to load metrics.')
  } finally {
    loading.value = false
  }
})
</script>

<template>
  <div>
    <div class="mb-6">
      <h1 class="font-display text-3xl font-extrabold tracking-tight">Overview</h1>
      <p class="text-muted text-sm mt-1">Every organization, location, admin and hire across the platform.</p>
    </div>

    <div v-if="loading" class="text-muted">Loading…</div>

    <div v-else class="grid grid-cols-2 lg:grid-cols-3 gap-4">
      <RouterLink
        v-for="c in cards()"
        :key="c.label"
        :to="c.to"
        class="card p-5 block hover:-translate-y-0.5 transition-transform"
      >
        <div class="eyebrow">{{ c.label }}</div>
        <div
          class="font-display text-4xl font-extrabold mt-2"
          :style="c.highlight && c.value > 0 ? { color: 'var(--amber)' } : {}"
        >
          {{ c.value }}
        </div>
      </RouterLink>
    </div>
  </div>
</template>
