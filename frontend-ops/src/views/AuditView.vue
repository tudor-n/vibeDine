<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { opsApi } from '@/services/api-client'
import { useToast } from '@/composables/useToast'
import { formatDate } from '@/utils/format'
import type { AuditEntry } from '@/types/ops'

const toast = useToast()
const entries = ref<AuditEntry[]>([])
const loading = ref(true)

async function load() {
  loading.value = true
  try {
    entries.value = await opsApi.getAudit({ limit: 200 })
  } catch (e) {
    toast.error(e instanceof Error ? e.message : 'Failed to load audit log.')
  } finally {
    loading.value = false
  }
}

onMounted(load)
</script>

<template>
  <div class="max-w-6xl mx-auto">
    <div class="flex items-center justify-between mb-6">
      <div>
        <h1 class="font-display text-2xl font-bold">Audit log</h1>
        <p class="text-muted text-sm mt-1">Every action taken through this console — who, what, when.</p>
      </div>
      <button class="btn-ghost" @click="load">Refresh</button>
    </div>

    <div v-if="loading" class="text-muted">Loading…</div>

    <div v-else-if="entries.length === 0" class="card p-10 text-center text-muted">No activity yet.</div>

    <div v-else class="card overflow-x-auto">
      <table class="data-table">
        <thead>
          <tr>
            <th>Time</th>
            <th>Actor</th>
            <th>Action</th>
            <th>Provider</th>
            <th>Target</th>
            <th>Result</th>
            <th>Summary</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="a in entries" :key="a.id">
            <td class="text-faint text-xs whitespace-nowrap">{{ formatDate(a.createdAt) }}</td>
            <td class="text-sm">{{ a.actorEmail || '—' }}</td>
            <td><span class="badge">{{ a.action }}</span></td>
            <td class="text-sm">{{ a.provider || '—' }}</td>
            <td class="text-faint text-xs">{{ a.targetType ? `${a.targetType}:${a.targetId}` : '—' }}</td>
            <td>
              <span :class="a.result === 'success' ? 'badge badge-green' : 'badge badge-red'">{{ a.result }}</span>
            </td>
            <td class="text-sm text-muted">{{ a.summary || '—' }}</td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>
