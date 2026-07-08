<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { apiClient } from '@/services/api-client'
import { useToast } from '@/composables/useToast'
import { formatDate } from '@/utils/format'
import type { OrganizationDetail } from '@/types/platform'

const route = useRoute()
const router = useRouter()
const toast = useToast()

const id = route.params.id as string
const org = ref<OrganizationDetail | null>(null)
const loading = ref(true)

const showEdit = ref(false)
const editName = ref('')
const showAddLocation = ref(false)
const newLocName = ref('')
const newLocAddress = ref('')
const saving = ref(false)

async function load() {
  loading.value = true
  try {
    org.value = await apiClient.getOrganization(id)
  } catch (e) {
    toast.error(e instanceof Error ? e.message : 'Failed to load organization.')
  } finally {
    loading.value = false
  }
}

function openEdit() {
  editName.value = org.value?.name ?? ''
  showEdit.value = true
}

async function saveEdit() {
  saving.value = true
  try {
    await apiClient.updateOrganization(id, { name: editName.value.trim() })
    toast.success('Organization updated.')
    showEdit.value = false
    await load()
  } catch (e) {
    toast.error(e instanceof Error ? e.message : 'Failed to update.')
  } finally {
    saving.value = false
  }
}

async function addLocation() {
  if (!newLocName.value.trim()) return
  saving.value = true
  try {
    await apiClient.createLocation({
      organizationId: id,
      name: newLocName.value.trim(),
      address: newLocAddress.value.trim() || undefined,
    })
    toast.success('Location created.')
    showAddLocation.value = false
    newLocName.value = ''
    newLocAddress.value = ''
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
    <button class="text-faint text-sm mb-4 hover:underline" @click="router.push('/organizations')">← Organizations</button>

    <div v-if="loading" class="text-muted">Loading…</div>

    <div v-else-if="org">
      <div class="flex items-start justify-between gap-3 mb-6 flex-wrap">
        <div>
          <h1 class="font-display text-3xl font-extrabold tracking-tight">{{ org.name }}</h1>
          <div class="text-faint text-sm font-mono mt-1">{{ org.slug }} · created {{ formatDate(org.createdAt) }}</div>
        </div>
        <div class="flex gap-2">
          <button class="btn-ghost" @click="openEdit">Edit</button>
          <button class="pill-btn" @click="showAddLocation = true">+ Add location</button>
        </div>
      </div>

      <!-- Owners -->
      <div class="card p-5 mb-5">
        <div class="eyebrow mb-3">Owners</div>
        <div v-if="org.owners.length === 0" class="text-muted text-sm">No owner account linked.</div>
        <ul v-else class="space-y-1">
          <li v-for="o in org.owners" :key="o.id" class="text-sm">
            <span class="font-semibold">{{ o.fullName || '—' }}</span>
            <span class="text-muted"> · {{ o.email }}</span>
          </li>
        </ul>
      </div>

      <!-- Locations -->
      <div class="card overflow-hidden">
        <div class="px-5 pt-4 pb-1 eyebrow">Locations ({{ org.locations.length }})</div>
        <div v-if="org.locations.length === 0" class="p-5 text-muted text-sm">No locations yet.</div>
        <table v-else class="data-table">
          <thead>
            <tr><th>Name</th><th>Address</th><th>Status</th><th>Admins</th><th>Hires</th></tr>
          </thead>
          <tbody>
            <tr
              v-for="loc in org.locations"
              :key="loc.id"
              class="cursor-pointer"
              @click="router.push(`/locations/${loc.id}`)"
            >
              <td class="font-semibold">{{ loc.name }}</td>
              <td class="text-muted">{{ loc.address || '—' }}</td>
              <td>
                <span :class="loc.isActive ? 'badge badge-green' : 'badge'">
                  {{ loc.isActive ? 'Active' : 'Inactive' }}
                </span>
              </td>
              <td>{{ loc.adminCount }}</td>
              <td>{{ loc.hireCount }}</td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>

    <!-- Edit org modal -->
    <div v-if="showEdit" class="modal-backdrop" @click.self="showEdit = false">
      <div class="modal">
        <h2 class="font-display text-xl font-bold mb-4">Edit organization</h2>
        <form @submit.prevent="saveEdit" class="space-y-4">
          <div>
            <label class="field-label">Name</label>
            <input v-model="editName" class="field-input" required />
          </div>
          <div class="flex justify-end gap-2 pt-2">
            <button type="button" class="btn-ghost" @click="showEdit = false">Cancel</button>
            <button type="submit" class="pill-btn" :disabled="saving">{{ saving ? 'Saving…' : 'Save' }}</button>
          </div>
        </form>
      </div>
    </div>

    <!-- Add location modal -->
    <div v-if="showAddLocation" class="modal-backdrop" @click.self="showAddLocation = false">
      <div class="modal">
        <h2 class="font-display text-xl font-bold mb-4">Add location</h2>
        <form @submit.prevent="addLocation" class="space-y-4">
          <div>
            <label class="field-label">Name</label>
            <input v-model="newLocName" class="field-input" placeholder="Downtown branch" required />
          </div>
          <div>
            <label class="field-label">Address (optional)</label>
            <input v-model="newLocAddress" class="field-input" placeholder="Str. Exemplu 1, București" />
          </div>
          <div class="flex justify-end gap-2 pt-2">
            <button type="button" class="btn-ghost" @click="showAddLocation = false">Cancel</button>
            <button type="submit" class="pill-btn" :disabled="saving">{{ saving ? 'Creating…' : 'Create' }}</button>
          </div>
        </form>
      </div>
    </div>
  </div>
</template>
