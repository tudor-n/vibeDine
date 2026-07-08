<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { apiClient } from '@/services/api-client'
import { useToast } from '@/composables/useToast'
import { formatDate } from '@/utils/format'
import type { OrganizationListItem } from '@/types/platform'

const router = useRouter()
const toast = useToast()

const orgs = ref<OrganizationListItem[]>([])
const loading = ref(true)
const search = ref('')

const showCreate = ref(false)
const createName = ref('')
const saving = ref(false)

async function load() {
  loading.value = true
  try {
    orgs.value = await apiClient.listOrganizations(search.value.trim() || undefined)
  } catch (e) {
    toast.error(e instanceof Error ? e.message : 'Failed to load organizations.')
  } finally {
    loading.value = false
  }
}

async function createOrg() {
  if (!createName.value.trim()) return
  saving.value = true
  try {
    await apiClient.createOrganization({ name: createName.value.trim() })
    toast.success('Organization created.')
    showCreate.value = false
    createName.value = ''
    await load()
  } catch (e) {
    toast.error(e instanceof Error ? e.message : 'Failed to create organization.')
  } finally {
    saving.value = false
  }
}

async function remove(org: OrganizationListItem) {
  if (!confirm(`Delete organization "${org.name}"? This cannot be undone.`)) return
  try {
    await apiClient.deleteOrganization(org.id)
    toast.success('Organization deleted.')
    await load()
  } catch (e) {
    toast.error(e instanceof Error ? e.message : 'Failed to delete organization.')
  }
}

onMounted(load)
</script>

<template>
  <div>
    <div class="flex items-center justify-between gap-3 mb-6 flex-wrap">
      <div>
        <h1 class="font-display text-3xl font-extrabold tracking-tight">Organizations</h1>
        <p class="text-muted text-sm mt-1">Every customer account on the platform.</p>
      </div>
      <button class="pill-btn" @click="showCreate = true">+ New organization</button>
    </div>

    <div class="mb-4 flex gap-2 max-w-md">
      <input
        v-model="search"
        class="field-input"
        placeholder="Search by name or slug…"
        @keyup.enter="load"
      />
      <button class="btn-ghost" @click="load">Search</button>
    </div>

    <div class="card overflow-hidden">
      <div v-if="loading" class="p-6 text-muted">Loading…</div>
      <div v-else-if="orgs.length === 0" class="p-6 text-muted">No organizations found.</div>
      <table v-else class="data-table">
        <thead>
          <tr>
            <th>Name</th>
            <th>Owner</th>
            <th>Locations</th>
            <th>Admins</th>
            <th>Hires</th>
            <th>Created</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="org in orgs"
            :key="org.id"
            class="cursor-pointer"
            @click="router.push(`/organizations/${org.id}`)"
          >
            <td>
              <div class="font-semibold">{{ org.name }}</div>
              <div class="text-faint text-xs font-mono">{{ org.slug }}</div>
            </td>
            <td class="text-muted">{{ org.owner?.email ?? '—' }}</td>
            <td>{{ org.locationCount }}</td>
            <td>{{ org.adminCount }}</td>
            <td>{{ org.hireCount }}</td>
            <td class="text-muted">{{ formatDate(org.createdAt) }}</td>
            <td @click.stop>
              <button class="btn-ghost btn-danger !px-3 !py-1.5 !min-h-0 text-xs" @click="remove(org)">Delete</button>
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <!-- Create modal -->
    <div v-if="showCreate" class="modal-backdrop" @click.self="showCreate = false">
      <div class="modal">
        <h2 class="font-display text-xl font-bold mb-4">New organization</h2>
        <form @submit.prevent="createOrg" class="space-y-4">
          <div>
            <label class="field-label">Name</label>
            <input v-model="createName" class="field-input" placeholder="Acme Restaurants" required />
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
