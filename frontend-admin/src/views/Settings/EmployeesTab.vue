<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useAuthStore, type AccessibleOrganization } from '@/stores/useAuthStore'
import { orgApi, waiterApi, type EmployeeRow } from '@/services/api-client'
import { useToast } from '@/composables/useToast'

const auth = useAuthStore()
const toast = useToast()

const employees = ref<EmployeeRow[]>([])
const loading = ref(false)
const busy = ref(false)
const selectedOrgId = ref<string | null>(null)

const ownedOrgs = computed<AccessibleOrganization[]>(() =>
  auth.accessibleOrganizations.filter((o: AccessibleOrganization) => o.role === 'OrgOwner'),
)

const grouped = computed(() => {
  const map = new Map<string, EmployeeRow[]>()
  for (const e of employees.value) {
    const key = e.restaurantId ?? 'org-wide'
    if (!map.has(key)) map.set(key, [])
    map.get(key)!.push(e)
  }
  return Array.from(map.entries()).map(([key, items]) => ({
    key,
    title: key === 'org-wide' ? 'Organizație (fără locație)' : items[0]?.restaurantName ?? 'Restaurant',
    items,
  }))
})

async function load() {
  if (!selectedOrgId.value) {
    employees.value = []
    return
  }
  loading.value = true
  try {
    employees.value = await orgApi.listEmployees(selectedOrgId.value)
  } catch (e) {
    toast.error(e instanceof Error ? e.message : 'Eroare la încărcarea angajaților.')
  } finally {
    loading.value = false
  }
}

onMounted(async () => {
  if (auth.accessibleOrganizations.length === 0) {
    await auth.loadAccessibleOrganizations()
  }
  // Pre-select the currently active org if the user owns it.
  if (auth.activeOrganizationId && ownedOrgs.value.some((o: AccessibleOrganization) => o.id === auth.activeOrganizationId)) {
    selectedOrgId.value = auth.activeOrganizationId
  } else if (ownedOrgs.value.length > 0) {
    selectedOrgId.value = ownedOrgs.value[0].id
  }
  await load()
})

watch(selectedOrgId, load)

function roleLabel(role: string) {
  if (role === 'OrgOwner') return 'Proprietar'
  if (role === 'RestaurantAdmin') return 'Administrator'
  if (role === 'Waiter') return 'Ospătar'
  return role
}

function statusLabel(status: string) {
  if (status === 'pending_approval') return 'În așteptare'
  if (status === 'rejected') return 'Respins'
  return 'Activ'
}

async function approve(e: EmployeeRow) {
  busy.value = true
  try {
    await waiterApi.approve(e.membershipId)
    await load()
    toast.success(`${e.fullName} aprobat.`)
  } catch (err) {
    toast.error(err instanceof Error ? err.message : 'Eroare la aprobare.')
  } finally {
    busy.value = false
  }
}

