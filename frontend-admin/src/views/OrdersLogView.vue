<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import { apiClient } from '@/services/api-client'

interface OrderItem {
  id: string
  productName: string
  quantity: number
  unitPrice: number
  subtotal: number
  wasAiRecommended: boolean
  isFlashDealItem: boolean
}

interface Order {
  id: string
  status: string
  paymentStatus: string
  totalAmount: number
  createdAt: string
  tableNumber: string | null
  items: OrderItem[]
}

const orders = ref<Order[]>([])
const isLoading = ref(false)
const error = ref<string | null>(null)
const expandedOrderId = ref<string | null>(null)
const filterStatus = ref<string>('all')

const STATUS_LABELS: Record<string, { label: string; classes: string }> = {
  draft:      { label: 'Ciornă',      classes: 'bg-gray-100 text-gray-600' },
  submitted:  { label: 'Trimisă',     classes: 'bg-blue-100 text-blue-700' },
  preparing:  { label: 'În pregătire', classes: 'bg-yellow-100 text-yellow-700' },
  ready:      { label: 'Gată',        classes: 'bg-green-100 text-green-700' },
  completed:  { label: 'Finalizată',  classes: 'bg-green-200 text-green-800' },
  cancelled:  { label: 'Anulată',     classes: 'bg-red-100 text-red-700' },
}

const filteredOrders = computed(() => {
  if (filterStatus.value === 'all') return orders.value
  return orders.value.filter(o => o.status === filterStatus.value)
})

function formatTime(iso: string) {
  const d = new Date(iso)
  return d.toLocaleDateString('ro-RO', { day: '2-digit', month: '2-digit', year: 'numeric' })
    + ' '
    + d.toLocaleTimeString('ro-RO', { hour: '2-digit', minute: '2-digit' })
}

function itemLabel(item: OrderItem): { icon: string; text: string; classes: string } | null {
  if (item.isFlashDealItem) {
    return { icon: '⚡', text: 'Ofertă Fulger', classes: 'bg-yellow-100 text-yellow-700 border border-yellow-300' }
  }
  if (item.wasAiRecommended) {
    return { icon: '🤖', text: 'Recomandat AI', classes: 'bg-purple-100 text-purple-700 border border-purple-300' }
  }
  return null
}

function orderHasAiItems(order: Order) {
  return order.items.some(i => i.wasAiRecommended || i.isFlashDealItem)
}

function toggleExpand(id: string) {
  expandedOrderId.value = expandedOrderId.value === id ? null : id
}

async function fetchOrders() {
  try {
    isLoading.value = true
    error.value = null
    orders.value = await apiClient.getOrders(100)
  } catch (err) {
    error.value = err instanceof Error ? err.message : 'Eroare la încărcarea comenzilor.'
  } finally {
    isLoading.value = false
  }
}

onMounted(fetchOrders)
</script>

