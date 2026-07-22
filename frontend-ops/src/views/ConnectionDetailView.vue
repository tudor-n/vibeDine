<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import { opsApi } from '@/services/api-client'
import { createLogStream } from '@/services/signalr'
import { useToast } from '@/composables/useToast'
import {
  deploymentBadgeClass,
  formatDate,
  groupMetrics,
  metricColor,
  metricLabel,
  roleLabel,
  severityClass,
} from '@/utils/format'
import Sparkline from '@/components/Sparkline.vue'
import type {
  Connection,
  Deployment,
  LogEntry,
  MetricPoint,
  Project,
  ProjectDetail,
  ServiceOverviewItem,
  Variable,
} from '@/types/ops'

const route = useRoute()
const toast = useToast()
const connId = route.params.id as string

const conn = ref<Connection | null>(null)
const loading = ref(true)
const caps = computed(() => conn.value?.capabilities)

const tab = ref<'overview' | 'deployments' | 'logs' | 'variables' | 'metrics'>('overview')

// ── Projects / services / environments ────────────────────────────────────────
const projects = ref<Project[]>([])
const projectId = ref('')
const projectDetail = ref<ProjectDetail | null>(null)
const serviceId = ref('')
const environmentId = ref('')

// ── Deployments ────────────────────────────────────────────────────────────────
const deployments = ref<Deployment[]>([])
const loadingDeps = ref(false)
const busyAction = ref<string | null>(null)

// ── Per-service drawer ───────────────────────────────────────────────────────────
const selectedService = ref<{ id: string; name: string } | null>(null)
const serviceDrawerOpen = ref(false)

// ── Logs ─────────────────────────────────────────────────────────────────────
const logType = ref('deploy')
const deploymentId = ref('')
const filter = ref('')
const logs = ref<LogEntry[]>([])
const loadingLogs = ref(false)
const live = ref(false)
const stream = createLogStream((batch) => {
  logs.value.push(...batch)
  if (logs.value.length > 2000) logs.value.splice(0, logs.value.length - 2000)
})

// ── Variables ──────────────────────────────────────────────────────────────────
// `reveal` is view-only state: values are secrets (DB strings, JWT_SECRET, API keys)
// and stay masked until the operator explicitly reveals a row.
interface VarRow extends Variable {
  reveal?: boolean
}
const varRows = ref<VarRow[]>([])
const replaceVars = ref(false)
const loadingVars = ref(false)

// ── Overview & metrics ─────────────────────────────────────────────────────────
const overview = ref<ServiceOverviewItem[]>([])
const loadingOverview = ref(false)
const metricsServiceId = ref('')
const metricsWindow = ref(60)
const metricsAggregate = ref(true) // default to the whole-server total, not a single process
const metricsPoints = ref<MetricPoint[]>([])
const loadingMetrics = ref(false)
const metricsError = ref<string | null>(null)

const metricsSeries = computed(() => groupMetrics(metricsPoints.value))

async function loadConnection() {
  loading.value = true
  try {
    conn.value = await opsApi.getConnection(connId)
    tab.value = conn.value.capabilities.overview
      ? 'overview'
      : conn.value.capabilities.deployments
        ? 'deployments'
        : 'logs'
    if (caps.value?.projects) await loadProjects()
  } catch (e) {
    toast.error(e instanceof Error ? e.message : 'Failed to load connection.')
  } finally {
    loading.value = false
  }
}

async function loadProjects() {
  try {
    projects.value = await opsApi.listProjects(connId)
    if (projects.value.length && !projectId.value) await selectProject(projects.value[0].id)
  } catch (e) {
    toast.error(e instanceof Error ? e.message : 'Failed to load projects.')
  }
}

