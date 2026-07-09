<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { apiClient } from '@/services/api-client'
import { useToast } from '@/composables/useToast'
import { formatDate, linkTypeLabel, statusBadgeClass, statusLabel } from '@/utils/format'
import type { OrganizationListItem, RegistrationLink } from '@/types/platform'

const toast = useToast()

const links = ref<RegistrationLink[]>([])
const orgs = ref<OrganizationListItem[]>([])
const loading = ref(true)

const showCreate = ref(false)
const form = ref({ type: 'new_organization', organizationId: '', label: '', intendedEmail: '', expiresInDays: 7 })
const saving = ref(false)

async function load() {
  loading.value = true
  try {
    links.value = await apiClient.listRegistrationLinks()
  } catch (e) {
    toast.error(e instanceof Error ? e.message : 'Failed to load links.')
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

async function create() {
  saving.value = true
  try {
    await apiClient.createRegistrationLink({
      type: form.value.type,
      organizationId: form.value.type === 'new_location' ? form.value.organizationId : undefined,
      label: form.value.label.trim() || undefined,
      intendedEmail: form.value.intendedEmail.trim() || undefined,
      expiresInDays: form.value.expiresInDays || undefined,
    })
    toast.success('Registration link created.')
    showCreate.value = false
    form.value = { type: 'new_organization', organizationId: '', label: '', intendedEmail: '', expiresInDays: 7 }
    await load()
  } catch (e) {
    toast.error(e instanceof Error ? e.message : 'Failed to create link.')
  } finally {
    saving.value = false
  }
}

async function revoke(link: RegistrationLink) {
  if (!confirm('Revoke this registration link?')) return
  try {
    await apiClient.revokeRegistrationLink(link.id)
    toast.success('Link revoked.')
    await load()
  } catch (e) {
    toast.error(e instanceof Error ? e.message : 'Failed to revoke link.')
  }
}

async function copy(url: string) {
  try {
    await navigator.clipboard.writeText(url)
    toast.success('Link copied to clipboard.')
  } catch {
    toast.error('Could not copy. Select and copy manually.')
  }
}

onMounted(load)
</script>

<template>
  <div>
    <div class="flex items-center justify-between gap-3 mb-6 flex-wrap">
      <div>
        <h1 class="font-display text-3xl font-extrabold tracking-tight">Registration links</h1>
        <p class="text-muted text-sm mt-1">One-time links to onboard a new organization or add a location.</p>
      </div>
      <button class="pill-btn" @click="openCreate">+ Generate link</button>
    </div>

    <div class="card overflow-hidden">
      <div v-if="loading" class="p-6 text-muted">Loading…</div>
      <div v-else-if="links.length === 0" class="p-6 text-muted">No links generated yet.</div>
      <table v-else class="data-table">
        <thead>
          <tr><th>Type</th><th>Target</th><th>Status</th><th>Expires</th><th>Link</th><th></th></tr>
        </thead>
        <tbody>
          <tr v-for="l in links" :key="l.id">
            <td class="font-semibold">{{ linkTypeLabel(l.type) }}</td>
            <td class="text-muted">
              <div>{{ l.organizationName || l.label || '—' }}</div>
              <div v-if="l.intendedEmail" class="text-faint text-xs">{{ l.intendedEmail }}</div>
            </td>
            <td><span :class="statusBadgeClass(l.status)">{{ statusLabel(l.status) }}</span></td>
            <td class="text-muted">{{ formatDate(l.expiresAt) }}</td>
            <td>
              <button class="text-link font-mono text-xs hover:underline" :style="{ color: 'var(--accent)' }" @click="copy(l.url)">
                Copy link
              </button>
            </td>
            <td>
              <button
                v-if="l.status === 'active'"
                class="btn-ghost btn-danger !px-3 !py-1.5 !min-h-0 text-xs"
                @click="revoke(l)"
              >Revoke</button>
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <!-- Create modal -->
    <div v-if="showCreate" class="modal-backdrop" @click.self="showCreate = false">
      <div class="modal">
        <h2 class="font-display text-xl font-bold mb-4">Generate registration link</h2>
        <form @submit.prevent="create" class="space-y-4">
          <div>
            <label class="field-label">Type</label>
            <select v-model="form.type" class="field-input">
              <option value="new_organization">New organization (new client)</option>
              <option value="new_location">New location (existing client)</option>
            </select>
          </div>
          <div v-if="form.type === 'new_location'">
            <label class="field-label">Organization</label>
            <select v-model="form.organizationId" class="field-input" required>
              <option value="" disabled>Select an organization…</option>
              <option v-for="o in orgs" :key="o.id" :value="o.id">{{ o.name }}</option>
            </select>
          </div>
          <div>
            <label class="field-label">Label / note (optional)</label>
            <input v-model="form.label" class="field-input" placeholder="Intended business name" />
          </div>
          <div>
            <label class="field-label">Client email (optional)</label>
            <input v-model="form.intendedEmail" type="email" class="field-input" />
          </div>
          <div>
            <label class="field-label">Expires in (days)</label>
            <input v-model.number="form.expiresInDays" type="number" min="1" class="field-input" />
          </div>
          <div class="flex justify-end gap-2 pt-2">
            <button type="button" class="btn-ghost" @click="showCreate = false">Cancel</button>
            <button type="submit" class="pill-btn" :disabled="saving">{{ saving ? 'Generating…' : 'Generate' }}</button>
          </div>
        </form>
      </div>
    </div>
  </div>
</template>
