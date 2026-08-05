// @vitest-environment jsdom
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it } from 'vitest'

/**
 * Behaviour contract for `stores/useWaiterCartStore.ts` — the per-table draft
 * cart a waiter builds before submitting an order.
 *
 * TODO(W2 landing): the cart store is owned by the waiter-mode frontend stream
 * and has not landed yet, so this suite is `describe.skip` and the module path
 * is assembled at runtime — a statically analysable import of a missing file
 * fails the whole file at collection. Drop the four `.skip`s at final
 * integration; the runtime import resolves the `@` alias as-is.
 * See PARALLEL-HANDOFF.md § W3.
 */
const CART_STORE_MODULE = ['@/stores', 'useWaiterCartStore'].join('/')

const loadCartStore = async () => {
  const mod = (await import(/* @vite-ignore */ CART_STORE_MODULE)) as unknown as {
    useWaiterCartStore: () => Record<string, never>
  }
  return mod.useWaiterCartStore() as unknown as {
    items: { productId: string; quantity: number; notes: string | null; unitPrice: number }[]
    itemCount: number
    total: number
    add: (p: { id: string; name: string; price: number }, qty?: number) => void
    setQuantity: (productId: string, qty: number) => void
    setNotes: (productId: string, notes: string) => void
    remove: (productId: string) => void
    clear: () => void
    setTable: (tableId: string) => void
  }
}

// Caps are frozen in the contract appendix and enforced on both sides.
const MAX_ORDER_ITEMS = 100
const MAX_LINE_QUANTITY = 99
const NOTES_MAX_LENGTH = 200

const product = (id: string, price: number) => ({ id, name: `Produs ${id}`, price })

beforeEach(() => {
  setActivePinia(createPinia())
  sessionStorage.clear()
})

describe.skip('useWaiterCartStore — totals', () => {
  it('sums quantity × unit price', async () => {
    const cart = await loadCartStore()
    cart.add(product('p-1', 24.5), 2)
    cart.add(product('p-2', 12.25), 1)

    expect(cart.itemCount).toBe(3)
    expect(cart.total).toBeCloseTo(61.25, 2)
  })

  it('adding the same product merges into one line', async () => {
    const cart = await loadCartStore()
    cart.add(product('p-1', 10), 1)
    cart.add(product('p-1', 10), 2)

    expect(cart.items).toHaveLength(1)
    expect(cart.items[0].quantity).toBe(3)
  })

  it('setQuantity(0) removes the line', async () => {
    const cart = await loadCartStore()
    cart.add(product('p-1', 10), 2)
    cart.setQuantity('p-1', 0)

    expect(cart.items).toHaveLength(0)
    expect(cart.total).toBe(0)
  })
})

describe.skip('useWaiterCartStore — caps', () => {
  it(`clamps a line to ${MAX_LINE_QUANTITY}`, async () => {
    const cart = await loadCartStore()
    cart.add(product('p-1', 10), 1)
    cart.setQuantity('p-1', MAX_LINE_QUANTITY + 5)

    expect(cart.items[0].quantity).toBe(MAX_LINE_QUANTITY)
  })

  it(`refuses more than ${MAX_ORDER_ITEMS} distinct lines`, async () => {
    const cart = await loadCartStore()
    for (let i = 0; i < MAX_ORDER_ITEMS + 10; i++) cart.add(product(`p-${i}`, 1), 1)

    expect(cart.items).toHaveLength(MAX_ORDER_ITEMS)
  })

  it(`truncates notes to ${NOTES_MAX_LENGTH} chars`, async () => {
    const cart = await loadCartStore()
    cart.add(product('p-1', 10), 1)
    cart.setNotes('p-1', 'x'.repeat(NOTES_MAX_LENGTH + 50))

    expect(cart.items[0].notes?.length).toBe(NOTES_MAX_LENGTH)
  })
})

describe.skip('useWaiterCartStore — persistence', () => {
  it('survives a reload via sessionStorage', async () => {
    const cart = await loadCartStore()
    cart.setTable('t-1')
    cart.add(product('p-1', 10), 2)

    // A fresh Pinia = a fresh page; the store rehydrates its own draft.
    setActivePinia(createPinia())
    const restored = await loadCartStore()
    restored.setTable('t-1')

    expect(restored.items).toHaveLength(1)
    expect(restored.items[0].quantity).toBe(2)
  })

  it('clear() empties the cart and its persisted copy', async () => {
    const cart = await loadCartStore()
    cart.setTable('t-1')
    cart.add(product('p-1', 10), 2)
    cart.clear()

    expect(cart.items).toHaveLength(0)

    setActivePinia(createPinia())
    const restored = await loadCartStore()
    restored.setTable('t-1')
    expect(restored.items).toHaveLength(0)
  })

  it('a submitted order leaves no draft behind', async () => {
    const cart = await loadCartStore()
    cart.setTable('t-1')
    cart.add(product('p-1', 10), 1)
    // The order view calls clear() after a successful submit.
    cart.clear()

    expect(cart.total).toBe(0)
    expect(cart.itemCount).toBe(0)
  })
})
