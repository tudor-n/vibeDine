<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/useAuthStore'
import ToastHost from '@/components/ToastHost.vue'

const route = useRoute()
const router = useRouter()
const auth = useAuthStore()

const isBare = computed(() => route.meta.public === true)

const nav = [
  { name: 'dashboard', label: 'Dashboard', to: '/' },
  { name: 'organizations', label: 'Organizations', to: '/organizations' },
  { name: 'locations', label: 'Locations', to: '/locations' },
  { name: 'registration-links', label: 'Registration links', to: '/registration-links' },
]

function logout() {
  auth.logout()
  router.push({ name: 'login' })
}

// ── Theme toggle ──────────────────────────────────────────────────────────
const THEME_KEY = 'vibedine.platform.theme'
const theme = ref<'light' | 'dark'>('light')

function applyTheme(value: 'light' | 'dark') {
  document.documentElement.setAttribute('data-theme', value)
}
function toggleTheme() {
  theme.value = theme.value === 'dark' ? 'light' : 'dark'
  localStorage.setItem(THEME_KEY, theme.value)
  applyTheme(theme.value)
}

onMounted(() => {
  const saved = localStorage.getItem(THEME_KEY)
  theme.value = saved === 'dark' ? 'dark' : 'light'
  applyTheme(theme.value)
})
</script>

<template>
  <ToastHost />

  <RouterView v-if="isBare" />

  <div v-else class="min-h-screen flex flex-col md:flex-row">
    <!-- Sidebar -->
    <aside
      class="md:w-64 shrink-0 border-b md:border-b-0 md:border-r flex md:flex-col gap-1 p-4"
      :style="{ borderColor: 'var(--ink12)', background: 'var(--paperHi)' }"
    >
      <div class="px-2 mb-4 hidden md:block">
        <div class="font-display text-xl font-extrabold tracking-tight">vibeDine</div>
        <div class="eyebrow mt-0.5">HQ Console</div>
      </div>
      <nav class="flex md:flex-col gap-1 flex-1 overflow-x-auto">
        <RouterLink v-for="item in nav" :key="item.name" :to="item.to" class="nav-link whitespace-nowrap">
          {{ item.label }}
        </RouterLink>
      </nav>
    </aside>

    <!-- Main -->
    <div class="flex-1 min-w-0 flex flex-col">
      <header
        class="flex items-center justify-between gap-3 px-5 md:px-8 py-3 border-b"
        :style="{ borderColor: 'var(--ink12)' }"
      >
        <div class="eyebrow">Platform control plane</div>
        <div class="flex items-center gap-3">
          <button class="btn-ghost !px-3 !py-2 !min-h-0" @click="toggleTheme" :title="theme === 'dark' ? 'Light mode' : 'Dark mode'">
            {{ theme === 'dark' ? '☀' : '☾' }}
          </button>
          <span class="text-sm text-muted hidden sm:inline">{{ auth.admin?.email }}</span>
          <button class="btn-ghost !px-4 !py-2 !min-h-0" @click="logout">Sign out</button>
        </div>
      </header>

      <main class="flex-1 px-5 md:px-8 py-6 max-w-[1200px] w-full">
        <RouterView />
      </main>
    </div>
  </div>
</template>
