import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import {
  startPostSubmitSequence,
  scheduleWaiterOnTheWay,
  cancelDemoTimers,
} from '@/demo/demoScripter'
import { useOrderStatusStore } from '@/stores/useOrderStatusStore'
import { useFlashDealStore } from '@/stores/useFlashDealStore'
import { useHelpCallStore } from '@/stores/useHelpCallStore'

describe('demo scripter', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    sessionStorage.clear()
    setActivePinia(createPinia())
  })
  afterEach(() => {
    cancelDemoTimers()
    vi.useRealTimers()
  })

  it('plays out preparing → ready on this order', () => {
    const orderStatus = useOrderStatusStore()
    startPostSubmitSequence('order-1')
    expect(orderStatus.status).toBeNull()
    vi.advanceTimersByTime(15_000)
    expect(orderStatus.status).toBe('preparing')
    expect(orderStatus.orderId).toBe('order-1')
    vi.advanceTimersByTime(30_000)
    expect(orderStatus.status).toBe('ready')
  })

  it('injects the flash deal for this order after ~8s', () => {
    const flashDeal = useFlashDealStore()
    startPostSubmitSequence('order-2')
    expect(flashDeal.activeDeal).toBeNull()
    vi.advanceTimersByTime(8_000)
    expect(flashDeal.activeDeal).not.toBeNull()
    expect(flashDeal.activeDealOrderId).toBe('order-2')
    expect(flashDeal.activeDeal!.discountedPrice).toBeLessThan(flashDeal.activeDeal!.menuItem.price)
  })

  it('a new submit sequence cancels the previous one', () => {
    const orderStatus = useOrderStatusStore()
    startPostSubmitSequence('order-a')
    vi.advanceTimersByTime(5_000)
    startPostSubmitSequence('order-b')
    vi.advanceTimersByTime(15_000)
    expect(orderStatus.orderId).toBe('order-b')
  })

  it('waiter-on-the-way clears the help-call cooldown after ~6s', () => {
    const helpCall = useHelpCallStore()
    helpCall.applyCooldown(Date.now() + 120_000)
    scheduleWaiterOnTheWay()
    expect(helpCall.isCoolingDown).toBe(true)
    vi.advanceTimersByTime(6_000)
    expect(helpCall.cooldownEndsAt).toBeNull()
  })

  it('cancelDemoTimers stops everything pending', () => {
    const orderStatus = useOrderStatusStore()
    startPostSubmitSequence('order-3')
    cancelDemoTimers()
    vi.advanceTimersByTime(60_000)
    expect(orderStatus.status).toBeNull()
  })
})