async function reject(e: EmployeeRow) {
  busy.value = true
  try {
    await waiterApi.reject(e.membershipId)
    await load()
    toast.success(`${e.fullName} respins.`)
  } catch (err) {
    toast.error(err instanceof Error ? err.message : 'Eroare la respingere.')
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <div>
    <div v-if="ownedOrgs.length === 0" class="card p-6 italic">
      Doar proprietarul unei organizații poate vedea angajații.
    </div>

    <template v-else>
      <div class="mb-4 flex flex-wrap items-center gap-3">
        <label class="text-sm font-semibold">Organizație</label>
        <select v-model="selectedOrgId" class="select">
          <option v-for="o in ownedOrgs" :key="o.id" :value="o.id">{{ o.name }}</option>
        </select>
      </div>

      <div v-if="loading" class="text-ink-70 text-sm">Se încarcă…</div>

      <div v-else-if="!employees.length" class="card p-6 text-ink-70">Niciun angajat încă.</div>

      <div v-else class="flex flex-col gap-6">
        <section v-for="group in grouped" :key="group.key" class="card p-5">
          <div class="eyebrow mb-3">{{ group.title }} · {{ group.items.length }}</div>
          <table class="emp-table">
            <thead>
              <tr>
                <th>Persoană</th>
                <th>Rol</th>
                <th>Status</th>
                <th class="text-right">Acțiuni</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="e in group.items" :key="e.membershipId">
                <td>
                  <div class="flex items-center gap-3">
                    <div class="avatar-mini">
                      <img v-if="e.avatarUrl" :src="e.avatarUrl" alt="" />
                      <span v-else>{{ e.fullName.charAt(0).toUpperCase() }}</span>
                    </div>
                    <div class="min-w-0">
                      <div class="font-semibold truncate">{{ e.fullName }}</div>
                      <div class="text-xs text-ink-70 font-mono truncate">{{ e.email }}</div>
                    </div>
                  </div>
                </td>
                <td>{{ roleLabel(e.role) }}</td>
                <td>
                  <span :class="['status-pill', `status-${e.status}`]">{{ statusLabel(e.status) }}</span>
                </td>
                <td class="text-right">
                  <template v-if="e.role === 'Waiter' && e.status === 'pending_approval'">
                    <button type="button" class="btn btn-primary mr-2" :disabled="busy" @click="approve(e)">
                      Aprobă
                    </button>
                    <button type="button" class="btn btn-danger" :disabled="busy" @click="reject(e)">
                      Respinge
                    </button>
                  </template>
                </td>
              </tr>
            </tbody>
          </table>
        </section>
      </div>
    </template>
  </div>
</template>

<style scoped>
.card {
  background: var(--card, var(--paperHi));
  border: 1px solid var(--ink12);
  border-radius: 18px;
  box-shadow: var(--shadow-soft);
}
.select {
  padding: 8px 12px;
  border-radius: 999px;
  border: 1px solid var(--ink12);
  background: var(--card, #fff);
  color: var(--ink);
  font-weight: 600;
  font-size: 14px;
}
.emp-table {
  width: 100%;
  border-collapse: collapse;
}
.emp-table th {
  text-align: left;
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 11px;
  text-transform: uppercase;
  letter-spacing: 0.06em;
  color: var(--ink-70, var(--ink));
  padding: 6px 8px;
  border-bottom: 1px solid var(--ink12);
}
.emp-table td {
  padding: 12px 8px;
  border-bottom: 1px solid var(--ink12);
  vertical-align: middle;
}
.emp-table tr:last-child td {
  border-bottom: none;
}
.text-right { text-align: right; }
.avatar-mini {
  width: 36px;
  height: 36px;
  border-radius: 50%;
  background: var(--ink);
  color: var(--paperHi);
  display: grid;
  place-items: center;
  font-weight: 700;
  font-size: 14px;
  overflow: hidden;
  flex-shrink: 0;
}
.avatar-mini img {
  width: 100%;
  height: 100%;
  object-fit: cover;
}
.status-pill {
  display: inline-flex;
  align-items: center;
  padding: 4px 10px;
  border-radius: 999px;
  font-size: 11px;
  font-weight: 600;
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  text-transform: uppercase;
  letter-spacing: 0.06em;
}
.status-active { background: var(--lime, #b8e986); color: var(--onLime, #0f3a0f); }
.status-pending_approval { background: #fde68a; color: #92400e; }
.status-rejected { background: #fecaca; color: #991b1b; }
.btn {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 6px 12px;
  border-radius: 999px;
  border: 1px solid var(--ink12);
  background: transparent;
  color: var(--ink);
  font-weight: 600;
  font-size: 13px;
  cursor: pointer;
}
.btn:hover { background: var(--ink06); }
.btn:disabled { opacity: 0.55; cursor: not-allowed; }
.btn-primary {
  background: var(--ink);
  color: var(--paperHi);
  border-color: var(--ink);
}
.btn-danger {
  border-color: #b91c1c;
  color: #b91c1c;
}
.btn-danger:hover { background: rgba(185, 28, 28, 0.08); }
</style>
