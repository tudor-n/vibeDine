import { ref, onUnmounted, watch } from 'vue'
import { useSessionStore } from '@/stores/useSessionStore'
import { useFlashDealStore } from '@/stores/useFlashDealStore'
import { useOrderStatusStore } from '@/stores/useOrderStatusStore'
import { useHelpCallStore } from '@/stores/useHelpCallStore'
import { useGuestStore } from '@/stores/useGuestStore'
import { useCartStore } from '@/stores/useCartStore'
import type { FlashDeal, OrderStatus } from '@shared/types/models'


const rawWsUrl = import.meta.env.VITE_WS_URL || 'ws://localhost:5005'

const BASE_WS_URL = rawWsUrl.endsWith('/') ? rawWsUrl.slice(0, -1) : rawWsUrl

export function useWebSocket() {
  const sessionStore = useSessionStore()
  const flashDealStore = useFlashDealStore()
  const orderStatusStore = useOrderStatusStore()
  const helpCallStore = useHelpCallStore()
  const guestStore = useGuestStore()
  const cartStore = useCartStore()
  const isConnected = ref(false)
  let socket: WebSocket | null = null

  function connect() {
    if (socket) return  // already connected, don't check tableId here

    const socketUrl = `${BASE_WS_URL}/hubs/customer?token=${sessionStore.qrToken}`

    socket = new WebSocket(socketUrl)

    socket.onopen = () => {
      console.log('Conexiune WebSocket stabilită!')
      isConnected.value = true

      // Step 1: send handshake
      socket?.send(JSON.stringify({ protocol: 'json', version: 1 }) + '\x1e')
    }

    socket.onmessage = (event) => {
      try {
        const messages = event.data.split('\x1e')

        for (const msg of messages) {
          if (!msg) continue

          // SignalR handshake ack is an empty object `{}`. Join the table group
          // immediately on the ack — waiting for a type=6 ping created a ~15s
          // window where flash-deal events could be missed.
          if (msg === '{}') {
            if (sessionStore.tableId && sessionStore.restaurantId) {
              socket?.send(
                JSON.stringify({
                  type: 1,
                  target: 'JoinTableGroup',
                  arguments: [sessionStore.restaurantId, sessionStore.tableId]
                }) + '\x1e'
              )
            }
            continue
          }

          const data = JSON.parse(msg)

          if (data.type === 1 && data.target) {
            if (data.target === 'FlashDealAvailable') {
              const raw = data.arguments[0]

              const newDeal: FlashDeal = {
                id: raw.dealId,
                menuItemId: raw.productId,
                menuItem: {
                  id: raw.productId,
                  name: raw.productName,
                  price: raw.originalPrice,
                  imageUrl: null
                },
                discountPercent: raw.discountPercent || 20,
                discountedPrice: raw.discountPrice,
                trigger: 'post_order',
                countdownSeconds: 120,
                expiresAt: raw.expiresAt,
                reason: '',
                isActive: true,
                createdAt: new Date().toISOString()
              }

              // Store the orderId the deal belongs to separately
              flashDealStore.setActiveDeal(newDeal, raw.orderId)
            }

            if (data.target === 'FlashDealExpired') {
              flashDealStore.clearDeal()
            }

            if (data.target === 'OrderStatusUpdated') {
              const payload = data.arguments[0] as { orderId: string; status: OrderStatus }
              if (payload?.orderId && payload?.status) {
                orderStatusStore.setStatus(payload.orderId, payload.status)
              }
            }

            if (data.target === 'WaiterOnTheWay') {
              helpCallStore.clearCooldown()
            }

            // Someone new scanned in at this table — refresh the party panel.
            if (data.target === 'PartyMemberJoined') {
              guestStore.refreshParty(sessionStore.qrToken)
            }

            // A peer changed their cart — refresh so totals stay live and the
            // "Merge our bills" button appears the moment someone else has items.
            if (data.target === 'PartyMemberUpdated') {
              guestStore.refreshParty(sessionStore.qrToken)
            }

            // A guest consolidated everyone's drafts into one bill.
            if (data.target === 'PartyMerged') {
              const payload = data.arguments[0] as {
                targetOrderId: string
                mergedOrderIds: string[]
                initiatorGuestId: string
                initiatorDisplayName: string
              }
              guestStore.refreshParty(sessionStore.qrToken)
              const myOrderId = sessionStore.orderId
              // If MY draft was one of the merged ones, freeze my cart and flag
              // the merged state so the view switches to the "merged" screen.
              if (
                myOrderId &&
                Array.isArray(payload?.mergedOrderIds) &&
                payload.mergedOrderIds.includes(myOrderId) &&
                payload.initiatorGuestId !== guestStore.guestId
              ) {
                cartStore.abandonForMerge()
                guestStore.markMerged(payload.initiatorDisplayName)
              }
            }
          }
        }
      } catch (error) {
        console.error('Eroare la parsarea mesajului WebSocket SignalR:', error)
      }
    }

    socket.onclose = () => {
      console.log('Conexiune WebSocket închisă.')
      isConnected.value = false
      socket = null
      setTimeout(() => {
        if (sessionStore.tableId) connect()
      }, 5000)
    }

    socket.onerror = (error) => {
      console.error('Eroare WebSocket:', error)
    }
  }

  function disconnect() {
    if (socket) {
      socket.close()
    }
  }

  // Key fix: watch tableId instead of connecting blindly in onMounted
  watch(
    () => sessionStore.tableId,
    (tableId) => {
      if (tableId && !socket) {
        connect()
      }
    },
    { immediate: true }
  )

  onUnmounted(() => {
    disconnect()
  })

  return { isConnected }
}
