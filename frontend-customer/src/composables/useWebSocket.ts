import { ref, onUnmounted, watch } from 'vue'
import { useSessionStore } from '@/stores/useSessionStore'
import { useFlashDealStore, buildFlashDealFromPayload } from '@/stores/useFlashDealStore'
import { useOrderStatusStore } from '@/stores/useOrderStatusStore'
import { useHelpCallStore } from '@/stores/useHelpCallStore'
import { useGuestStore } from '@/stores/useGuestStore'
import { useCartStore } from '@/stores/useCartStore'
import { useMenu } from '@/composables/useMenu'
import type { OrderStatus } from '@shared/types/models'
import { isDemoToken } from '@/demo/config'


const rawWsUrl = import.meta.env.VITE_WS_URL || 'ws://localhost:5005'

const BASE_WS_URL = rawWsUrl.endsWith('/') ? rawWsUrl.slice(0, -1) : rawWsUrl

export function useWebSocket() {
  const sessionStore = useSessionStore()
  const flashDealStore = useFlashDealStore()
  const orderStatusStore = useOrderStatusStore()
  const helpCallStore = useHelpCallStore()
  const guestStore = useGuestStore()
  const cartStore = useCartStore()
  const { setAvailability } = useMenu()
  const isConnected = ref(false)
  let socket: WebSocket | null = null
  // Keep-alive ping timer (CUST-03) and reconnect timer / intentional-close flag
  // (CUST-04) — module-scoped to this composable instance so disconnect() and
  // onUnmounted can fully tear the connection down.
  let pingInterval: ReturnType<typeof setInterval> | null = null
  let reconnectTimer: ReturnType<typeof setTimeout> | null = null
  let intentionalClose = false

  function stopPing() {
    if (pingInterval) {
      clearInterval(pingInterval)
      pingInterval = null
    }
  }

  function connect() {
    // Demo session: there is no backend and no hub — the demo scripter replays
    // the pushes instead (src/demo/demoScripter.ts). Bail before creating a
    // socket so there's also no 5s reconnect loop hammering a dead URL.
    if (isDemoToken(sessionStore.qrToken)) return
    if (socket) return  // already connected, don't check tableId here

    // A fresh connect means we want to stay connected; reconnect on unexpected close.
    intentionalClose = false

    const socketUrl = `${BASE_WS_URL}/hubs/customer?token=${sessionStore.qrToken}`

    socket = new WebSocket(socketUrl)

    socket.onopen = () => {
      console.log('Conexiune WebSocket stabilită!')
      isConnected.value = true

      // Step 1: send handshake
      socket?.send(JSON.stringify({ protocol: 'json', version: 1 }) + '\x1e')

      // Keep-alive: SignalR expects periodic client pings ({"type":6}). Without
      // them the server's default 30s ClientTimeoutInterval drops the socket
      // roughly every 30s, and any event fired during the reconnect gap is lost
      // (SignalR does not replay group messages). Ping every 15s while open.
      stopPing()
      pingInterval = setInterval(() => {
        if (socket?.readyState === WebSocket.OPEN) {
          socket.send(JSON.stringify({ type: 6 }) + '\x1e')
        }
      }, 15000)
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
            // Also join the restaurant-wide menu group for live stock availability updates.
            if (sessionStore.restaurantId) {
              socket?.send(
                JSON.stringify({
                  type: 1,
                  target: 'JoinRestaurantMenuGroup',
                  arguments: [sessionStore.restaurantId]
                }) + '\x1e'
              )
            }
            continue
          }

          const data = JSON.parse(msg)

          if (data.type === 1 && data.target) {
            if (data.target === 'FlashDealAvailable') {
              const raw = data.arguments[0]

              // Every phone at the table shares this SignalR group, but a deal
              // belongs to exactly one order — the one that was just submitted.
              // Show it only on the phone that owns that order, so a peer's deal
              // never pops up on everyone's screen. Separate bills => separate
              // deals; a merged bill's deal shows only on the payer's phone
              // (its orderId is the merge target's id).
              const myOrderId = sessionStore.orderId
              const isMyDeal =
                !!myOrderId &&
                !!raw?.orderId &&
                String(raw.orderId).toLowerCase() === myOrderId.toLowerCase()

              if (isMyDeal) {
                const newDeal = buildFlashDealFromPayload(raw)

                // Store the orderId the deal belongs to + how many portions it's
                // for (one per guest on this bill; 1 unless the bill was merged).
                flashDealStore.setActiveDeal(newDeal, raw.orderId, raw.quantity || 1)
              }
            }

            if (data.target === 'FlashDealExpired') {
              // Clear only if it's the deal this phone is actually showing: two
              // guests at one table can each hold their own pending deal, and
              // this event hits the whole group — don't cancel a peer's offer.
              const payload = data.arguments[0] as { dealId?: string }
              if (payload?.dealId && flashDealStore.activeDeal?.id === payload.dealId) {
                flashDealStore.clearDeal()
              }
            }

            if (data.target === 'OrderStatusUpdated') {
              const payload = data.arguments[0] as { orderId: string; status: OrderStatus }
              // Broadcast to the whole table group, but each phone owns a
              // different order in a pay-separately party. Apply the update only
              // to THIS phone's order so a peer's status (or a cancel) can't
              // overwrite our timeline — mirror the FlashDealAvailable filter.
              const myOrderId = sessionStore.orderId
              const isMyOrder =
                !!myOrderId &&
                !!payload?.orderId &&
                String(payload.orderId).toLowerCase() === myOrderId.toLowerCase()
              if (isMyOrder && payload?.status) {
                orderStatusStore.setStatus(payload.orderId, payload.status)
              }
              // A cancellation must self-correct the visit's running total no
              // matter which round it hit — a diner can be looking at the
              // "sent" screen for round 2 while the kitchen cancels round 1.
              // Reconcile against every known session order, not just this
              // phone's current draft.
              if (payload?.status === 'cancelled' && payload?.orderId) {
                sessionStore.removeCancelledOrder(payload.orderId)
              }
            }

            // Live stock availability: an item went out of / back into stock — blur or un-blur it.
            if (data.target === 'MenuItemAvailabilityChanged') {
              const payload = data.arguments[0] as {
                items?: Array<{ productId: string; isAvailable: boolean; outOfStock: boolean }>
              }
              if (Array.isArray(payload?.items)) {
                for (const it of payload.items) {
                  if (it?.productId) {
                    setAvailability(it.productId, Boolean(it.isAvailable), Boolean(it.outOfStock))
                  }
                }
              }
            }

            if (data.target === 'WaiterOnTheWay') {
              helpCallStore.clearCooldown()
            }

            // Someone new scanned in at this table — refresh the party panel and
            // pop a short toast on the phones already seated (never for our own
            // join; the store also suppresses it while our join is in flight).
            if (data.target === 'PartyMemberJoined') {
              guestStore.refreshParty(sessionStore.qrToken)
              const payload = data.arguments[0] as { guestId?: string; displayName?: string }
              if (payload?.guestId && payload.displayName && payload.guestId !== guestStore.guestId) {
                guestStore.announceJoin(payload.displayName)
              }
            }

            // A guest's order went to the kitchen (or their seat was reclaimed) —
            // drop them from the party panel.
            if (data.target === 'PartyMemberLeft') {
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
      stopPing()
      // Don't reconnect after an intentional disconnect() (teardown / unmount) —
      // otherwise close() would just trigger another connect() loop.
      if (intentionalClose) return
      if (reconnectTimer) clearTimeout(reconnectTimer)
      reconnectTimer = setTimeout(() => {
        reconnectTimer = null
        if (sessionStore.tableId) connect()
      }, 5000)
    }

    socket.onerror = (error) => {
      console.error('Eroare WebSocket:', error)
    }
  }

  function disconnect() {
    // Mark this as an intentional close so onclose does not schedule a reconnect,
    // and cancel any pending reconnect / ping timers so nothing survives teardown.
    intentionalClose = true
    if (reconnectTimer) {
      clearTimeout(reconnectTimer)
      reconnectTimer = null
    }
    stopPing()
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

  // Exposed so a link-expiry timeout can tear down the socket explicitly (an
  // abandoned open tab must stop talking to the server so the backend's wipe
  // of the device session can proceed) without waiting for unmount.
  return { isConnected, disconnect }
}