async function selectProject(id: string) {
  projectId.value = id
  serviceId.value = ''
  environmentId.value = ''
  projectDetail.value = null
  try {
    projectDetail.value = await opsApi.getProject(connId, id)
    if (projectDetail.value.environments.length) environmentId.value = projectDetail.value.environments[0].id
    metricsServiceId.value = projectDetail.value.services[0]?.id ?? ''
    await loadDeployments()
    if (caps.value?.overview) await loadOverview()
  } catch (e) {
    toast.error(e instanceof Error ? e.message : 'Failed to load project.')
  }
}

async function loadDeployments() {
  if (!projectId.value) return
  loadingDeps.value = true
  try {
    deployments.value = await opsApi.listDeployments(connId, {
      projectId: projectId.value,
      serviceId: serviceId.value || undefined,
      environmentId: environmentId.value || undefined,
      first: 20,
    })
  } catch (e) {
    toast.error(e instanceof Error ? e.message : 'Failed to load deployments.')
  } finally {
    loadingDeps.value = false
  }
}

async function act(dep: Deployment, action: string) {
  if (['stop', 'remove', 'rollback'].includes(action) && !confirm(`${action} deployment ${dep.id.slice(0, 8)}?`)) return
  busyAction.value = `${dep.id}:${action}`
  try {
    await opsApi.deploymentAction(connId, dep.id, action)
    toast.success(`${action} sent.`)
    await loadDeployments()
  } catch (e) {
    toast.error(e instanceof Error ? e.message : `${action} failed.`)
  } finally {
    busyAction.value = null
  }
}

async function triggerDeploy() {
  if (!serviceId.value || !environmentId.value) {
    toast.error('Pick a service and environment first.')
    return
  }
  try {
    await opsApi.triggerDeploy(connId, { serviceId: serviceId.value, environmentId: environmentId.value })
    toast.success('Deploy triggered.')
    await loadDeployments()
  } catch (e) {
    toast.error(e instanceof Error ? e.message : 'Trigger failed.')
  }
}

function useForLogs(dep: Deployment) {
  deploymentId.value = dep.id
  tab.value = 'logs'
  fetchLogs()
}

// ── Service drawer: open one service and act on it ────────────────────────────────
async function openService(svc: { id: string; name: string }) {
  selectedService.value = svc
  serviceId.value = svc.id // scope deployments / logs / metrics / variables to this service
  serviceDrawerOpen.value = true
  await loadDeployments()
}

function closeService() {
  serviceDrawerOpen.value = false
}

function serviceLogs(dep?: Deployment) {
  const id = dep?.id || deployments.value[0]?.id
  if (!id) {
    toast.error('No deployment to show logs for.')
    return
  }
  deploymentId.value = id
  serviceDrawerOpen.value = false
  tab.value = 'logs'
  fetchLogs()
}

function serviceMetrics() {
  if (!selectedService.value) return
  serviceDrawerOpen.value = false
  openMetrics(selectedService.value.id)
}

function serviceVariables() {
  serviceDrawerOpen.value = false
  tab.value = 'variables'
  loadVariables()
}

async function fetchLogs() {
  if (!deploymentId.value) {
    toast.error('Enter a deployment id.')
    return
  }
  loadingLogs.value = true
  try {
    logs.value = await opsApi.getLogs(connId, {
      logType: logType.value,
      deploymentId: deploymentId.value,
      filter: filter.value || undefined,
      limit: 300,
      archive: true,
    })
  } catch (e) {
    toast.error(e instanceof Error ? e.message : 'Failed to fetch logs.')
  } finally {
    loadingLogs.value = false
  }
}

async function toggleLive() {
  if (!deploymentId.value) {
    toast.error('Enter a deployment id first.')
    return
  }
  live.value = !live.value
  if (live.value) {
    try {
      await stream.start(connId, deploymentId.value, logType.value)
      toast.info('Live tail on.')
    } catch (e) {
      live.value = false
      toast.error(e instanceof Error ? e.message : 'Could not start live tail.')
    }
  } else {
    await stream.stop()
  }
}

