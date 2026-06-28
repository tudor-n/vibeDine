<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { apiClient } from '@/services/api-client'
import { useToast } from '@/composables/useToast'
import { formatDate, statusBadgeClass, statusLabel } from '@/utils/format'
import type { LocationDetail } from '@/types/platform'

const route = useRoute()
const router = useRouter()
const toast = useToast()

const id = route.params.id as string
const loc = ref<LocationDetail | null>(null)
const loading = ref(true)

const showEdit = ref(false)
const edit = ref({ name: '', address: '', timezone: '', currency: '', locale: '' })
const showAssign = ref(false)
const assignEmail = ref('')
const saving = ref(false)

async function load() {
  loading.value = true
  try {
    loc.value = await apiClient.getLocation(id)
  } catch (e) {
    toast.error(e instanceof Error ? e.message : 'Failed to load location.')
  } finally {
    loading.value = false
  }
}

async function toggleActive() {
  if (!loc.value) return
  try {
    await apiClient.setLocationStatus(id, !loc.value.isActive)
    toast.success(loc.value.isActive ? 'Location deactivated.' : 'Location activated.')
    await load()
  } catch (e) {
    toast.error(e instanceof Error ? e.message : 'Failed to update status.')
  }
}

function openEdit() {
  if (!loc.value) return
  edit.value = {
    name: loc.value.name,
    address: loc.value.address ?? '',
    timezone: loc.value.timezone,
    currency: loc.value.currency,
    locale: loc.value.locale,
  }
  showEdit.value = true
}

async function saveEdit() {
  saving.value = true
  try {
    await apiClient.updateLocation(id, {
      name: edit.value.name.trim(),
      address: edit.value.address.trim(),
      timezone: edit.value.timezone.trim(),
      currency: edit.value.currency.trim(),
      locale: edit.value.locale.trim(),
    })
    toast.success('Location updated.')
    showEdit.value = false
    await load()
  } catch (e) {
    toast.error(e instanceof Error ? e.message : 'Failed to update location.')
  } finally {
    saving.value = false
  }
}

async function assignAdmin() {
  if (!assignEmail.value.trim()) return
  saving.value = true
  try {
    await apiClient.assignAdmin(id, assignEmail.value.trim())
    toast.success('Admin assigned.')
    showAssign.value = false
    assignEmail.value = ''
    await load()
  } catch (e) {
    toast.error(e instanceof Error ? e.message : 'Failed to assign admin.')
  } finally {
    saving.value = false
  }
}

async function setStatus(membershipId: string, status: string) {
  try {
    await apiClient.setMemberStatus(id, membershipId, status)
    await load()
  } catch (e) {
    toast.error(e instanceof Error ? e.message : 'Failed to update member.')
  }
}

async function removeMember(membershipId: string, name: string) {
  if (!confirm(`Remove ${name} from this location?`)) return
  try {
    await apiClient.removeMember(id, membershipId)
    toast.success('Member removed.')
    await load()
  } catch (e) {
    toast.error(e instanceof Error ? e.message : 'Failed to remove member.')
  }
}

async function deleteLocation() {
  if (!loc.value) return
  if (!confirm(`Permanently delete "${loc.value.name}"? This also removes its menu, tables and order history.`)) return
  try {
    await apiClient.deleteLocation(id, true)
    toast.success('Location deleted.')
    router.push('/locations')
  } catch (e) {
    toast.error(e instanceof Error ? e.message : 'Failed to delete location.')
  }
}
</script>

