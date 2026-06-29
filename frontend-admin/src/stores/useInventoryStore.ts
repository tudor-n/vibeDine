import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import {
    apiClient,
    type CreateIngredientPayload,
    type UpdateIngredientPayload,
    type AdjustStockPayload,
} from '@/services/api-client'
import type { AdminIngredient } from '@shared/types/models'

export const useInventoryStore = defineStore('inventory', () => {
    const ingredients = ref<AdminIngredient[]>([])
    const isLoading = ref(false)
    const error = ref<string | null>(null)

    const lowStockItems = computed(() => ingredients.value.filter((i) => i.isLow))
    const lowStockCount = computed(() => lowStockItems.value.length)
    // Standalone ingredients only — the recipe builder must never offer a simple product's auto-row.
    const standaloneIngredients = computed(() =>
        ingredients.value.filter((i) => !i.linkedProductId),
    )

    async function fetchIngredients() {
        isLoading.value = true
        error.value = null
        try {
            ingredients.value = await apiClient.getIngredients()
        } catch (e: any) {
            error.value = e?.message ?? 'A apărut o eroare la încărcarea stocului.'
            console.error('Eroare la încărcarea stocului:', e)
        } finally {
            isLoading.value = false
        }
    }

    async function createIngredient(payload: CreateIngredientPayload) {
        const created = await apiClient.createIngredient(payload)
        ingredients.value.push(created)
        sortIngredients()
    }

    async function updateIngredient(id: string, payload: UpdateIngredientPayload) {
        const updated = await apiClient.updateIngredient(id, payload)
        replace(updated)
    }

    async function adjustIngredient(id: string, payload: AdjustStockPayload) {
        const updated = await apiClient.adjustIngredient(id, payload)
        replace(updated)
    }

    async function deleteIngredient(id: string) {
        await apiClient.deleteIngredient(id)
        ingredients.value = ingredients.value.filter((i) => i.id !== id)
    }

    function replace(updated: AdminIngredient) {
        const idx = ingredients.value.findIndex((i) => i.id === updated.id)
        if (idx !== -1) ingredients.value[idx] = updated
        else ingredients.value.push(updated)
        sortIngredients()
    }

    function sortIngredients() {
        ingredients.value.sort((a, b) => a.name.localeCompare(b.name))
    }

    return {
        ingredients,
        isLoading,
        error,
        lowStockItems,
        lowStockCount,
        standaloneIngredients,
        fetchIngredients,
        createIngredient,
        updateIngredient,
        adjustIngredient,
        deleteIngredient,
    }
})
