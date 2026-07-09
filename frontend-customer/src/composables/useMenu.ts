import { ref, computed } from 'vue'
import { apiClient } from '@/services/api-client'
import type { Category, Product, RecipeLine, StockUnitCode } from '@shared/types/models'

const categories = ref<Category[]>([])
const products = ref<Product[]>([])
const isLoading = ref(false)
const error = ref<string | null>(null)
let loadPromise: Promise<void> | null = null

// Normalizes the recipe lines a recipe-tracked product carries. Tolerant of both camelCase and
// snake_case, like the rest of this loader. Returns undefined when the product has no recipe.
function mapRecipe(raw: unknown): RecipeLine[] | undefined {
  if (!Array.isArray(raw) || raw.length === 0) return undefined
  return (raw as Array<Record<string, unknown>>).map((r) => ({
    ingredientId: (r.ingredientId ?? r.ingredient_id ?? '') as string,
    ingredientName: (r.ingredientName ?? r.ingredient_name ?? '') as string,
    unit: (r.unit ?? 'piece') as StockUnitCode,
    quantityPerPortion: Number(r.quantityPerPortion ?? r.quantity_per_portion ?? 0),
  }))
}

async function load() {
  if (loadPromise) return loadPromise
  loadPromise = (async () => {
    try {
      isLoading.value = true
      error.value = null

      const response = await apiClient.getMenu()

      categories.value = response.categories
        .map((c) => ({
          id: c.id,
          name: c.name,
          // Tolerate both camelCase and snake_case shapes returned by the API.
          sortOrder:
            (c as { sortOrder?: number; sort_order?: number }).sortOrder ??
            (c as { sortOrder?: number; sort_order?: number }).sort_order ??
            0,
        }))
        .sort((a, b) => (a.sortOrder ?? 0) - (b.sortOrder ?? 0))

      const all: Product[] = []
      for (const cat of response.categories) {
        const items = (cat as unknown as { products?: unknown[] }).products
        if (!Array.isArray(items)) continue
        for (const p of items as Array<Record<string, unknown>>) {
          all.push({
            id: p.id as string,
            categoryId: cat.id,
            name: p.name as string,
            description: (p.description as string) ?? '',
            price: p.price as number,
            imageUrl:
              (p.imageUrl as string) ??
              (p.image_url as string) ??
              null,
            isAvailable:
              p.isAvailable !== undefined
                ? Boolean(p.isAvailable)
                : p.is_available !== undefined
                  ? Boolean(p.is_available)
                  : true,
            outOfStock:
              p.outOfStock !== undefined
                ? Boolean(p.outOfStock)
                : p.out_of_stock !== undefined
                  ? Boolean(p.out_of_stock)
                  : false,
            recipe: mapRecipe(p.recipe ?? p.recipe_lines),
          })
        }
      }
      products.value = all
    } catch (e) {
      error.value = e instanceof Error ? e.message : 'Eroare la încărcarea meniului.'
      loadPromise = null
      throw e
    } finally {
      isLoading.value = false
    }
  })()
  return loadPromise
}

function productsByCategory(categoryId: string) {
  return products.value.filter((p) => p.categoryId === categoryId)
}

// Live stock update from SignalR: flip a product's availability in place so the
// menu blurs / un-blurs reactively without a refetch.
function setAvailability(productId: string, isAvailable: boolean, outOfStock: boolean) {
  const p = products.value.find((x) => x.id === productId)
  if (p) {
    p.isAvailable = isAvailable
    p.outOfStock = outOfStock
  }
}

export function useMenu() {
  return {
    categories,
    products,
    isLoading,
    error,
    load,
    productsByCategory,
    setAvailability,
    countByCategory: (categoryId: string) =>
      computed(() => productsByCategory(categoryId).length),
  }
}
