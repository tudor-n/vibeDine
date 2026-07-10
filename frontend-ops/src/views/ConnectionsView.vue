<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { useRouter } from 'vue-router'
import { opsApi } from '@/services/api-client'
import { useToast } from '@/composables/useToast'
import { connectionBadgeClass, roleLabel, timeAgo } from '@/utils/format'
import type { Connection, CreateConnectionPayload, ProviderInfo } from '@/types/ops'

const router = useRouter()
const toast = useToast()

const connections = ref<Connection[]>([])
const providers = ref<ProviderInfo[]>([])
const loading = ref(true)

const showWizard = ref(false)
const submitting = ref(false)

const ROLES = ['app', 'db', 'worker', 'other']

const form = reactive({
  provider: 'railway',
  name: '',
  role: 'app',
  tokenKind: 'account',
  token: '',
  // metadata fields
  workspaceId: '',
  host: '',
  port: '22',
  user: 'root',
})

const selectedProvider = computed(() => providers.value.find((p) => p.key === form.provider))
const tokenKinds = computed(() => selectedProvider.value?.capabilities.tokenKinds ?? [])

async function load() {
  loading.value = true
  try {
    ;[connections.value, providers.value] = await Promise.all([
      opsApi.listConnections(),
      opsApi.listProviders(),
    ])
  } catch (e) {
    toast.error(e instanceof Error ? e.message : 'Failed to load connections.')
  } finally {
    loading.value = false
  }
}

function openWizard() {
  Object.assign(form, {
    provider: 'railway',
    name: '',
    role: 'app',
    tokenKind: 'account',
    token: '',
    workspaceId: '',
    host: '',
    port: '22',
    user: 'root',
  })
  showWizard.value = true
}

function pickProvider(key: string) {
  form.provider = key
  const kinds = providers.value.find((p) => p.key === key)?.capabilities.tokenKinds ?? []
  form.tokenKind = kinds[0] ?? ''
}

async function submit() {
  if (!form.token.trim()) {
    toast.error('A token / credential is required.')
    return
  }
  submitting.value = true
  try {
    const metadata: Record<string, string | null> = {}
    if (form.provider === 'railway' && form.tokenKind === 'workspace') {
      metadata.workspaceId = form.workspaceId.trim()
    }
    if (form.provider === 'vps') {
      metadata.host = form.host.trim()
      metadata.port = form.port.trim() || '22'
      metadata.user = form.user.trim() || 'root'
    }

    const payload: CreateConnectionPayload = {
      provider: form.provider,
      name: form.name.trim(),
      role: form.role,
      token: form.token,
      tokenKind: form.tokenKind || null,
      metadata,
    }

    const created = await opsApi.createConnection(payload)
    toast.success(`Connected ${created.accountLabel || created.name}`)
    form.token = '' // don't retain the SSH key / password / API token in client memory
    showWizard.value = false
    await load()
    router.push(`/connections/${created.id}`)
  } catch (e) {
    toast.error(e instanceof Error ? e.message : 'Connection failed.')
  } finally {
    submitting.value = false
  }
}

async function test(c: Connection) {
  try {
    const res = await opsApi.testConnection(c.id)
    toast[res.success ? 'success' : 'error'](res.detail || (res.success ? 'Healthy' : 'Unhealthy'))
    await load()
  } catch (e) {
    toast.error(e instanceof Error ? e.message : 'Test failed.')
  }
}

async function remove(c: Connection) {
  if (!confirm(`Remove connection "${c.name}"? This cannot be undone.`)) return
  try {
    await opsApi.deleteConnection(c.id)
    toast.success('Connection removed.')
    await load()
  } catch (e) {
    toast.error(e instanceof Error ? e.message : 'Delete failed.')
  }
}

onMounted(load)
</script>