<template>
  <div class="p-6 max-w-5xl mx-auto">

    <header class="mb-8 flex flex-col md:flex-row md:items-center md:justify-between gap-4">
      <div>
        <h1 class="text-3xl font-bold text-gray-900">Jurnal Comenzi</h1>
        <p class="text-gray-500 mt-1">Istoricul comenzilor cu detalii despre produse și sursa lor.</p>
      </div>

      <div class="flex items-center gap-3">
        <!-- Filter -->
        <select
          v-model="filterStatus"
          class="bg-white border border-gray-200 rounded-lg px-3 py-2 text-sm text-gray-700 focus:outline-none focus:ring-2 focus:ring-brand"
        >
          <option value="all">Toate statusurile</option>
          <option value="draft">Ciornă</option>
          <option value="submitted">Trimisă</option>
          <option value="preparing">În pregătire</option>
          <option value="ready">Gată</option>
          <option value="completed">Finalizată</option>
          <option value="cancelled">Anulată</option>
        </select>

        <button
          @click="fetchOrders"
          :disabled="isLoading"
          class="bg-gray-900 text-white px-4 py-2 rounded-lg text-sm font-semibold hover:bg-gray-800 transition-colors disabled:opacity-50 flex items-center gap-2"
        >
          <span :class="{ 'animate-spin': isLoading }">↻</span>
          Reîncarcă
        </button>
      </div>
    </header>

    <!-- Legend -->
    <div class="flex items-center gap-4 mb-6 text-sm">
      <span class="text-gray-500 font-medium">Legende:</span>
      <span class="flex items-center gap-1 px-2 py-1 rounded-md bg-purple-100 text-purple-700 border border-purple-300 font-medium">
        🤖 Recomandat AI
      </span>
      <span class="flex items-center gap-1 px-2 py-1 rounded-md bg-yellow-100 text-yellow-700 border border-yellow-300 font-medium">
        ⚡ Ofertă Fulger
      </span>
    </div>

    <!-- Error -->
    <div v-if="error" class="bg-red-50 border border-red-200 text-red-700 p-4 rounded-lg mb-6">
      {{ error }}
    </div>

    <!-- Loading skeleton -->
    <div v-if="isLoading && orders.length === 0" class="space-y-3">
      <div v-for="i in 5" :key="i" class="animate-pulse bg-white rounded-xl border border-gray-100 p-4 h-20" />
    </div>

    <!-- Empty -->
    <div v-else-if="!isLoading && filteredOrders.length === 0" class="text-center py-16 text-gray-400">
      <p class="text-5xl mb-4">🧾</p>
      <p class="text-lg font-medium">Nicio comandă găsită</p>
    </div>

    <!-- Orders list -->
    <div v-else class="space-y-3">
      <div
        v-for="order in filteredOrders"
        :key="order.id"
        class="bg-white rounded-xl border border-gray-100 shadow-sm overflow-hidden"
      >
        <!-- Order header row -->
        <button
          class="w-full text-left px-5 py-4 flex items-center gap-4 hover:bg-gray-50 transition-colors"
          @click="toggleExpand(order.id)"
        >
          <!-- Expand indicator -->
          <span class="text-gray-400 text-sm transition-transform" :class="{ 'rotate-90': expandedOrderId === order.id }">
            ▶
          </span>

          <!-- Table -->
          <div class="w-12 h-12 rounded-xl bg-gray-100 flex items-center justify-center flex-shrink-0">
            <span class="text-xs font-black text-gray-600">M{{ order.tableNumber ?? '?' }}</span>
          </div>

          <!-- Main info -->
          <div class="flex-1 min-w-0">
            <div class="flex items-center gap-2 flex-wrap">
              <span class="font-bold text-gray-900">
                {{ order.items.length }} {{ order.items.length === 1 ? 'produs' : 'produse' }}
              </span>
              <span
                class="text-xs font-semibold px-2 py-0.5 rounded-full"
                :class="STATUS_LABELS[order.status]?.classes ?? 'bg-gray-100 text-gray-600'"
              >
                {{ STATUS_LABELS[order.status]?.label ?? order.status }}
              </span>
              <!-- AI/Flash badge on collapsed row -->
              <span v-if="orderHasAiItems(order)" class="text-xs text-purple-600 font-medium">
                {{ order.items.filter(i => i.isFlashDealItem).length > 0 ? '⚡' : '' }}
                {{ order.items.filter(i => i.wasAiRecommended).length > 0 ? '🤖' : '' }}
              </span>
            </div>
            <p class="text-xs text-gray-400 mt-0.5">{{ formatTime(order.createdAt) }}</p>
          </div>

          <!-- Total -->
          <div class="text-right flex-shrink-0">
            <p class="font-black text-lg text-brand">{{ order.totalAmount.toFixed(2) }} RON</p>
          </div>
        </button>

        <!-- Expanded items -->
        <Transition name="expand">
          <div v-if="expandedOrderId === order.id" class="border-t border-gray-100">
            <table class="w-full text-sm">
              <thead>
                <tr class="bg-gray-50 text-gray-500 text-xs uppercase tracking-wide">
                  <th class="text-left px-5 py-2 font-semibold">Produs</th>
                  <th class="text-center px-3 py-2 font-semibold">Cant.</th>
                  <th class="text-right px-3 py-2 font-semibold">Preț/buc</th>
                  <th class="text-right px-5 py-2 font-semibold">Subtotal</th>
                </tr>
              </thead>
              <tbody class="divide-y divide-gray-50">
                <tr
                  v-for="item in order.items"
                  :key="item.id"
                  class="hover:bg-gray-50"
                >
                  <td class="px-5 py-3">
                    <div class="flex items-center gap-2 flex-wrap">
                      <span class="font-medium text-gray-900">{{ item.productName }}</span>
                      <span
                        v-if="itemLabel(item)"
                        class="inline-flex items-center gap-1 text-xs font-semibold px-2 py-0.5 rounded-full"
                        :class="itemLabel(item)!.classes"
                      >
                        {{ itemLabel(item)!.icon }} {{ itemLabel(item)!.text }}
                      </span>
                    </div>
                  </td>
                  <td class="px-3 py-3 text-center text-gray-600">× {{ item.quantity }}</td>
                  <td class="px-3 py-3 text-right text-gray-600">{{ item.unitPrice.toFixed(2) }} RON</td>
                  <td class="px-5 py-3 text-right font-semibold text-gray-800">{{ item.subtotal.toFixed(2) }} RON</td>
                </tr>
              </tbody>
              <tfoot>
                <tr class="border-t border-gray-200 bg-gray-50">
                  <td colspan="3" class="px-5 py-3 text-right font-bold text-gray-700">Total comandă</td>
                  <td class="px-5 py-3 text-right font-black text-brand">{{ order.totalAmount.toFixed(2) }} RON</td>
                </tr>
              </tfoot>
            </table>
          </div>
        </Transition>
      </div>
    </div>
  </div>
</template>

<style scoped>
.expand-enter-active,
.expand-leave-active {
  transition: opacity 0.2s ease, max-height 0.25s ease;
  max-height: 600px;
  overflow: hidden;
}
.expand-enter-from,
.expand-leave-to {
  opacity: 0;
  max-height: 0;
}
.rotate-90 {
  transform: rotate(90deg);
}
</style>
