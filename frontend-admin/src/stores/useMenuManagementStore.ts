import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import {
    apiClient,
    type CreateCategoryPayload,
    type UpdateCategoryPayload,
    type CreateProductPayload,
    type UpdateProductPayload,
} from '@/services/api-client'
import type { AdminMenuCategoryNode, AdminProduct } from '@shared/types/models'

export const useMenuManagementStore = defineStore('menuManagement', () => {
    const categories = ref<AdminMenuCategoryNode[]>([])
    const isLoading = ref(false)
    const error = ref<string | null>(null)

    const totalCategories = computed(() => categories.value.length)
    const totalProducts = computed(() =>
        categories.value.reduce((sum, c) => sum + c.products.length, 0)
    )
    const availableProducts = computed(() =>
        categories.value.reduce(
            (sum, c) => sum + c.products.filter((p) => p.isAvailable).length,
            0
        )
    )

    async function fetchMenu() {
        isLoading.value = true
        error.value = null
        try {
            const data = await apiClient.getAdminMenu()
            categories.value = data.categories
        } catch (e: any) {
            error.value = e?.message ?? 'A apărut o eroare la încărcarea meniului.'
            console.error('Eroare la încărcarea meniului admin:', e)
        } finally {
            isLoading.value = false
        }
    }

    async function createCategory(payload: CreateCategoryPayload) {
        const created = await apiClient.createCategory(payload)
        categories.value.push({
            id: created.id,
            name: created.name,
            sortOrder: created.sortOrder,
            imageUrl: created.imageUrl,
            products: [],
        })
        sortCategories()
    }

    async function updateCategory(id: string, payload: UpdateCategoryPayload) {
        const updated = await apiClient.updateCategory(id, payload)
        const idx = categories.value.findIndex((c) => c.id === id)
        if (idx !== -1) {
            const existing = categories.value[idx]
            categories.value[idx] = {
                ...existing,
                name: updated.name,
                sortOrder: updated.sortOrder,
                imageUrl: updated.imageUrl,
            }
            sortCategories()
        }
    }

    async function deleteCategory(id: string) {
        await apiClient.deleteCategory(id)
        categories.value = categories.value.filter((c) => c.id !== id)
    }

    async function createProduct(payload: CreateProductPayload) {
        const created = await apiClient.createProduct(payload)
        const cat = categories.value.find((c) => c.id === created.categoryId)
        if (cat) {
            cat.products.push(created)
            cat.products.sort((a, b) => a.sortOrder - b.sortOrder || a.name.localeCompare(b.name))
        }
    }

    async function updateProduct(id: string, payload: UpdateProductPayload) {
        const updated = await apiClient.updateProduct(id, payload)
        moveOrUpdateProduct(id, updated)
    }

    async function reorderProducts(categoryId: string, productIds: string[]) {
        const cat = categories.value.find((c) => c.id === categoryId)
        if (!cat) return

        const previous = cat.products.slice()
        const byId = new Map(cat.products.map((p) => [p.id, p]))
        const reordered = productIds
            .map((id) => byId.get(id))
            .filter((p): p is AdminProduct => !!p)

        if (reordered.length !== cat.products.length) {
            await fetchMenu()
            throw new Error('Reorder list mismatch — meniu reîncărcat.')
        }

        cat.products = reordered.map((p, idx) => ({ ...p, sortOrder: idx }))

        try {
            await apiClient.reorderProducts(categoryId, productIds)
        } catch (e) {
            cat.products = previous
            throw e
        }
    }

    async function softDeleteProduct(id: string) {
        await apiClient.softDeleteProduct(id)
        // Backend just flips IsAvailable=false; reflect locally.
        for (const cat of categories.value) {
            const p = cat.products.find((x) => x.id === id)
            if (p) {
                p.isAvailable = false
                break
            }
        }
    }

    async function restoreProduct(id: string) {
        const restored = await apiClient.restoreProduct(id)
        moveOrUpdateProduct(id, restored)
    }

    function moveOrUpdateProduct(id: string, updated: AdminProduct) {
        for (const cat of categories.value) {
            const idx = cat.products.findIndex((p) => p.id === id)
            if (idx !== -1) {
                if (cat.id === updated.categoryId) {
                    cat.products.splice(idx, 1, updated)
                    cat.products.sort((a, b) => a.sortOrder - b.sortOrder || a.name.localeCompare(b.name))
                } else {
                    cat.products.splice(idx, 1)
                    const target = categories.value.find((c) => c.id === updated.categoryId)
                    if (target) {
                        target.products.push(updated)
                        target.products.sort((a, b) => a.name.localeCompare(b.name))
                    }
                }
                return
            }
        }
    }

    function sortCategories() {
        categories.value.sort((a, b) => {
            const sa = a.sortOrder ?? Number.MAX_SAFE_INTEGER
            const sb = b.sortOrder ?? Number.MAX_SAFE_INTEGER
            if (sa !== sb) return sa - sb
            return a.name.localeCompare(b.name)
        })
    }

    return {
        categories,
        isLoading,
        error,
        totalCategories,
        totalProducts,
        availableProducts,
        fetchMenu,
        createCategory,
        updateCategory,
        deleteCategory,
        createProduct,
        updateProduct,
        softDeleteProduct,
        restoreProduct,
        reorderProducts,
    }
})
