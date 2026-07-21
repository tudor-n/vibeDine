import { defineStore } from 'pinia'
import { ref } from 'vue'
import type { FlashDeal } from '@shared/types/models'

// The two places a flash-deal payload arrives from — the live `FlashDealAvailable`
// WebSocket push and the join response's `pendingDeal` (PendingFlashDealPayload
// in shared/types/models.ts) — share every field except `imageUrl` (join-only;
// the WS push doesn't carry a product image). `imageUrl` is therefore the only
// optional field here so one mapping covers both callers.
export interface FlashDealPayloadLike {
  dealId: string
  orderId: string
  productId: string
  productName: string
  imageUrl?: string | null
  originalPrice: number
  discountPrice: number
  discountPercent: number
  expiresAt: string
  quantity?: number
}

// Single source of truth for payload -> FlashDeal conversion, used by the
// WebSocket handler (useWebSocket.ts) and by the join-restore path
// (OrderingView.onMounted) so a pending deal survives a re-scan identically
// to a freshly pushed one.
export function buildFlashDealFromPayload(raw: FlashDealPayloadLike): FlashDeal {
  // Prefer the server-computed percent (now sent by both the WS push and the
  // join payload). If it's ever missing/zero, derive it from the two prices we
  // always have rather than inventing a number — guarded against a zero/invalid
  // originalPrice, which would otherwise divide by zero.
  const derivedPercent =
    raw.originalPrice > 0
      ? Math.round((1 - raw.discountPrice / raw.originalPrice) * 100)
      : 0
  const discountPercent = raw.discountPercent || derivedPercent

  return {
    id: raw.dealId,
    menuItemId: raw.productId,
    menuItem: {
      id: raw.productId,
      name: raw.productName,
      price: raw.originalPrice,
      imageUrl: raw.imageUrl ?? null,
    },
    discountPercent,
    discountedPrice: raw.discountPrice,
    trigger: 'post_order',
    countdownSeconds: 120,
    expiresAt: raw.expiresAt,
    reason: '',
    isActive: true,
    createdAt: new Date().toISOString(),
  }
}

export const useFlashDealStore = defineStore('flashDeal', () => {
  const activeDeal = ref<FlashDeal | null>(null)
  const activeDealOrderId = ref<string | null>(null)
  // How many discounted portions the offer is for: one per guest whose cart is
  // on this bill. 1 for a solo / pay-separately order, N for a bill that merged
  // N guests. Kept alongside the deal (like orderId) rather than on the shared
  // FlashDeal type so it stays a frontend-only display concern.
  const activeDealQuantity = ref(1)

  function setActiveDeal(deal: FlashDeal, orderId: string, quantity = 1) {
    activeDeal.value = deal
    activeDealOrderId.value = orderId
    activeDealQuantity.value = quantity > 0 ? quantity : 1
  }

  function clearDeal() {
    activeDeal.value = null
    activeDealOrderId.value = null
    activeDealQuantity.value = 1
  }

  return { activeDeal, activeDealOrderId, activeDealQuantity, setActiveDeal, clearDeal }
})
