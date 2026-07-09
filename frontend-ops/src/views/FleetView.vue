<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { opsApi } from '@/services/api-client'
import { useToast } from '@/composables/useToast'
import { connectionDotColor, roleLabel, timeAgo } from '@/utils/format'
import type { FleetOverview } from '@/types/ops'

const router = useRouter()
const toast = useToast()

const fleet = ref<FleetOverview | null>(null)
const loading = ref(true)
const testingId = ref<string | null>(null)

async function load() {
  loading.value = true
  try {
    fleet.value = await opsApi.getFleet()
  } catch (e) {
    toast.error(e instanceof Error ? e.message : 'Failed to load fleet.')
  } finally {
    loading.value = false
  }
}

async function test(id: string) {
  testingId.value = id
  try {
    const res = await opsApi.testConnection(id)
    toast[res.success ? 'success' : 'error'](res.detail || (res.success ? 'Healthy' : 'Unhealthy'))
    await load()
  } catch (e) {
    toast.error(e instanceof Error ? e.message : 'Test failed.')
  } finally {
    testingId.value = null
  }
}

onMounted(load)
</script>

<template>
  <div class="max-w-6xl mx-auto">
    <div class="flex items-center justify-between mb-6">
      <div>
        <h1 class="font-display text-2xl font-bold">Fleet</h1>
        <p class="text-muted text-sm mt-1">Every server &amp; connection at a glance.</p>
      </div>
      <RouterLink to="/connections" class="pill-btn">+ Connect a server</RouterLink>
    </div>

    <div v-if="fleet" class="grid grid-cols-3 gap-3 mb-6">
      <div class="card p-4">
        <div class="eyebrow">Total</div>
        <div class="text-3xl font-bold mt-1">{{ fleet.total }}</div>
      </div>
      <div class="card p-4">
        <div class="eyebrow">Active</div>
        <div class="text-3xl font-bold mt-1" style="color: var(--green)">{{ fleet.active }}</div>
      </div>
      <div class="card p-4">
        <div class="eyebrow">Unhealthy</div>
        <div class="text-3xl font-bold mt-1" style="color: var(--red)">{{ fleet.unhealthy }}</div>
      </div>
    </div>

    <div v-if="loading" class="text-muted">Loading…</div>

    <div v-else-if="fleet && fleet.servers.length === 0" class="card p-10 text-center">
      <div class="text-lg font-semibold mb-1">No servers connected yet</div>
      <p class="text-muted text-sm mb-4">
        Connect Railway or a VPS to start managing deployments, logs and variables.
      </p>
      <RouterLink to="/connections" class="pill-btn">Connect your first server</RouterLink>
    </div>

    <div v-else class="grid sm:grid-cols-2 lg:grid-cols-3 gap-4">
      <div v-for="s in fleet?.servers" :key="s.connectionId" class="card p-5 flex flex-col gap-3">
        <div class="flex items-start justify-between">
          <div>
            <div class="font-semibold text-lg">{{ s.name }}</div>
            <div class="text-faint text-xs mt-0.5">{{ s.accountLabel || '—' }}</div>
          </div>
          <span class="status-dot mt-2" :style="{ background: connectionDotColor(s.status) }" />
        </div>
        <div class="flex flex-wrap gap-1.5">
          <span class="badge badge-blue">{{ s.provider }}</span>
          <span class="badge">{{ roleLabel(s.role) }}</span>
          <span class="badge">{{ s.status }}</span>
        </div>
        <div class="text-faint text-xs">Checked {{ timeAgo(s.lastCheckedAt) }}</div>
        <div class="flex gap-2 mt-auto pt-2">
          <button class="pill-btn flex-1 !py-1.5" @click="router.push(`/connections/${s.connectionId}`)">
            Open
          </button>
          <button class="btn-ghost" :disabled="testingId === s.connectionId" @click="test(s.connectionId)">
            {{ testingId === s.connectionId ? '…' : 'Test' }}
          </button>
        </div>
      </div>
    </div>
  </div>
</template>
