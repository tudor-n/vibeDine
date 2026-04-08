import { ref, onMounted, onUnmounted } from 'vue'
import { useSessionStore } from '@/stores/useSessionStore'
import { useFlashDealStore } from '@/stores/useFlashDealStore'
import type { FlashDeal } from '@shared/types/models'


const rawWsUrl = import.meta.env.VITE_WS_URL || 'ws://localhost:5005'

const BASE_WS_URL = rawWsUrl.endsWith('/') ? rawWsUrl.slice(0, -1) : rawWsUrl

export function useWebSocket() {
  const sessionStore = useSessionStore()
  const flashDealStore = useFlashDealStore()
  const isConnected = ref(false)
  let socket: WebSocket | null = null

  function connect() {
    if (!sessionStore.tableId || socket) return


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
          if (!msg || msg === '{}') continue

          const data = JSON.parse(msg)

          // Step 2: after handshake ack (type 6), join the table group
          if (data.type === 6 && sessionStore.tableId) {
            socket?.send(
              JSON.stringify({
                type: 1,
                target: 'JoinTableGroup',
                arguments: [sessionStore.tableId]
              }) + '\x1e'
            )
          }

          if (data.type === 1 && data.target) {
            if (data.target === 'FlashDealAvailable') {
              const raw = data.arguments[0]
              // Step 3: map backend payload to FlashDeal shape
              const newDeal: FlashDeal = {
                id: raw.dealId,
                menuItemId: raw.dealId,
                menuItem: {
                  id: raw.dealId,
                  name: raw.productName,
                  price: raw.discountPrice,
                  imageUrl: null
                },
                discountPercent: 0,
                discountedPrice: raw.discountPrice,
                trigger: 'post_order',
                countdownSeconds: 120,
                expiresAt: raw.expiresAt,
                reason: 'Ofertă specială!',
                isActive: true,
                createdAt: new Date().toISOString()
              }
              flashDealStore.setActiveDeal(newDeal)
            }

            if (data.target === 'FlashDealExpired') {
              flashDealStore.clearDeal()
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
      setTimeout(connect, 5000)
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

  onMounted(() => {
    connect()
  })

  onUnmounted(() => {
    disconnect()
  })

  return { isConnected }
}
