import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import type { Product } from '@shared/types/models'
import { apiClient } from '@/services/api-client'
import { useSessionStore } from './useSessionStore'

export interface CartItem extends Product {
  quantity: number
}

export const useCartStore = defineStore('cart', () => {

  const items = ref<CartItem[]>([])
  const sessionStore = useSessionStore()

  const total = computed(() => {
    return items.value.reduce((sum, item) => sum + item.price * item.quantity, 0)
  })

  async function syncCartWithBackend() {
    if(!sessionStore.orderId){
      console.warn('Nu există o comandă activă (orderId lipsește).')
      return
    }


  const payload = items.value.map((item) => ({
      product_id: item.id,
      quantity: item.quantity
    }))

  try {
      await apiClient.updateCartItems(sessionStore.orderId, payload)
    } catch (error) {
      console.error('Eroare la sincronizarea coșului pe backend:', error)
    }

  }


  async function addItem(product: Product){
    const existingItem = items.value.find((i) => i.id === product.id)

    if(existingItem){
      existingItem.quantity++
    }else {
      items.value.push({ ...product, quantity: 1 })
    }
    await syncCartWithBackend()
  }

  async function removeItem(productId: string) {
    const item = items.value.find((i) => i.id === productId)
    if (item) {
      if (item.quantity > 1) {
        item.quantity--
      } else {
        items.value = items.value.filter((i) => i.id !== productId)
      }

      await syncCartWithBackend()
    }
  }

  function addLocalItemOnly(product: Product) {
    const existingItem = items.value.find((i) => i.id === product.id)
    if (existingItem) {
      existingItem.quantity++
    } else {
      items.value.push({ ...product, quantity: 1 })
    }
  }

  function clearCart() {
    items.value = []
  }



  return { items, total , addItem, removeItem, clearCart, addLocalItemOnly}

})