<template>
  <div class="max-w-5xl mx-auto">
    <div class="flex items-center justify-between mb-6">
      <div>
        <h1 class="font-display text-2xl font-bold">Connections</h1>
        <p class="text-muted text-sm mt-1">Railway accounts and VPS servers you manage.</p>
      </div>
      <button class="pill-btn" @click="openWizard">+ Connect</button>
    </div>

    <div v-if="loading" class="text-muted">Loading…</div>

    <div v-else-if="connections.length === 0" class="card p-10 text-center">
      <div class="text-lg font-semibold mb-1">Nothing connected yet</div>
      <p class="text-muted text-sm mb-4">Paste a Railway token or add a VPS to get started.</p>
      <button class="pill-btn" @click="openWizard">Connect a server</button>
    </div>

    <div v-else class="card overflow-hidden">
      <table class="data-table">
        <thead>
          <tr>
            <th>Name</th>
            <th>Provider</th>
            <th>Role</th>
            <th>Status</th>
            <th>Checked</th>
            <th class="text-right">Actions</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="c in connections" :key="c.id">
            <td>
              <RouterLink :to="`/connections/${c.id}`" class="font-medium hover:underline">
                {{ c.name }}
              </RouterLink>
              <div class="text-faint text-xs">{{ c.accountLabel || '—' }}</div>
            </td>
            <td><span class="badge badge-blue">{{ c.provider }}</span></td>
            <td>{{ roleLabel(c.role) }}</td>
            <td><span :class="connectionBadgeClass(c.status)">{{ c.status }}</span></td>
            <td class="text-faint text-xs">{{ timeAgo(c.lastCheckedAt) }}</td>
            <td>
              <div class="flex justify-end gap-2">
                <button class="btn-ghost !py-1.5" @click="test(c)">Test</button>
                <button class="btn-danger !py-1.5" @click="remove(c)">Delete</button>
              </div>
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <!-- Connect wizard -->
    <div v-if="showWizard" class="modal-backdrop" @click.self="showWizard = false">
      <div class="modal">
        <div class="flex items-center justify-between mb-4">
          <h2 class="font-display text-xl font-bold">Connect a server</h2>
          <button class="text-faint hover:opacity-70" @click="showWizard = false">✕</button>
        </div>

        <!-- Step 1: provider -->
        <div class="mb-4">
          <label class="field-label">Provider</label>
          <div class="flex gap-2">
            <button
              v-for="p in providers"
              :key="p.key"
              class="btn-ghost flex-1"
              :style="form.provider === p.key ? 'border-color: var(--accent); color: var(--accent)' : ''"
              @click="pickProvider(p.key)"
            >
              {{ p.key === 'railway' ? 'Railway' : p.key === 'vps' ? 'VPS (SSH)' : p.key }}
            </button>
          </div>
        </div>

        <form @submit.prevent="submit" class="space-y-4">
          <div class="grid grid-cols-2 gap-3">
            <div>
              <label class="field-label">Name</label>
              <input v-model="form.name" class="field-input" placeholder="e.g. Production app" />
            </div>
            <div>
              <label class="field-label">Role</label>
              <select v-model="form.role" class="field-input">
                <option v-for="r in ROLES" :key="r" :value="r">{{ roleLabel(r) }}</option>
              </select>
            </div>
          </div>

          <!-- Railway fields -->
          <template v-if="form.provider === 'railway'">
            <div>
              <label class="field-label">Token type</label>
              <select v-model="form.tokenKind" class="field-input">
                <option v-for="k in tokenKinds" :key="k" :value="k">{{ k }}</option>
              </select>
            </div>
            <div v-if="form.tokenKind === 'workspace'">
              <label class="field-label">Workspace ID</label>
              <input v-model="form.workspaceId" class="field-input" placeholder="workspace UUID" />
            </div>
            <div>
              <label class="field-label">API token</label>
              <input
                v-model="form.token"
                type="password"
                class="field-input"
                placeholder="Paste your Railway token"
                autocomplete="off"
              />
              <p class="text-faint text-xs mt-1">
                Create one at railway.com/account/tokens. Stored encrypted, never shown in the browser.
              </p>
            </div>
          </template>

          <!-- VPS fields -->
          <template v-else-if="form.provider === 'vps'">
            <div class="grid grid-cols-3 gap-3">
              <div class="col-span-2">
                <label class="field-label">Host</label>
                <input v-model="form.host" class="field-input" placeholder="1.2.3.4 or db.example.com" />
              </div>
              <div>
                <label class="field-label">Port</label>
                <input v-model="form.port" class="field-input" placeholder="22" />
              </div>
            </div>
            <div>
              <label class="field-label">User</label>
              <input v-model="form.user" class="field-input" placeholder="root" />
            </div>
            <div>
              <label class="field-label">Auth</label>
              <select v-model="form.tokenKind" class="field-input">
                <option v-for="k in tokenKinds" :key="k" :value="k">{{ k }}</option>
              </select>
            </div>
            <div>
              <label class="field-label">{{ form.tokenKind === 'password' ? 'Password' : 'SSH private key' }}</label>
              <input
                v-if="form.tokenKind === 'password'"
                v-model="form.token"
                type="password"
                class="field-input font-mono text-xs"
                placeholder="password"
                autocomplete="off"
              />
              <textarea
                v-else
                v-model="form.token"
                class="field-input font-mono text-xs"
                rows="4"
                placeholder="-----BEGIN OPENSSH PRIVATE KEY-----"
                autocomplete="off"
              />
              <p class="text-faint text-xs mt-1">
                We test reachability now; full SSH control ships next. Stored encrypted; cleared from the browser after connect.
              </p>
            </div>
          </template>

          <div class="flex justify-end gap-2 pt-2">
            <button type="button" class="btn-ghost" @click="showWizard = false">Cancel</button>
            <button type="submit" class="pill-btn" :disabled="submitting">
              {{ submitting ? 'Validating…' : 'Validate & connect' }}
            </button>
          </div>
        </form>
      </div>
    </div>
  </div>
</template>
