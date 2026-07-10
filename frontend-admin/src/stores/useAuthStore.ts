import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import { authApi, orgApi } from '@/services/api-client'

export interface Membership {
  organizationId: string
  organizationName: string
  restaurantId: string | null
  restaurantName: string | null
  role: string
  status: string
}

export interface UserProfile {
  id: string
  email: string
  fullName: string
  avatarUrl: string | null
  memberships: Membership[]
}

export interface AccessibleRestaurant {
  id: string
  name: string
  organizationId: string
}

export interface AccessibleOrganization {
  id: string
  name: string
  role: string
  restaurantCount: number
}

const STORAGE_KEY = 'sr_admin_auth'

interface PersistedAuth {
  accessToken: string | null
  refreshToken: string | null
  activeOrganizationId: string | null
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

/** Pick the most privileged active membership a user holds for a given org. */
function pickRoleForOrg(memberships: Membership[] | undefined, orgId: string | null): string | null {
  if (!orgId || !memberships) return null
  const inOrg = memberships.filter((m) => m.organizationId === orgId && m.status === 'active')
  if (inOrg.some((m) => m.role === 'OrgOwner')) return 'OrgOwner'
  if (inOrg.some((m) => m.role === 'RestaurantAdmin')) return 'RestaurantAdmin'
  if (inOrg.some((m) => m.role === 'Waiter')) return 'Waiter'
  return null
}

export const useAuthStore = defineStore('auth', () => {
  const persisted = loadPersisted()

  const accessToken = ref<string | null>(persisted?.accessToken ?? null)
  const refreshToken = ref<string | null>(persisted?.refreshToken ?? null)
  const activeOrganizationId = ref<string | null>(persisted?.activeOrganizationId ?? null)
  const activeRestaurantId = ref<string | null>(persisted?.activeRestaurantId ?? null)
  const user = ref<UserProfile | null>(null)
  const accessibleOrganizations = ref<AccessibleOrganization[]>([])
  const accessibleRestaurants = ref<AccessibleRestaurant[]>([])

  const isAuthenticated = computed(() => Boolean(accessToken.value))

  const activeOrganization = computed<AccessibleOrganization | null>(
    () =>
      accessibleOrganizations.value.find((o: AccessibleOrganization) => o.id === activeOrganizationId.value) ?? null,
  )

  /** Restaurants the user can switch between *within the active organization*. */
  const restaurantsInActiveOrg = computed<AccessibleRestaurant[]>(() =>
    activeOrganizationId.value
      ? accessibleRestaurants.value.filter(
          (r: AccessibleRestaurant) => r.organizationId === activeOrganizationId.value,
        )
      : [],
  )

  const activeRestaurant = computed<AccessibleRestaurant | null>(
    () =>
      restaurantsInActiveOrg.value.find((r: AccessibleRestaurant) => r.id === activeRestaurantId.value) ?? null,
  )

  /** Role the user holds in the active org. Null when no org selected or user is not a member. */
  const activeRole = computed(() => pickRoleForOrg(user.value?.memberships, activeOrganizationId.value))

  const isOrgOwnerOfActive = computed(() => activeRole.value === 'OrgOwner')
  const isWaiter = computed(() => activeRole.value === 'Waiter')

  /** Managers (org owners + restaurant admins) can generate links and approve waiters. */
  const canManageWaitersOfActive = computed(
    () => activeRole.value === 'OrgOwner' || activeRole.value === 'RestaurantAdmin',
  )

  function persist() {
    localStorage.setItem(
      STORAGE_KEY,
      JSON.stringify({
        accessToken: accessToken.value,
        refreshToken: refreshToken.value,
        activeOrganizationId: activeOrganizationId.value,
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
    await loadAccessibleOrganizations()
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
    await loadAccessibleOrganizations()
  }

  async function resendConfirmation(email: string) {
    await authApi.resendConfirmation(email)
  }

  // Single-flight guard: while a refresh is in progress, every caller awaits the
  // same promise instead of firing its own POST /api/auth/refresh. This prevents
  // the reload logout-storm where N concurrent 401s trigger N refreshes and the
  // losers (rotated refresh token) wipe the session the winner just renewed.
  let refreshInFlight: Promise<boolean> | null = null

  /** Returns true if the session was renewed. Used by the api-client on 401. */
  async function tryRefresh(): Promise<boolean> {
    if (!refreshToken.value) return false
    if (refreshInFlight) return refreshInFlight
    refreshInFlight = (async () => {
      try {
        setSession(await authApi.refresh(refreshToken.value!))
        return true
      } catch {
        // Only the shared refresh may log out, and only when it actually fails.
        logout()
        return false
      } finally {
        refreshInFlight = null
      }
    })()
    return refreshInFlight
  }

  async function fetchMe() {
    user.value = await authApi.me()
  }

  /**
   * Loads the orgs the current user belongs to plus all restaurants in those orgs.
   * After login or page reload, the org picker drives selection — we no longer
   * auto-pick a restaurant. `activeRestaurantId` only gets set when an org is chosen.
   */
  async function loadAccessibleOrganizations() {
    const orgs = await orgApi.listOrganizations()
    const restaurantLists = await Promise.all(
      orgs.map((o) => orgApi.listRestaurants(o.id).then((rs) => ({ orgId: o.id, restaurants: rs }))),
    )

    accessibleRestaurants.value = restaurantLists.flatMap(
      (entry: { orgId: string; restaurants: { id: string; name: string }[] }) =>
        entry.restaurants.map((r: { id: string; name: string }) => ({
          id: r.id,
          name: r.name,
          organizationId: entry.orgId,
        })),
    )

    accessibleOrganizations.value = orgs.map((o: { id: string; name: string; role: string }) => {
      const entry = restaurantLists.find(
        (e: { orgId: string; restaurants: unknown[] }) => e.orgId === o.id,
      )
      return {
        id: o.id,
        name: o.name,
        role: o.role,
        restaurantCount: entry?.restaurants.length ?? 0,
      }
    })

    // If the persisted active org disappeared (revoked membership), clear selection.
    if (
      activeOrganizationId.value &&
      !accessibleOrganizations.value.some(
        (o: AccessibleOrganization) => o.id === activeOrganizationId.value,
      )
    ) {
      activeOrganizationId.value = null
      activeRestaurantId.value = null
      persist()
    } else if (
      activeOrganizationId.value &&
      !restaurantsInActiveOrg.value.some(
        (r: AccessibleRestaurant) => r.id === activeRestaurantId.value,
      )
    ) {
      // Active org still valid but restaurant disappeared — pick a fresh one.
      activeRestaurantId.value = restaurantsInActiveOrg.value[0]?.id ?? null
      persist()
    }
  }

  /** Backwards-compatible alias kept for any caller that hasn't been migrated yet. */
  async function loadAccessibleRestaurants() {
    return loadAccessibleOrganizations()
  }

  /** Enter an organization: sets it active and seeds a default restaurant pick. */
  function setActiveOrganization(orgId: string) {
    activeOrganizationId.value = orgId
    const first =
      accessibleRestaurants.value.find((r: AccessibleRestaurant) => r.organizationId === orgId) ?? null
    activeRestaurantId.value = first?.id ?? null
    persist()
  }

  /** Clear the active org selection — sends the user back to the picker. */
  function clearActiveOrganization() {
    activeOrganizationId.value = null
    activeRestaurantId.value = null
    persist()
  }

  function setActiveRestaurant(id: string) {
    activeRestaurantId.value = id
    // Keep org in sync if the restaurant was moved/switched across orgs.
    const r = accessibleRestaurants.value.find((x: AccessibleRestaurant) => x.id === id)
    if (r) activeOrganizationId.value = r.organizationId
    persist()
  }

  function logout() {
    accessToken.value = null
    refreshToken.value = null
    user.value = null
    activeOrganizationId.value = null
    activeRestaurantId.value = null
    accessibleOrganizations.value = []
    accessibleRestaurants.value = []
    localStorage.removeItem(STORAGE_KEY)
  }

  return {
    accessToken,
    refreshToken,
    activeOrganizationId,
    activeRestaurantId,
    user,
    accessibleOrganizations,
    accessibleRestaurants,
    isAuthenticated,
    activeOrganization,
    activeRestaurant,
    restaurantsInActiveOrg,
    activeRole,
    isOrgOwnerOfActive,
    isWaiter,
    canManageWaitersOfActive,
    login,
    register,
    confirmEmail,
    resendConfirmation,
    tryRefresh,
    fetchMe,
    loadAccessibleOrganizations,
    loadAccessibleRestaurants,
    setActiveOrganization,
    clearActiveOrganization,
    setActiveRestaurant,
    logout,
  }
})