async function loadVariables() {
  if (!projectId.value || !environmentId.value) {
    toast.error('Pick a project and environment.')
    return
  }
  loadingVars.value = true
  try {
    varRows.value = await opsApi.getVariables(connId, {
      projectId: projectId.value,
      environmentId: environmentId.value,
      serviceId: serviceId.value || undefined,
    })
  } catch (e) {
    toast.error(e instanceof Error ? e.message : 'Failed to load variables.')
  } finally {
    loadingVars.value = false
  }
}

function addVar() {
  // A new row is operator-entered (not a fetched secret), so show it while typing.
  varRows.value.push({ key: '', value: '', reveal: true })
}
function removeVar(i: number) {
  varRows.value.splice(i, 1)
}

async function saveVariables() {
  const variables: Record<string, string> = {}
  for (const r of varRows.value) if (r.key.trim()) variables[r.key.trim()] = r.value
  if (replaceVars.value && !confirm('Replace ALL variables? Missing keys will be deleted.')) return
  try {
    await opsApi.upsertVariables(connId, {
      projectId: projectId.value,
      environmentId: environmentId.value,
      serviceId: serviceId.value || undefined,
      variables,
      replace: replaceVars.value,
      skipDeploys: false,
    })
    toast.success('Variables saved.')
  } catch (e) {
    toast.error(e instanceof Error ? e.message : 'Save failed.')
  }
}

async function loadOverview() {
  if (!projectId.value) return
  loadingOverview.value = true
  try {
    overview.value = await opsApi.getOverview(connId, {
      projectId: projectId.value,
      environmentId: environmentId.value || undefined,
    })
  } catch (e) {
    toast.error(e instanceof Error ? e.message : 'Failed to load overview.')
  } finally {
    loadingOverview.value = false
  }
}

function openMetrics(serviceId?: string) {
  if (serviceId) {
    metricsAggregate.value = false
    metricsServiceId.value = serviceId
  } else {
    metricsServiceId.value = metricsServiceId.value || projectDetail.value?.services[0]?.id || ''
  }
  tab.value = 'metrics'
  loadMetrics()
}

async function loadMetrics() {
  if (!environmentId.value) {
    toast.error('Pick an environment.')
    return
  }
  if (!metricsAggregate.value && !metricsServiceId.value) {
    toast.error('Pick a service.')
    return
  }
  loadingMetrics.value = true
  metricsError.value = null
  try {
    metricsPoints.value = metricsAggregate.value
      ? await opsApi.getAggregateMetrics(connId, {
          projectId: projectId.value,
          environmentId: environmentId.value,
          windowMinutes: metricsWindow.value,
        })
      : await opsApi.getMetrics(connId, {
          serviceId: metricsServiceId.value,
          environmentId: environmentId.value,
          windowMinutes: metricsWindow.value,
        })
    if (metricsPoints.value.length === 0) metricsError.value = 'No metric data returned for this window.'
  } catch (e) {
    metricsError.value = e instanceof Error ? e.message : 'Failed to load metrics.'
  } finally {
    loadingMetrics.value = false
  }
}

onMounted(loadConnection)
onUnmounted(() => {
  stream.stop()
  varRows.value = [] // don't leave decrypted env-var secrets in memory after leaving
})
</script>

