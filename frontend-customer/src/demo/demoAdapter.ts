// The demo session's "backend": pattern-matches the same endpoints
// api-client.ts would hit and serves fixtures with realistic latency.
// Reached ONLY via dynamic import from api-client (see Task 5) — a static
// import there would be circular (stores → api-client → adapter → stores).
// Anything unmatched throws naming the endpoint: a newly added API call must
// fail fast in the demo instead of silently half-working.
import {
  buildDemoJoinResponse,
  buildDemoHappyHour,
  buildDemoVibeyResponse,
  demoPartyMembers,
  DEMO_MENU,
} from '@/demo/fixtures'
import { startPostSubmitSequence, scheduleWaiterOnTheWay } from '@/demo/demoScripter'
import { useSessionStore } from '@/stores/useSessionStore'
import { useFlashDealStore } from '@/stores/useFlashDealStore'
import { roundMoney } from '@/utils/money'
import type { VibeyRecommendationRequest } from '@shared/types/api-requests'

function latency(): Promise<void> {
  return new Promise((r) => setTimeout(r, 150 + Math.random() * 250))
}

function parseBody<T>(options?: RequestInit): T {
  return JSON.parse((options?.body as string) || '{}') as T
}

export async function handleDemoRequest(
  endpoint: string,
  options?: RequestInit,
): Promise<unknown> {
  await latency()
  const method = (options?.method || 'GET').toUpperCase()
  const path = endpoint.split('?')[0] ?? ''

  if (method === 'POST' && /^\/api\/tables\/[^/]+\/join$/.test(path)) {
    return buildDemoJoinResponse()
  }
  if (method === 'GET' && /^\/api\/tables\/[^/]+\/party$/.test(path)) {
    return { partyMembers: demoPartyMembers() }
  }
  if (method === 'GET' && /^\/api\/restaurants\/[^/]+\/menu$/.test(path)) {
    return DEMO_MENU
  }
  if (method === 'GET' && /^\/api\/restaurants\/[^/]+\/happy-hour$/.test(path)) {
    return buildDemoHappyHour()
  }
  if (method === 'POST' && /^\/api\/restaurants\/[^/]+\/recommendations$/.test(path)) {
    return buildDemoVibeyResponse(parseBody<VibeyRecommendationRequest>(options))
  }
  if (method === 'PATCH' && /^\/api\/restaurants\/[^/]+\/orders\/[^/]+\/items$/.test(path)) {
    return {}
  }
  const submit = path.match(/^\/api\/restaurants\/[^/]+\/orders\/([^/]+)\/submit$/)
  const orderId = submit?.[1]
  if (method === 'POST' && orderId) {
    // The "kitchen" takes over: status progression + the post-order flash deal.
    startPostSubmitSequence(orderId)
    return {}
  }
  const claim = path.match(/^\/api\/restaurants\/[^/]+\/orders\/([^/]+)\/claim-deal\/[^/]+$/)
  const claimOrderId = claim?.[1]
  if (method === 'POST' && claimOrderId) {
    // Mirror the server: the claimed dessert is appended to the sent order at
    // the deal price, so the visit's running total (and the sent screen, via
    // OrderingView's cumulativeTotal watcher) updates.
    const flashDeal = useFlashDealStore()
    const deal = flashDeal.activeDeal
    if (deal) {
      const price = roundMoney(deal.discountedPrice * Math.max(1, flashDeal.activeDealQuantity))
      useSessionStore().registerSubmittedRound(price, claimOrderId)
    }
    return {}
  }

  throw new Error(
    `[demo] Unhandled endpoint in demo session: ${method} ${endpoint} — add it to demoAdapter.ts`,
  )
}

export async function handleDemoCallWaiter(): Promise<{ ok: true; cooldownEndsAt: string }> {
  await latency()
  scheduleWaiterOnTheWay()
  return { ok: true, cooldownEndsAt: new Date(Date.now() + 120_000).toISOString() }
}
