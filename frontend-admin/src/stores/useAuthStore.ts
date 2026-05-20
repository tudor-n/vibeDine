import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import { authApi, orgApi } from '@/services/api-client'

export interface Membership {
  organizationId: string
  organizationName: string
  restaurantId: string | null
  restaurantName: string | null
  role: string
}

export interface UserProfile {
  id: string
  email: string
  fullName: string
  memberships: Membership[]
}

export interface AccessibleRestaurant {
  id: string
  name: string
  organizationId: string
}

const STORAGE_KEY = 'sr_admin_auth'

interface PersistedAuth {
  accessToken: string | null
  refreshToken: string | null
  activeRestaurantId: string | null
}

function loadPersisted(): PersistedAuth | null {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    return raw ? (JSON.parse(raw) as PersistedAuth) : null
  } catch {
    return null
  }
}

export const useAuthStore = defineStore('auth', () => {
  const persisted = loadPersisted()

  const accessToken = ref<string | null>(persisted?.accessToken ?? null)
  const refreshToken = ref<string | null>(persisted?.refreshToken ?? null)
  const activeRestaurantId = ref<string | null>(persisted?.activeRestaurantId ?? null)
  const user = ref<UserProfile | null>(null)
  const accessibleRestaurants = ref<AccessibleRestaurant[]>([])

  const isAuthenticated = computed(() => Boolean(accessToken.value))
  const activeRestaurant = computed(
    () => accessibleRestaurants.value.find((r) => r.id === activeRestaurantId.value) ?? null,
  )
  const activeOrganizationId = computed(() => activeRestaurant.value?.organizationId ?? null)
  const isOrgOwnerOfActive = computed(() =>
    Boolean(
      activeOrganizationId.value &&
        user.value?.memberships.some(
          (m) => m.organizationId === activeOrganizationId.value && m.role === 'OrgOwner',
        ),
    ),
  )

  function persist() {
    localStorage.setItem(
      STORAGE_KEY,
      JSON.stringify({
        accessToken: accessToken.value,
        refreshToken: refreshToken.value,
        activeRestaurantId: activeRestaurantId.value,
      } satisfies PersistedAuth),
    )
  }

  function setSession(res: { accessToken: string; refreshToken: string; user: UserProfile }) {
    accessToken.value = res.accessToken
    refreshToken.value = res.refreshToken
    user.value = res.user
    persist()
  }

  async function login(email: string, password: string) {
    setSession(await authApi.login(email, password))
    await loadAccessibleRestaurants()
  }

  /**
   * Creates the account and triggers the confirmation email.
   * Does NOT log the user in — they must click the email link first.
   */
  async function register(email: string, password: string, fullName: string) {
    return await authApi.register(email, password, fullName)
  }

  /** Called by ConfirmEmailView once the user clicks the link in their inbox. */
  async function confirmEmail(userId: string, token: string) {
    setSession(await authApi.confirmEmail(userId, token))
    await loadAccessibleRestaurants()
  }

  async function resendConfirmation(email: string) {
    await authApi.resendConfirmation(email)
  }

  /** Returns true if the session was renewed. Used by the api-client on 401. */
  async function tryRefresh(): Promise<boolean> {
    if (!refreshToken.value) return false
    try {
      setSession(await authApi.refresh(refreshToken.value))
      return true
    } catch {
      logout()
      return false
    }
  }

  async function fetchMe() {
    user.value = await authApi.me()
  }

  async function loadAccessibleRestaurants() {
    const orgs = await orgApi.listOrganizations()
    const lists = await Promise.all(
      orgs.map(async (o) => {
        const restaurants = await orgApi.listRestaurants(o.id)
        return restaurants.map((r) => ({
          id: r.id,
          name: r.name,
          organizationId: o.id,
        }))
      }),
    )
    accessibleRestaurants.value = lists.flat()

    // Keep a valid active restaurant selected.
    if (
      accessibleRestaurants.value.length > 0 &&
      !accessibleRestaurants.value.some((r) => r.id === activeRestaurantId.value)
    ) {
      activeRestaurantId.value = accessibleRestaurants.value[0].id
      persist()
    }
  }

  function setActiveRestaurant(id: string) {
    activeRestaurantId.value = id
    persist()
  }

  function logout() {
    accessToken.value = null
    refreshToken.value = null
    user.value = null
    activeRestaurantId.value = null
    accessibleRestaurants.value = []
    localStorage.removeItem(STORAGE_KEY)
  }

  return {
    accessToken,
    refreshToken,
    activeRestaurantId,
    user,
    accessibleRestaurants,
    isAuthenticated,
    activeRestaurant,
    activeOrganizationId,
    isOrgOwnerOfActive,
    login,
    register,
    confirmEmail,
    resendConfirmation,
    tryRefresh,
    fetchMe,
    loadAccessibleRestaurants,
    setActiveRestaurant,
    logout,
  }
})
