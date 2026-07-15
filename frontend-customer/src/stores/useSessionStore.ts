import { defineStore } from 'pinia'
import { ref } from 'vue'
import { useGuestStore } from '@/stores/useGuestStore'
import { useTheme } from '@/composables/useTheme'
import type { JoinTableResponse } from '@/services/api-client'
import type { RestaurantBranding } from '@shared/types/models'

export const useSessionStore = defineStore('session' , () => {
  const tableId = ref<string | null>(sessionStorage.getItem('tableId'))
  const orderId = ref<string | null>(sessionStorage.getItem('orderId'))
  const qrToken = ref<string | null>(sessionStorage.getItem('qrToken'))
  const tableNumber = ref<string | null>(sessionStorage.getItem('tableNumber'))
  const restaurantId = ref<string | null>(sessionStorage.getItem('restaurantId'))
  const restaurantName = ref<string | null>(sessionStorage.getItem('restaurantName'))
  const restaurantAddress = ref<string | null>(sessionStorage.getItem('restaurantAddress'))
  const organizationName = ref<string | null>(sessionStorage.getItem('organizationName'))
  // Not persisted to sessionStorage — re-loads fresh on every join.
  const branding = ref<RestaurantBranding | null>(null)

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
      if (b?.enabled && b.primary && b.secondary && b.background) {
        applyBrand({ primary: b.primary, secondary: b.secondary, background: b.background })
      } else {
        applyBrand(null)
      }

      sessionStorage.setItem('tableId', response.tableId)
      sessionStorage.setItem('orderId', response.activeOrder.id)
      sessionStorage.setItem('qrToken', token)
      sessionStorage.setItem('tableNumber', response.tableNumber)
      sessionStorage.setItem('restaurantId', response.restaurantId)
      sessionStorage.setItem('restaurantName', response.restaurantName)
      sessionStorage.setItem('restaurantAddress', response.restaurantAddress ?? '')
      sessionStorage.setItem('organizationName', response.organizationName)

      return response
    }catch(error){
      console.error('Eroare la inițializarea sesiunii:', error)
      throw error
    }
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
    const { applyBrand } = useTheme()
    applyBrand(null)
    sessionStorage.clear()
  }

  return { tableId, orderId, qrToken, tableNumber, restaurantId, restaurantName, restaurantAddress, organizationName, branding, initializeSession, clearSession}
})
