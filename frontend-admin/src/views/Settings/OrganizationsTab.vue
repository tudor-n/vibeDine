<script setup lang="ts">
import { computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore, type AccessibleOrganization } from '@/stores/useAuthStore'

const auth = useAuthStore()
const router = useRouter()

onMounted(async () => {
  if (auth.accessibleOrganizations.length === 0) {
    await auth.loadAccessibleOrganizations()
  }
})

const orgs = computed<AccessibleOrganization[]>(() => auth.accessibleOrganizations)

function roleLabel(role: string) {
  if (role === 'OrgOwner') return 'Proprietar'
  if (role === 'RestaurantAdmin') return 'Administrator'
  if (role === 'Waiter') return 'Ospătar'
  return role
}

function switchTo(orgId: string) {
  auth.setActiveOrganization(orgId)
  router.push('/')
}
</script>

<template>
  <div class="grid gap-4 md:grid-cols-2">
    <div v-for="o in orgs" :key="o.id" class="card p-5">
      <div class="flex items-start justify-between gap-3">
        <div class="min-w-0">
          <div class="text-lg font-semibold truncate">{{ o.name }}</div>
          <div class="mt-1 font-mono text-[12px] uppercase tracking-[0.06em] text-ink-70">
            {{ roleLabel(o.role) }} · {{ o.restaurantCount }} restaurant{{ o.restaurantCount === 1 ? '' : 'e' }}
          </div>
        </div>
        <button
          v-if="auth.activeOrganizationId !== o.id"
          type="button"
          class="btn"
          @click="switchTo(o.id)"
        >
          Comută aici
        </button>
        <span v-else class="active-badge">activ</span>
      </div>
    </div>
  </div>
</template>

<style scoped>
.card {
  background: var(--card, var(--paperHi));
  border: 1px solid var(--ink12);
  border-radius: 18px;
  box-shadow: var(--shadow-soft);
}
.btn {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 8px 14px;
  border-radius: 999px;
  border: 1px solid var(--ink12);
  background: transparent;
  color: var(--ink);
  font-weight: 600;
  cursor: pointer;
}
.btn:hover { background: var(--ink06); }
.active-badge {
  padding: 6px 12px;
  border-radius: 999px;
  background: var(--lime, #b8e986);
  color: var(--onLime, #0f3a0f);
  font-weight: 600;
  font-size: 12px;
  text-transform: uppercase;
  letter-spacing: 0.06em;
  font-family: 'JetBrains Mono', ui-monospace, monospace;
}
</style>
