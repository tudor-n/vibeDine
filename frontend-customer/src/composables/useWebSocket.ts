import { ref, onMounted, onUnmounted } from 'vue'
import { useSessionStore } from '@/stores/useSessionStore'
import { useFlashDealStore } from '@/stores/useFlashDealStore'
import type { FlashDeal } from '@shared/types/models'


const rawWsUrl = import.meta.env.VITE_WS_URL || 'ws://localhost:4000'

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


      socket?.send(JSON.stringify({ protocol: 'json', version: 1 }) + '\x1e')
    }

    socket.onmessage = (event) => {
      try {
        const messages = event.data.split('\x1e')

        for (const msg of messages) {
          if (!msg || msg === '{}') continue

          const data = JSON.parse(msg)

          if (data.type === 1 && data.target) {
            if (data.target === 'FlashDealAvailable') {
              const newDeal = data.arguments[0] as FlashDeal
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
