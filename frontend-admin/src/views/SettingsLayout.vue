<script setup lang="ts">
import { computed } from 'vue'
import { useAuthStore } from '@/stores/useAuthStore'

const auth = useAuthStore()
const showManagerTabs = computed(() => !auth.isWaiter)
</script>

<template>
  <div class="max-w-5xl mx-auto">
    <div class="mb-6">
      <div class="eyebrow mb-4">Setări</div>
      <h1 class="display-title text-[44px] md:text-[56px]">Contul tău</h1>
    </div>

    <nav class="settings-tabs mb-6 flex flex-wrap gap-2" aria-label="Setări">
      <RouterLink to="/settings/profile" class="tab" active-class="tab-active">
        Profil
      </RouterLink>
      <RouterLink v-if="showManagerTabs" to="/settings/organizations" class="tab" active-class="tab-active">
        Organizații
      </RouterLink>
      <RouterLink v-if="showManagerTabs" to="/settings/employees" class="tab" active-class="tab-active">
        Angajați
      </RouterLink>
    </nav>

    <RouterView />
  </div>
</template>

<style scoped>
.tab {
  display: inline-flex;
  align-items: center;
  padding: 10px 18px;
  border-radius: 999px;
  background: transparent;
  border: 1px solid var(--ink12);
  color: var(--ink);
  font-weight: 600;
  font-size: 14px;
  text-decoration: none;
}
.tab:hover { background: var(--ink06); }
.tab-active {
  background: var(--ink);
  color: var(--paperHi);
  border-color: var(--ink);
}
</style>
