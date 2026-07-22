<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/useAuthStore'
import ToastHost from '@/components/ToastHost.vue'

const auth = useAuthStore()
const route = useRoute()
const router = useRouter()

const isBare = computed(() => route.meta.public === true)

const nav = [
  { name: 'fleet', label: 'Fleet', to: '/' },
  { name: 'metrics', label: 'Server metrics', to: '/metrics' },
  { name: 'connections', label: 'Connections', to: '/connections' },
  { name: 'audit', label: 'Audit log', to: '/audit' },
]

const theme = ref<'light' | 'dark'>('light')
const THEME_KEY = 'vibedine.ops.theme'

onMounted(() => {
  const saved = (localStorage.getItem(THEME_KEY) as 'light' | 'dark' | null) ?? 'light'
  applyTheme(saved)
})

function applyTheme(value: 'light' | 'dark') {
  theme.value = value
  document.documentElement.setAttribute('data-theme', value)
  localStorage.setItem(THEME_KEY, value)
}

function toggleTheme() {
  applyTheme(theme.value === 'light' ? 'dark' : 'light')
}

function logout() {
  auth.logout()
  router.push({ name: 'login' })
}
</script>

<template>
  <ToastHost />

  <RouterView v-if="isBare" />

  <div v-else class="min-h-screen flex" style="background: var(--paper)">
    <aside class="hidden md:flex md:w-64 flex-col border-r p-4 gap-6" style="border-color: var(--ink12)">
      <div>
        <div class="font-display text-xl font-extrabold tracking-tight">vibeDine</div>
        <div class="eyebrow mt-0.5">Ops Console</div>
      </div>
      <nav class="flex flex-col gap-1">
        <RouterLink v-for="item in nav" :key="item.name" :to="item.to" class="nav-link">
          {{ item.label }}
        </RouterLink>
      </nav>
      <div class="mt-auto text-faint text-xs">
        Server &amp; infrastructure control plane
      </div>
    </aside>

    <div class="flex-1 flex flex-col min-w-0">
      <header
        class="flex items-center justify-between px-5 py-3 border-b"
        style="border-color: var(--ink12)"
      >
        <div class="eyebrow">Infrastructure management</div>
        <div class="flex items-center gap-3">
          <button class="btn-ghost !min-h-[34px] !py-1.5" @click="toggleTheme">
            {{ theme === 'light' ? '🌙' : '☀️' }}
          </button>
          <span class="text-muted text-sm hidden sm:inline">{{ auth.admin?.email }}</span>
          <button class="btn-ghost !min-h-[34px] !py-1.5" @click="logout">Sign out</button>
        </div>
      </header>
      <main class="flex-1 p-5 md:p-8 overflow-x-hidden">
        <RouterView />
      </main>
    </div>
  </div>
</template>