<template>
  <div class="max-w-6xl mx-auto">
    <RouterLink to="/connections" class="text-faint text-sm hover:underline">← Connections</RouterLink>

    <div v-if="loading" class="text-muted mt-4">Loading…</div>

    <template v-else-if="conn">
      <div class="flex items-center justify-between mt-2 mb-5">
        <div>
          <h1 class="font-display text-2xl font-bold">{{ conn.name }}</h1>
          <div class="flex items-center gap-2 mt-1">
            <span class="badge badge-blue">{{ conn.provider }}</span>
            <span class="badge">{{ roleLabel(conn.role) }}</span>
            <span class="text-faint text-sm">{{ conn.accountLabel }}</span>
          </div>
        </div>
      </div>

      <!-- Providers with no manageable resources yet (e.g. VPS scaffold) -->
      <div v-if="!caps?.projects && !caps?.deployments" class="card p-8 text-center">
        <div class="text-lg font-semibold mb-1">Reachability only, for now</div>
        <p class="text-muted text-sm">
          This provider ({{ conn.provider }}) is connected and monitored in the fleet. Deployment,
          log and variable control ship as the provider is built out.
        </p>
      </div>

      <template v-else>
        <!-- Scope selectors -->
        <div class="card p-4 mb-4 grid sm:grid-cols-3 gap-3">
          <div>
            <label class="field-label">Project</label>
            <select class="field-input" :value="projectId" @change="selectProject(($event.target as HTMLSelectElement).value)">
              <option v-for="p in projects" :key="p.id" :value="p.id">{{ p.name }}</option>
            </select>
          </div>
          <div>
            <label class="field-label">Service</label>
            <select v-model="serviceId" class="field-input" @change="loadDeployments">
              <option value="">All services</option>
              <option v-for="s in projectDetail?.services" :key="s.id" :value="s.id">{{ s.name }}</option>
            </select>
          </div>
          <div>
            <label class="field-label">Environment</label>
            <select v-model="environmentId" class="field-input" @change="loadDeployments">
              <option v-for="e in projectDetail?.environments" :key="e.id" :value="e.id">{{ e.name }}</option>
            </select>
          </div>
        </div>

        <!-- Tabs -->
        <div class="flex gap-2 mb-4 flex-wrap">
          <button
            v-if="caps?.overview"
            class="btn-ghost"
            :style="tab === 'overview' ? 'border-color: var(--accent); color: var(--accent)' : ''"
            @click="tab = 'overview'; loadOverview()"
          >
            Overview
          </button>
          <button
            v-if="caps?.deployments"
            class="btn-ghost"
            :style="tab === 'deployments' ? 'border-color: var(--accent); color: var(--accent)' : ''"
            @click="tab = 'deployments'"
          >
            Deployments
          </button>
          <button
            v-if="caps?.deployLogs || caps?.buildLogs || caps?.httpLogs"
            class="btn-ghost"
            :style="tab === 'logs' ? 'border-color: var(--accent); color: var(--accent)' : ''"
            @click="tab = 'logs'"
          >
            Logs
          </button>
          <button
            v-if="caps?.variables"
            class="btn-ghost"
            :style="tab === 'variables' ? 'border-color: var(--accent); color: var(--accent)' : ''"
            @click="tab = 'variables'; loadVariables()"
          >
            Variables
          </button>
          <button
            v-if="caps?.metrics"
            class="btn-ghost"
            :style="tab === 'metrics' ? 'border-color: var(--accent); color: var(--accent)' : ''"
            @click="openMetrics()"
          >
            Metrics
          </button>
        </div>

        <!-- Overview (process / service canvas, Railway-dashboard style) -->
        <div v-if="tab === 'overview'">
          <div class="flex items-center justify-between mb-3">
            <div class="text-sm text-muted">{{ overview.length }} service(s)</div>
            <button class="btn-ghost !py-1.5" @click="loadOverview">Refresh</button>
          </div>
          <div v-if="loadingOverview" class="text-muted">Loading…</div>
          <div v-else-if="overview.length === 0" class="card p-8 text-center text-muted">
            No services in this project.
          </div>
          <div v-else class="grid sm:grid-cols-2 lg:grid-cols-3 gap-4">
            <div
              v-for="s in overview"
              :key="s.serviceId"
              class="card p-4 flex flex-col gap-2 cursor-pointer transition hover:shadow-pop"
              role="button"
              tabindex="0"
              @click="openService({ id: s.serviceId, name: s.serviceName })"
              @keydown.enter="openService({ id: s.serviceId, name: s.serviceName })"
            >
              <div class="flex items-start justify-between gap-2">
                <div class="font-semibold">{{ s.serviceName }}</div>
                <span :class="deploymentBadgeClass(s.status || 'UNKNOWN')">{{ s.status || 'n/a' }}</span>
              </div>
              <a
                v-if="s.url"
                :href="s.url.startsWith('http') ? s.url : `https://${s.url}`"
                target="_blank"
                rel="noopener"
                class="text-xs hover:underline truncate"
                style="color: var(--accent)"
                @click.stop
              >
                {{ s.url }}
              </a>
              <div class="text-faint text-xs">Latest deploy {{ formatDate(s.latestCreatedAt) }}</div>
              <div class="text-sm font-semibold mt-auto pt-2" style="color: var(--accent)">Manage service ▸</div>
            </div>
          </div>
        </div>

        <!-- Deployments -->
        <div v-else-if="tab === 'deployments'" class="card overflow-x-auto">
          <div class="flex items-center justify-between p-3">
            <div class="text-sm text-muted">{{ deployments.length }} deployment(s)</div>
            <div class="flex gap-2">
              <button class="btn-ghost !py-1.5" @click="loadDeployments">Refresh</button>
              <button v-if="caps?.triggerDeploy" class="pill-btn !py-1.5" @click="triggerDeploy">Deploy now</button>
            </div>
          </div>
          <div v-if="loadingDeps" class="p-4 text-muted">Loading…</div>
          <table v-else class="data-table">
            <thead>
              <tr>
                <th>ID</th>
                <th>Status</th>
                <th>Created</th>
                <th class="text-right">Actions</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="d in deployments" :key="d.id">
                <td class="font-mono text-xs">{{ d.id.slice(0, 12) }}</td>
                <td><span :class="deploymentBadgeClass(d.status)">{{ d.status }}</span></td>
                <td class="text-faint text-xs">{{ formatDate(d.createdAt) }}</td>
                <td>
                  <div class="flex justify-end gap-1.5 flex-wrap">
                    <button class="btn-ghost !py-1" @click="useForLogs(d)">Logs</button>
                    <button
                      v-if="caps?.redeploy"
                      class="btn-ghost !py-1"
                      :disabled="busyAction === `${d.id}:redeploy`"
                      @click="act(d, 'redeploy')"
                    >
                      Redeploy
                    </button>
                    <button
                      v-if="caps?.restart"
                      class="btn-ghost !py-1"
                      :disabled="busyAction === `${d.id}:restart`"
                      @click="act(d, 'restart')"
                    >
                      Restart
                    </button>
                    <button
                      v-if="caps?.rollback && d.canRollback"
                      class="btn-ghost !py-1"
                      @click="act(d, 'rollback')"
                    >
                      Rollback
                    </button>
                    <button v-if="caps?.stop" class="btn-danger !py-1" @click="act(d, 'stop')">Stop</button>
                  </div>
                </td>
              </tr>
              <tr v-if="deployments.length === 0">
                <td colspan="4" class="text-center text-muted py-6">No deployments.</td>
              </tr>
            </tbody>
          </table>
        </div>

        <!-- Logs -->
        <div v-else-if="tab === 'logs'" class="card p-4">
          <div class="grid sm:grid-cols-4 gap-3 mb-3">
            <div>
              <label class="field-label">Type</label>
              <select v-model="logType" class="field-input">
                <option v-if="caps?.deployLogs" value="deploy">Deploy</option>
                <option v-if="caps?.buildLogs" value="build">Build</option>
                <option v-if="caps?.httpLogs" value="http">HTTP</option>
              </select>
            </div>
            <div class="sm:col-span-2">
              <label class="field-label">Deployment ID</label>
              <input v-model="deploymentId" class="field-input font-mono text-xs" placeholder="deployment id" />
            </div>
            <div>
              <label class="field-label">Filter</label>
              <input v-model="filter" class="field-input" placeholder="@level:error" />
            </div>
          </div>
          <div class="flex gap-2 mb-3">
            <button class="pill-btn !py-1.5" :disabled="loadingLogs" @click="fetchLogs">Fetch</button>
            <button
              v-if="caps?.liveTail"
              class="btn-ghost !py-1.5"
              :style="live ? 'border-color: var(--green); color: var(--green)' : ''"
              @click="toggleLive"
            >
              {{ live ? '● Live' : 'Live tail' }}
            </button>
          </div>
          <div
            class="rounded-xl overflow-y-auto"
            style="background: var(--paperHi); border: 1px solid var(--ink12); max-height: 60vh"
          >
            <div v-if="logs.length === 0" class="p-4 text-faint text-sm">No log lines.</div>
            <div v-for="(l, i) in logs" :key="i" class="log-line">
              <span class="text-faint">{{ formatDate(l.timestamp) }}</span>
              <span v-if="l.severity" :class="severityClass(l.severity)" class="mx-2">{{ l.severity }}</span>
              {{ l.message }}
            </div>
          </div>
        </div>

        <!-- Variables -->
        <div v-else-if="tab === 'variables'" class="card p-4">
          <div class="flex items-center justify-between mb-3">
            <button class="btn-ghost !py-1.5" @click="loadVariables">Reload</button>
            <div class="flex items-center gap-3">
              <label class="text-sm flex items-center gap-2">
                <input type="checkbox" v-model="replaceVars" /> Replace all
              </label>
              <button class="btn-ghost !py-1.5" @click="addVar">+ Add</button>
              <button class="pill-btn !py-1.5" @click="saveVariables">Save</button>
            </div>
          </div>
          <div v-if="loadingVars" class="text-muted">Loading…</div>
          <div v-else class="space-y-2">
            <div v-for="(v, i) in varRows" :key="i" class="flex gap-2">
              <input v-model="v.key" class="field-input font-mono text-xs w-1/3" placeholder="KEY" />
              <input
                v-model="v.value"
                :type="v.reveal ? 'text' : 'password'"
                class="field-input font-mono text-xs flex-1"
                placeholder="value"
                autocomplete="off"
              />
              <button
                type="button"
                class="btn-ghost !py-1.5"
                :title="v.reveal ? 'Hide value' : 'Reveal value'"
                :aria-label="v.reveal ? 'Hide value' : 'Reveal value'"
                @click="v.reveal = !v.reveal"
              >
                {{ v.reveal ? '🙈' : '👁' }}
              </button>
              <button class="btn-danger !py-1.5" @click="removeVar(i)">✕</button>
            </div>
            <div v-if="varRows.length === 0" class="text-faint text-sm">No variables. Add one to begin.</div>
          </div>
        </div>

        <!-- Metrics -->
        <div v-else-if="tab === 'metrics'" class="card p-4">
          <div class="grid sm:grid-cols-4 gap-3 mb-4">
            <div>
              <label class="field-label">Scope</label>
              <select v-model="metricsAggregate" class="field-input">
                <option :value="true">Server total (all services)</option>
                <option :value="false">Single service</option>
              </select>
            </div>
            <div v-if="!metricsAggregate">
              <label class="field-label">Service</label>
              <select v-model="metricsServiceId" class="field-input">
                <option v-for="s in projectDetail?.services" :key="s.id" :value="s.id">{{ s.name }}</option>
              </select>
            </div>
            <div>
              <label class="field-label">Window</label>
              <select v-model.number="metricsWindow" class="field-input">
                <option :value="15">Last 15m</option>
                <option :value="60">Last 1h</option>
                <option :value="360">Last 6h</option>
                <option :value="1440">Last 24h</option>
              </select>
            </div>
            <div class="flex items-end">
              <button class="pill-btn !py-2 w-full" :disabled="loadingMetrics" @click="loadMetrics">
                {{ loadingMetrics ? 'Loading…' : 'Load metrics' }}
              </button>
            </div>
          </div>

          <div v-if="metricsError" class="badge badge-amber mb-3">{{ metricsError }}</div>

          <div v-if="metricsSeries.length" class="grid sm:grid-cols-2 gap-4">
            <div v-for="m in metricsSeries" :key="m.metric" class="card p-4">
              <div class="flex items-center justify-between mb-1">
                <span class="eyebrow">{{ metricLabel(m.metric) }}</span>
                <span class="font-mono text-sm">{{ m.last.toFixed(2) }}</span>
              </div>
              <Sparkline :values="m.values" :color="metricColor(m.metric)" />
              <div class="text-faint text-xs mt-1">peak {{ m.max.toFixed(2) }} · {{ m.values.length }} pts</div>
            </div>
          </div>
          <div v-else-if="!metricsError && !loadingMetrics" class="text-faint text-sm">
            Pick a service and load to see CPU, memory and network graphs.
          </div>

          <p class="text-faint text-xs mt-4">
            "Server total" sums CPU / memory / network across every service in the selected project.
          </p>
        </div>
      </template>

      <!-- Per-service drawer: every action for one service in one place -->
      <div v-if="serviceDrawerOpen && selectedService" class="modal-backdrop" @click.self="closeService">
        <div class="modal" style="max-width: 42rem">
          <div class="flex items-start justify-between mb-4">
            <div>
              <div class="eyebrow">Service</div>
              <h2 class="font-display text-xl font-bold">{{ selectedService.name }}</h2>
            </div>
            <button class="text-faint hover:opacity-70" @click="closeService">✕</button>
          </div>

          <!-- Cross-service quick links -->
          <div class="flex flex-wrap gap-2 mb-5">
            <button v-if="caps?.triggerDeploy" class="pill-btn !py-1.5" @click="triggerDeploy">Deploy now</button>
            <button
              v-if="caps?.deployLogs || caps?.buildLogs || caps?.httpLogs"
              class="btn-ghost !py-1.5"
              @click="serviceLogs()"
            >
              Logs
            </button>
            <button v-if="caps?.metrics" class="btn-ghost !py-1.5" @click="serviceMetrics">Metrics</button>
            <button v-if="caps?.variables" class="btn-ghost !py-1.5" @click="serviceVariables">Variables</button>
          </div>

          <div class="eyebrow mb-2">Deployments</div>
          <div v-if="loadingDeps" class="text-muted text-sm">Loading…</div>
          <div v-else-if="deployments.length === 0" class="text-faint text-sm">No deployments for this service.</div>
          <div v-else class="space-y-2 overflow-y-auto" style="max-height: 50vh">
            <div v-for="d in deployments" :key="d.id" class="rounded-xl p-3" style="border: 1px solid var(--ink12)">
              <div class="flex items-center justify-between gap-2 mb-2">
                <div class="flex items-center gap-2">
                  <span :class="deploymentBadgeClass(d.status)">{{ d.status }}</span>
                  <span class="font-mono text-xs text-faint">{{ d.id.slice(0, 12) }}</span>
                </div>
                <span class="text-faint text-xs">{{ formatDate(d.createdAt) }}</span>
              </div>
              <div class="flex flex-wrap gap-1.5">
                <button class="btn-ghost !py-1" @click="serviceLogs(d)">Logs</button>
                <button
                  v-if="caps?.redeploy"
                  class="btn-ghost !py-1"
                  :disabled="busyAction === `${d.id}:redeploy`"
                  @click="act(d, 'redeploy')"
                >
                  Redeploy
                </button>
                <button
                  v-if="caps?.restart"
                  class="btn-ghost !py-1"
                  :disabled="busyAction === `${d.id}:restart`"
                  @click="act(d, 'restart')"
                >
                  Restart
                </button>
                <button v-if="caps?.rollback && d.canRollback" class="btn-ghost !py-1" @click="act(d, 'rollback')">
                  Rollback
                </button>
                <button v-if="caps?.stop" class="btn-danger !py-1" @click="act(d, 'stop')">Stop</button>
                <button v-if="caps?.remove" class="btn-danger !py-1" @click="act(d, 'remove')">Remove</button>
              </div>
            </div>
          </div>
        </div>
      </div>
    </template>
  </div>
</template>
