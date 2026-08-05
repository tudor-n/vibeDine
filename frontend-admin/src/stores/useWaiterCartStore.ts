import { defineStore } from 'pinia'
import { computed, ref, watch } from 'vue'
import type { UUID } from '@shared/types/models'
import { WAITER_CAPS } from '@/services/waiterApi'

/**
 * The waiter's in-progress round, kept per table so a waiter can start a cart on
 * table 4, get pulled to table 7, and come back to an untouched cart. Nothing is
 * sent to the backend until "Trimite la bucătărie" — the draft order is created,
 * filled and submitted in one chain — so the only durability that matters is
 * surviving an accidental reload, hence sessionStorage rather than localStorage.
 */

const STORAGE_KEY = 'sr_admin_waiter_cart'

export interface WaiterCartLine {
  productId: UUID
  name: string
  price: number
  quantity: number
  /** Kitchen remark for this line ("fără ceapă"), ≤ NotesMaxLength. */
  notes: string
}

/** Why an add/increment was refused — the view turns these into Romanian toasts. */
export type CartLimit = 'ok' | 'max_items' | 'max_qty'

type CartMap = Record<string, WaiterCartLine[]>

function round2(value: number): number {
  return Math.round(value * 100) / 100
}

function restore(): CartMap {
  try {
    const raw = sessionStorage.getItem(STORAGE_KEY)
    const parsed = raw ? (JSON.parse(raw) as CartMap) : {}
    return parsed && typeof parsed === 'object' ? parsed : {}
  } catch {
    return {}
  }
}

export const useWaiterCartStore = defineStore('waiterCart', () => {
  const carts = ref<CartMap>(restore())
  /** The table the order screen is currently working on. */
  const activeTableId = ref<string | null>(null)

  watch(
    carts,
    (value) => {
      try {
        sessionStorage.setItem(STORAGE_KEY, JSON.stringify(value))
      } catch {
        // A full/blocked sessionStorage must never break order taking.
      }
    },
    { deep: true },
  )

  const lines = computed<WaiterCartLine[]>(() =>
    activeTableId.value ? (carts.value[activeTableId.value] ?? []) : [],
  )
  const count = computed(() => lines.value.reduce((sum, line) => sum + line.quantity, 0))
  const total = computed(() =>
    round2(lines.value.reduce((sum, line) => sum + line.price * line.quantity, 0)),
  )
  const isEmpty = computed(() => lines.value.length === 0)

  function linesFor(tableId: string): WaiterCartLine[] {
    return carts.value[tableId] ?? []
  }

  /** Point the cart at a table; call it from the order screen's `load()`. */
  function open(tableId: string) {
    activeTableId.value = tableId
    if (!carts.value[tableId]) carts.value = { ...carts.value, [tableId]: [] }
  }

  function current(): WaiterCartLine[] | null {
    const tableId = activeTableId.value
    if (!tableId) return null
    if (!carts.value[tableId]) carts.value[tableId] = []
    return carts.value[tableId]
  }

  function find(productId: string): WaiterCartLine | undefined {
    return current()?.find((line) => line.productId === productId)
  }

  function quantityOf(productId: string): number {
    return find(productId)?.quantity ?? 0
  }

  function add(product: { id: UUID; name: string; price: number }): CartLimit {
    const cart = current()
    if (!cart) return 'ok'
    const existing = cart.find((line) => line.productId === product.id)
    if (existing) return increment(product.id)
    if (cart.length >= WAITER_CAPS.MaxOrderItems) return 'max_items'
    cart.push({
      productId: product.id,
      name: product.name,
      price: round2(product.price),
      quantity: 1,
      notes: '',
    })
    return 'ok'
  }

  function increment(productId: string): CartLimit {
    const line = find(productId)
    if (!line) return 'ok'
    if (line.quantity >= WAITER_CAPS.MaxLineQuantity) return 'max_qty'
    line.quantity += 1
    return 'ok'
  }

  /** Decrementing the last unit removes the line (and with it its notes). */
  function decrement(productId: string) {
    const cart = current()
    if (!cart) return
    const index = cart.findIndex((line) => line.productId === productId)
    if (index === -1) return
    if (cart[index].quantity <= 1) cart.splice(index, 1)
    else cart[index].quantity -= 1
  }

  function remove(productId: string) {
    const cart = current()
    if (!cart) return
    const index = cart.findIndex((line) => line.productId === productId)
    if (index !== -1) cart.splice(index, 1)
  }

  function setNotes(productId: string, notes: string) {
    const line = find(productId)
    if (line) line.notes = notes.slice(0, WAITER_CAPS.NotesMaxLength)
  }

  /** Empty one table's cart — defaults to the active one. */
  function clear(tableId?: string) {
    const key = tableId ?? activeTableId.value
    if (!key) return
    const next = { ...carts.value }
    delete next[key]
    carts.value = next
  }

  return {
    // state
    carts,
    activeTableId,
    // getters
    lines,
    count,
    total,
    isEmpty,
    linesFor,
    quantityOf,
    // actions
    open,
    add,
    increment,
    decrement,
    remove,
    setNotes,
    clear,
  }
})