<template>
  <div>
    <button class="text-faint text-sm mb-4 hover:underline" @click="router.push('/locations')">← Locations</button>

    <div v-if="loading" class="text-muted">Loading…</div>

    <div v-else-if="loc">
      <div class="flex items-start justify-between gap-3 mb-6 flex-wrap">
        <div>
          <h1 class="font-display text-3xl font-extrabold tracking-tight">{{ loc.name }}</h1>
          <div class="text-muted text-sm mt-1">
            <RouterLink :to="`/organizations/${loc.organizationId}`" class="hover:underline">{{ loc.organizationName }}</RouterLink>
            · {{ loc.address || 'No address' }}
          </div>
          <div class="mt-2">
            <span :class="loc.isActive ? 'badge badge-green' : 'badge'">{{ loc.isActive ? 'Active' : 'Inactive' }}</span>
            <span class="badge ml-2">{{ loc.currency }}</span>
            <span class="badge ml-2">{{ loc.locale }}</span>
            <span class="badge ml-2">{{ loc.timezone }}</span>
          </div>
        </div>
        <div class="flex gap-2 flex-wrap">
          <button class="btn-ghost" @click="openEdit">Edit</button>
          <button class="btn-ghost" @click="toggleActive">{{ loc.isActive ? 'Deactivate' : 'Activate' }}</button>
          <button class="btn-ghost btn-danger" @click="deleteLocation">Delete</button>
        </div>
      </div>

      <!-- Admins -->
      <div class="card overflow-hidden mb-5">
        <div class="flex items-center justify-between px-5 pt-4 pb-2">
          <div class="eyebrow">Location admins ({{ loc.admins.length }})</div>
          <button class="btn-ghost !px-3 !py-1.5 !min-h-0 text-xs" @click="showAssign = true">+ Assign admin</button>
        </div>
        <div v-if="loc.admins.length === 0" class="px-5 pb-5 text-muted text-sm">No admin assigned.</div>
        <table v-else class="data-table">
          <thead><tr><th>Name</th><th>Email</th><th>Status</th><th>Since</th><th></th></tr></thead>
          <tbody>
            <tr v-for="m in loc.admins" :key="m.membershipId">
              <td class="font-semibold">{{ m.fullName || '—' }}</td>
              <td class="text-muted">{{ m.email }}</td>
              <td><span :class="statusBadgeClass(m.status)">{{ statusLabel(m.status) }}</span></td>
              <td class="text-muted">{{ formatDate(m.createdAt) }}</td>
              <td>
                <button class="btn-ghost btn-danger !px-3 !py-1.5 !min-h-0 text-xs" @click="removeMember(m.membershipId, m.fullName || m.email || 'this admin')">Remove</button>
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <!-- Hires -->
      <div class="card overflow-hidden">
        <div class="px-5 pt-4 pb-2 eyebrow">Hires ({{ loc.hires.length }})</div>
        <div v-if="loc.hires.length === 0" class="px-5 pb-5 text-muted text-sm">No hires yet.</div>
        <table v-else class="data-table">
          <thead><tr><th>Name</th><th>Email</th><th>Status</th><th>Since</th><th></th></tr></thead>
          <tbody>
            <tr v-for="m in loc.hires" :key="m.membershipId">
              <td class="font-semibold">{{ m.fullName || '—' }}</td>
              <td class="text-muted">{{ m.email }}</td>
              <td><span :class="statusBadgeClass(m.status)">{{ statusLabel(m.status) }}</span></td>
              <td class="text-muted">{{ formatDate(m.createdAt) }}</td>
              <td class="flex gap-2">
                <button
                  v-if="m.status !== 'active'"
                  class="btn-ghost !px-3 !py-1.5 !min-h-0 text-xs"
                  @click="setStatus(m.membershipId, 'active')"
                >Approve</button>
                <button
                  v-if="m.status !== 'rejected'"
                  class="btn-ghost !px-3 !py-1.5 !min-h-0 text-xs"
                  @click="setStatus(m.membershipId, 'rejected')"
                >Reject</button>
                <button class="btn-ghost btn-danger !px-3 !py-1.5 !min-h-0 text-xs" @click="removeMember(m.membershipId, m.fullName || m.email || 'this hire')">Remove</button>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>

    <!-- Edit modal -->
    <div v-if="showEdit" class="modal-backdrop" @click.self="showEdit = false">
      <div class="modal">
        <h2 class="font-display text-xl font-bold mb-4">Edit location</h2>
        <form @submit.prevent="saveEdit" class="space-y-3">
          <div><label class="field-label">Name</label><input v-model="edit.name" class="field-input" required /></div>
          <div><label class="field-label">Address</label><input v-model="edit.address" class="field-input" /></div>
          <div class="grid grid-cols-2 gap-3">
            <div><label class="field-label">Currency</label><input v-model="edit.currency" class="field-input" /></div>
            <div><label class="field-label">Locale</label><input v-model="edit.locale" class="field-input" /></div>
          </div>
          <div><label class="field-label">Timezone</label><input v-model="edit.timezone" class="field-input" /></div>
          <div class="flex justify-end gap-2 pt-2">
            <button type="button" class="btn-ghost" @click="showEdit = false">Cancel</button>
            <button type="submit" class="pill-btn" :disabled="saving">{{ saving ? 'Saving…' : 'Save' }}</button>
          </div>
        </form>
      </div>
    </div>

    <!-- Assign admin modal -->
    <div v-if="showAssign" class="modal-backdrop" @click.self="showAssign = false">
      <div class="modal">
        <h2 class="font-display text-xl font-bold mb-2">Assign admin</h2>
        <p class="text-muted text-sm mb-4">The person must already have a vibeDine account. Otherwise, send them a registration link first.</p>
        <form @submit.prevent="assignAdmin" class="space-y-4">
          <div><label class="field-label">Account email</label><input v-model="assignEmail" type="email" class="field-input" required /></div>
          <div class="flex justify-end gap-2 pt-2">
            <button type="button" class="btn-ghost" @click="showAssign = false">Cancel</button>
            <button type="submit" class="pill-btn" :disabled="saving">{{ saving ? 'Assigning…' : 'Assign' }}</button>
          </div>
        </form>
      </div>
    </div>
  </div>
</template>
