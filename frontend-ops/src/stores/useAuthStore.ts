import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import { authApi } from '@/services/api-client'
import type { OpsAdmin } from '@/types/ops'

const STORAGE_KEY = 'vibedine_ops_auth'

interface Persisted {
  accessToken: string | null
  expiresAt: string | null
  admin: OpsAdmin | null
}

function load(): Persisted | null {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    return raw ? (JSON.parse(raw) as Persisted) : null
  } catch {
    return null
  }
}

export const useAuthStore = defineStore('opsAuth', () => {
  const persisted = load()
  const accessToken = ref<string | null>(persisted?.accessToken ?? null)
  const expiresAt = ref<string | null>(persisted?.expiresAt ?? null)
  const admin = ref<OpsAdmin | null>(persisted?.admin ?? null)

  // An expired token is treated as unauthenticated: a stale/tampered token must not
  // render the privileged shell. NaN (missing/invalid expiry) is not < now, so it
  // stays presence-only rather than logging valid sessions out spuriously.
  const isAuthenticated = computed(() => {
    if (!accessToken.value) return false
    if (expiresAt.value && Date.parse(expiresAt.value) <= Date.now()) return false
    return true
  })

  function persist() {
    localStorage.setItem(
      STORAGE_KEY,
      JSON.stringify({
        accessToken: accessToken.value,
        expiresAt: expiresAt.value,
        admin: admin.value,
      } satisfies Persisted),
    )
  }

  async function login(email: string, password: string) {
    const res = await authApi.login(email, password)
    accessToken.value = res.accessToken
    expiresAt.value = res.expiresAt
    admin.value = res.admin
    persist()
  }

  async function fetchMe() {
    admin.value = await authApi.me()
    persist()
  }

  function logout() {
    accessToken.value = null
    expiresAt.value = null
    admin.value = null
    localStorage.removeItem(STORAGE_KEY)
  }

  return { accessToken, expiresAt, admin, isAuthenticated, login, fetchMe, logout }
})
