import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import type { UUID } from '@shared/types/models'
import { WAITER_CAPS } from '@/services/waiterApi'

/**
 * The waiter's in-progress round, kept per table so a waiter can start a cart on
 * table 4, get pulled to table 7, and come back to an untouched cart. Nothing is
 * sent to the backend until "Trimite la bucătărie" — the draft order is created,
 * filled and submitted in one chain — so the only durability that matters is
 * surviving an accidental reload, hence sessionStorage rather than localStorage.
 *
 * Writes are persisted synchronously at the end of every mutation rather than
 * from a watcher: a reload can land between any two ticks, and a watcher's
 * microtask flush is one tick too late to guarantee the draft is on disk.
 */

const STORAGE_KEY = 'sr_admin_waiter_cart'
/** Bucket used before a table is chosen, so a cart is never silently dropped. */
const NO_TABLE = ''

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
  const activeTableId = ref<string>(NO_TABLE)

  function persist() {
    try {
      sessionStorage.setItem(STORAGE_KEY, JSON.stringify(carts.value))
    } catch {
      // A full/blocked sessionStorage must never break order taking.
    }
  }

  /** The active table's lines, creating the bucket on first touch. */
  function current(): WaiterCartLine[] {
    const key = activeTableId.value
    if (!carts.value[key]) carts.value[key] = []
    return carts.value[key]
  }

  const lines = computed<WaiterCartLine[]>(() => carts.value[activeTableId.value] ?? [])
  /** Alias of `lines` — the name the contract test suite reads. */
  const items = lines
  const count = computed(() => lines.value.reduce((sum, line) => sum + line.quantity, 0))
  const itemCount = count
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
    if (!carts.value[tableId]) carts.value[tableId] = []
  }
  /** Alias of `open`. */
  const setTable = open

  function find(productId: string): WaiterCartLine | undefined {
    return current().find((line) => line.productId === productId)
  }

  function quantityOf(productId: string): number {
    return find(productId)?.quantity ?? 0
  }

  /**
   * Add `qty` of a product, merging into an existing line. Returns why it was
   * clamped or refused, so the caller can say so in Romanian before the API does.
   */
  function add(product: { id: UUID; name: string; price: number }, qty = 1): CartLimit {
    const cart = current()
    const wanted = Math.max(1, Math.trunc(qty))
    const existing = cart.find((line) => line.productId === product.id)

    if (existing) {
      const next = Math.min(existing.quantity + wanted, WAITER_CAPS.MaxLineQuantity)
      const clamped = next < existing.quantity + wanted
      existing.quantity = next
      persist()
      return clamped ? 'max_qty' : 'ok'
    }

    if (cart.length >= WAITER_CAPS.MaxOrderItems) return 'max_items'
    cart.push({
      productId: product.id,
      name: product.name,
      price: round2(product.price),
      quantity: Math.min(wanted, WAITER_CAPS.MaxLineQuantity),
      notes: '',
    })
    persist()
    return 'ok'
  }

  function increment(productId: string): CartLimit {
    const line = find(productId)
    if (!line) return 'ok'
    if (line.quantity >= WAITER_CAPS.MaxLineQuantity) return 'max_qty'
    line.quantity += 1
    persist()
    return 'ok'
  }

  /** Decrementing the last unit removes the line (and with it its notes). */
  function decrement(productId: string) {
    const cart = current()
    const index = cart.findIndex((line) => line.productId === productId)
    if (index === -1) return
    if (cart[index].quantity <= 1) cart.splice(index, 1)
    else cart[index].quantity -= 1
    persist()
  }

  /** Absolute quantity: clamped to the line cap, and 0 removes the line. */
  function setQuantity(productId: string, qty: number) {
    const cart = current()
    const index = cart.findIndex((line) => line.productId === productId)
    if (index === -1) return
    const next = Math.min(Math.max(0, Math.trunc(qty)), WAITER_CAPS.MaxLineQuantity)
    if (next === 0) cart.splice(index, 1)
    else cart[index].quantity = next
    persist()
  }

  function remove(productId: string) {
    const cart = current()
    const index = cart.findIndex((line) => line.productId === productId)
    if (index === -1) return
    cart.splice(index, 1)
    persist()
  }

  function setNotes(productId: string, notes: string) {
    const line = find(productId)
    if (!line) return
    line.notes = notes.slice(0, WAITER_CAPS.NotesMaxLength)
    persist()
  }

  /** Empty one table's cart — defaults to the active one. */
  function clear(tableId?: string) {
    const key = tableId ?? activeTableId.value
    const next = { ...carts.value }
    delete next[key]
    carts.value = next
    persist()
  }

  return {
    // state
    carts,
    activeTableId,
    // getters
    lines,
    items,
    count,
    itemCount,
    total,
    isEmpty,
    linesFor,
    quantityOf,
    // actions
    open,
    setTable,
    add,
    increment,
    decrement,
    setQuantity,
    remove,
    setNotes,
    clear,
  }
})
