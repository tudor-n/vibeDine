import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import { authApi } from '@/services/api-client'
import type { PlatformAdmin } from '@/types/platform'

const STORAGE_KEY = 'vibedine_platform_auth'

interface Persisted {
  accessToken: string | null
  admin: PlatformAdmin | null
}

function load(): Persisted | null {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    return raw ? (JSON.parse(raw) as Persisted) : null
  } catch {
    return null
  }
}

export const useAuthStore = defineStore('platformAuth', () => {
  const persisted = load()
  const accessToken = ref<string | null>(persisted?.accessToken ?? null)
  const admin = ref<PlatformAdmin | null>(persisted?.admin ?? null)

  const isAuthenticated = computed(() => Boolean(accessToken.value))

  function persist() {
    localStorage.setItem(
      STORAGE_KEY,
      JSON.stringify({ accessToken: accessToken.value, admin: admin.value } satisfies Persisted),
    )
  }

  async function login(email: string, password: string) {
    const res = await authApi.login(email, password)
    accessToken.value = res.accessToken
    admin.value = res.admin
    persist()
  }

  async function fetchMe() {
    admin.value = await authApi.me()
    persist()
  }

  function logout() {
    accessToken.value = null
    admin.value = null
    localStorage.removeItem(STORAGE_KEY)
  }

  return { accessToken, admin, isAuthenticated, login, fetchMe, logout }
})
