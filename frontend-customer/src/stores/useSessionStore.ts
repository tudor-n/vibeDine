import { defineStore } from 'pinia'
import { ref } from 'vue'
import { useGuestStore } from '@/stores/useGuestStore'
import { useTheme } from '@/composables/useTheme'
import { writeCachedBrand } from '@shared/branding/cache'
import { roundMoney } from '@/utils/money'
import type { JoinTableResponse } from '@/services/api-client'
import type { DeviceSessionOrder, PendingFlashDealPayload, RestaurantBranding } from '@shared/types/models'

export const useSessionStore = defineStore('session' , () => {
  const tableId = ref<string | null>(sessionStorage.getItem('tableId'))
  const orderId = ref<string | null>(sessionStorage.getItem('orderId'))
  const qrToken = ref<string | null>(sessionStorage.getItem('qrToken'))
  const tableNumber = ref<string | null>(sessionStorage.getItem('tableNumber'))
  const restaurantId = ref<string | null>(sessionStorage.getItem('restaurantId'))
  const restaurantName = ref<string | null>(sessionStorage.getItem('restaurantName'))
  const restaurantAddress = ref<string | null>(sessionStorage.getItem('restaurantAddress'))
  const organizationName = ref<string | null>(sessionStorage.getItem('organizationName'))
  // Persisted to sessionStorage as a render hint only — join always overwrites.
  // The brand cache in localStorage restores COLOURS before mount, but the
  // branding images (welcome photo, logo, menu background) only exist here; a
  // mid-session reload renders its restored screen immediately (no splash), so
  // without this restore those images pop in when join lands — the diner sees a
  // default-looking screen first, then the design. Restored only when the tab's
  // stored session belongs to the table in the URL, so opening another table's
  // QR in this tab can't paint the previous restaurant's images.
  function readStoredBranding(): RestaurantBranding | null {
    try {
      const urlToken = window.location.pathname.split('/')[1] || null
      if (!urlToken || sessionStorage.getItem('qrToken') !== urlToken) return null
      const raw = sessionStorage.getItem('branding')
      return raw ? (JSON.parse(raw) as RestaurantBranding) : null
    } catch {
      return null
    }
  }
  const branding = ref<RestaurantBranding | null>(readStoredBranding())

  // Device-session summary (multi-round visit tracking, see Task 2/3 wire
  // contract). sessionOrders/cumulativeTotal/deviceSessionId/linkExpirySeconds
  // mirror the existing sessionStorage-persisted fields above: a same-tab
  // reload before the next join lands still has them. pendingDeal is
  // deliberately NOT persisted — it's restored fresh from each join, since a
  // stale claimable deal (past its expiry) is worse than a missing one.
  function readStoredNumber(key: string): number | null {
    const raw = sessionStorage.getItem(key)
    if (raw === null) return null
    const n = Number(raw)
    return Number.isFinite(n) ? n : null
  }

  function readStoredOrders(): DeviceSessionOrder[] {
    try {
      const raw = sessionStorage.getItem('sessionOrders')
      return raw ? (JSON.parse(raw) as DeviceSessionOrder[]) : []
    } catch {
      return []
    }
  }

  const deviceSessionId = ref<string | null>(sessionStorage.getItem('deviceSessionId'))
  const linkExpirySeconds = ref<number | null>(readStoredNumber('linkExpirySeconds'))
  const cumulativeTotal = ref<number>(readStoredNumber('cumulativeTotal') ?? 0)
  const sessionOrders = ref<DeviceSessionOrder[]>(readStoredOrders())
  const pendingDeal = ref<PendingFlashDealPayload | null>(null)

  async function initializeSession(token: string): Promise<JoinTableResponse>{
    try{
      // Guest-aware entry: this phone gets/keeps its OWN draft under the shared
      // table (so multiple people can order independently). orderId now points
      // at the guest's draft rather than a single table-wide draft.
      const guestStore = useGuestStore()
      const response = await guestStore.join(token)

      tableId.value = response.tableId
      orderId.value = response.activeOrder.id
      qrToken.value = token
      tableNumber.value = response.tableNumber
      restaurantId.value = response.restaurantId
      restaurantName.value = response.restaurantName
      restaurantAddress.value = response.restaurantAddress
      organizationName.value = response.organizationName

      branding.value = response.branding ?? null
      const b = branding.value
      const { applyBrand } = useTheme()
      // Cache per qrToken so a refresh/re-scan of this table paints the brand on
      // the first frame instead of flashing the default palette until join lands.
      if (b?.enabled && b.primary && b.secondary && b.background) {
        const colors = { primary: b.primary, secondary: b.secondary, background: b.background }
        applyBrand(colors)
        writeCachedBrand(token, colors)
      } else {
        applyBrand(null)
        writeCachedBrand(token, null)
      }

      if (branding.value) sessionStorage.setItem('branding', JSON.stringify(branding.value))
      else sessionStorage.removeItem('branding')

      sessionStorage.setItem('tableId', response.tableId)
      sessionStorage.setItem('orderId', response.activeOrder.id)
      sessionStorage.setItem('qrToken', token)
      sessionStorage.setItem('tableNumber', response.tableNumber)
      sessionStorage.setItem('restaurantId', response.restaurantId)
      sessionStorage.setItem('restaurantName', response.restaurantName)
      sessionStorage.setItem('restaurantAddress', response.restaurantAddress ?? '')
      sessionStorage.setItem('organizationName', response.organizationName)

      const session = response.session
      deviceSessionId.value = session?.id ?? null
      linkExpirySeconds.value = session?.linkExpirySeconds ?? null
      cumulativeTotal.value = session ? roundMoney(session.cumulativeTotal) : 0
      sessionOrders.value = session?.orders ?? []
      // Restored fresh from THIS join only — never from storage (see comment above).
      pendingDeal.value = session?.pendingDeal ?? null

      if (deviceSessionId.value) sessionStorage.setItem('deviceSessionId', deviceSessionId.value)
      else sessionStorage.removeItem('deviceSessionId')

      if (linkExpirySeconds.value !== null) {
        sessionStorage.setItem('linkExpirySeconds', String(linkExpirySeconds.value))
      } else {
        sessionStorage.removeItem('linkExpirySeconds')
      }

      sessionStorage.setItem('cumulativeTotal', String(cumulativeTotal.value))
      sessionStorage.setItem('sessionOrders', JSON.stringify(sessionOrders.value))

      return response
    }catch(error){
      console.error('Eroare la inițializarea sesiunii:', error)
      throw error
    }
  }

  // Adds roundTotal to cumulativeTotal (persisting immediately) and returns the
  // new running total. This is a local, optimistic bump only — the next join's
  // `session.cumulativeTotal` is the authoritative value and will overwrite it.
  // Also appends a minimal round entry to sessionOrders so a later cancellation
  // of THIS round (before the next join lands) can be reconciled locally — see
  // removeCancelledOrder. Join still overwrites sessionOrders authoritatively.
  function registerSubmittedRound(roundTotal: number, orderId: string): number {
    cumulativeTotal.value = roundMoney(cumulativeTotal.value + roundTotal)
    sessionStorage.setItem('cumulativeTotal', String(cumulativeTotal.value))
    sessionOrders.value = [
      ...sessionOrders.value,
      { id: orderId, status: 'submitted', totalAmount: roundTotal, items: [] },
    ]
    sessionStorage.setItem('sessionOrders', JSON.stringify(sessionOrders.value))
    return cumulativeTotal.value
  }

  // Kitchen-side cancellation of an in-flight round: the diner's running total
  // must self-correct instead of staying overstated until the next join
  // overwrites it. Case-insensitive id match (order ids are GUIDs, casing can
  // differ between what the socket echoes and what's stored); no-op if the id
  // isn't one of this visit's known rounds — a stale/unrelated cancel event
  // must never touch cumulativeTotal.
  function removeCancelledOrder(orderId: string): void {
    const needle = orderId.toLowerCase()
    const match = sessionOrders.value.find((o) => o.id.toLowerCase() === needle)
    if (!match) return

    cumulativeTotal.value = Math.max(0, roundMoney(cumulativeTotal.value - match.totalAmount))
    sessionOrders.value = sessionOrders.value.filter((o) => o.id.toLowerCase() !== needle)

    sessionStorage.setItem('cumulativeTotal', String(cumulativeTotal.value))
    sessionStorage.setItem('sessionOrders', JSON.stringify(sessionOrders.value))
  }

  function clearSession() {
    tableId.value = null
    orderId.value = null
    qrToken.value = null
    tableNumber.value = null
    restaurantId.value = null
    restaurantName.value = null
    restaurantAddress.value = null
    organizationName.value = null
    branding.value = null
    deviceSessionId.value = null
    linkExpirySeconds.value = null
    cumulativeTotal.value = 0
    sessionOrders.value = []
    pendingDeal.value = null
    const { applyBrand } = useTheme()
    applyBrand(null)
    sessionStorage.clear()
  }

  return {
    tableId,
    orderId,
    qrToken,
    tableNumber,
    restaurantId,
    restaurantName,
    restaurantAddress,
    organizationName,
    branding,
    deviceSessionId,
    linkExpirySeconds,
    cumulativeTotal,
    sessionOrders,
    pendingDeal,
    initializeSession,
    registerSubmittedRound,
    removeCancelledOrder,
    clearSession,
  }
})
