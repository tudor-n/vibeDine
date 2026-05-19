import { ref, onUnmounted, watch } from 'vue'
import { useSessionStore } from '@/stores/useSessionStore'
import { useFlashDealStore } from '@/stores/useFlashDealStore'
import { useOrderStatusStore } from '@/stores/useOrderStatusStore'
import type { FlashDeal, OrderStatus } from '@shared/types/models'


const rawWsUrl = import.meta.env.VITE_WS_URL || 'ws://localhost:5005'

const BASE_WS_URL = rawWsUrl.endsWith('/') ? rawWsUrl.slice(0, -1) : rawWsUrl

export function useWebSocket() {
  const sessionStore = useSessionStore()
  const flashDealStore = useFlashDealStore()
  const orderStatusStore = useOrderStatusStore()
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
                reason: 'Ofertă specială pentru comanda ta!',
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
