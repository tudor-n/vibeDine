<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { apiClient } from '@/services/api-client'
import { useToast } from '@/composables/useToast'
import { formatDate } from '@/utils/format'
import type { LocationListItem, OrganizationListItem } from '@/types/platform'

const router = useRouter()
const toast = useToast()

const locations = ref<LocationListItem[]>([])
const orgs = ref<OrganizationListItem[]>([])
const loading = ref(true)
const search = ref('')
const statusFilter = ref('')

const showCreate = ref(false)
const form = ref({ organizationId: '', name: '', address: '' })
const saving = ref(false)

async function load() {
  loading.value = true
  try {
    locations.value = await apiClient.listLocations({
      search: search.value.trim() || undefined,
      status: statusFilter.value || undefined,
    })
  } catch (e) {
    toast.error(e instanceof Error ? e.message : 'Failed to load locations.')
  } finally {
    loading.value = false
  }
}

async function openCreate() {
  showCreate.value = true
  if (orgs.value.length === 0) {
    try {
      orgs.value = await apiClient.listOrganizations()
    } catch (e) {
      toast.error(e instanceof Error ? e.message : 'Failed to load organizations.')
    }
  }
}

async function createLocation() {
  if (!form.value.organizationId || !form.value.name.trim()) return
  saving.value = true
  try {
    await apiClient.createLocation({
      organizationId: form.value.organizationId,
      name: form.value.name.trim(),
      address: form.value.address.trim() || undefined,
    })
    toast.success('Location created.')
    showCreate.value = false
    form.value = { organizationId: '', name: '', address: '' }
    await load()
  } catch (e) {
    toast.error(e instanceof Error ? e.message : 'Failed to create location.')
  } finally {
    saving.value = false
  }
}

onMounted(load)
</script>

<template>
  <div>
    <div class="flex items-center justify-between gap-3 mb-6 flex-wrap">
      <div>
        <h1 class="font-display text-3xl font-extrabold tracking-tight">Locations</h1>
        <p class="text-muted text-sm mt-1">Every restaurant location across all organizations.</p>
      </div>
      <button class="pill-btn" @click="openCreate">+ New location</button>
    </div>

    <div class="mb-4 flex gap-2 flex-wrap">
      <input v-model="search" class="field-input max-w-xs" placeholder="Search name / address / org…" @keyup.enter="load" />
      <select v-model="statusFilter" class="field-input max-w-[180px]" @change="load">
        <option value="">All statuses</option>
        <option value="active">Active</option>
        <option value="inactive">Inactive</option>
      </select>
      <button class="btn-ghost" @click="load">Search</button>
    </div>

    <div class="card overflow-hidden">
      <div v-if="loading" class="p-6 text-muted">Loading…</div>
      <div v-else-if="locations.length === 0" class="p-6 text-muted">No locations found.</div>
      <table v-else class="data-table">
        <thead>
          <tr>
            <th>Location</th><th>Organization</th><th>Status</th><th>Admins</th><th>Hires</th><th>Created</th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="loc in locations"
            :key="loc.id"
            class="cursor-pointer"
            @click="router.push(`/locations/${loc.id}`)"
          >
            <td>
              <div class="font-semibold">{{ loc.name }}</div>
              <div class="text-faint text-xs">{{ loc.address || '—' }}</div>
            </td>
            <td class="text-muted">{{ loc.organizationName }}</td>
            <td>
              <span :class="loc.isActive ? 'badge badge-green' : 'badge'">
                {{ loc.isActive ? 'Active' : 'Inactive' }}
              </span>
            </td>
            <td>{{ loc.adminCount }}</td>
            <td>{{ loc.hireCount }}</td>
            <td class="text-muted">{{ formatDate(loc.createdAt) }}</td>
          </tr>
        </tbody>
      </table>
    </div>

    <!-- Create modal -->
    <div v-if="showCreate" class="modal-backdrop" @click.self="showCreate = false">
      <div class="modal">
        <h2 class="font-display text-xl font-bold mb-4">New location</h2>
        <form @submit.prevent="createLocation" class="space-y-4">
          <div>
            <label class="field-label">Organization</label>
            <select v-model="form.organizationId" class="field-input" required>
              <option value="" disabled>Select an organization…</option>
              <option v-for="o in orgs" :key="o.id" :value="o.id">{{ o.name }}</option>
            </select>
          </div>
          <div>
            <label class="field-label">Name</label>
            <input v-model="form.name" class="field-input" placeholder="Downtown branch" required />
          </div>
          <div>
            <label class="field-label">Address (optional)</label>
            <input v-model="form.address" class="field-input" />
          </div>
          <div class="flex justify-end gap-2 pt-2">
            <button type="button" class="btn-ghost" @click="showCreate = false">Cancel</button>
            <button type="submit" class="pill-btn" :disabled="saving">{{ saving ? 'Creating…' : 'Create' }}</button>
          </div>
        </form>
      </div>
    </div>
  </div>
</template>
