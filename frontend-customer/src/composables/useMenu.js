import { ref, computed } from 'vue';
import { apiClient } from '@/services/api-client';
const categories = ref([]);
const products = ref([]);
const isLoading = ref(false);
const error = ref(null);
let loadPromise = null;
async function load() {
    if (loadPromise)
        return loadPromise;
    loadPromise = (async () => {
        try {
            isLoading.value = true;
            error.value = null;
            const response = await apiClient.getMenu();
            categories.value = response.categories
                .map((c) => ({
                id: c.id,
                name: c.name,
                // Tolerate both camelCase and snake_case shapes returned by the API.
                sortOrder: c.sortOrder ??
                    c.sort_order ??
                    0,
            }))
                .sort((a, b) => (a.sortOrder ?? 0) - (b.sortOrder ?? 0));
            const all = [];
            for (const cat of response.categories) {
                const items = cat.products;
                if (!Array.isArray(items))
                    continue;
                for (const p of items) {
                    all.push({
                        id: p.id,
                        categoryId: cat.id,
                        name: p.name,
                        description: p.description ?? '',
                        price: p.price,
                        imageUrl: p.imageUrl ??
                            p.image_url ??
                            null,
                        isAvailable: p.isAvailable !== undefined
                            ? Boolean(p.isAvailable)
                            : p.is_available !== undefined
                                ? Boolean(p.is_available)
                                : true,
                    });
                }
            }
            products.value = all;
        }
        catch (e) {
            error.value = e instanceof Error ? e.message : 'Eroare la încărcarea meniului.';
            loadPromise = null;
            throw e;
        }
        finally {
            isLoading.value = false;
        }
    })();
    return loadPromise;
}
function productsByCategory(categoryId) {
    return products.value.filter((p) => p.categoryId === categoryId);
}
export function useMenu() {
    return {
        categories,
        products,
        isLoading,
        error,
        load,
        productsByCategory,
        countByCategory: (categoryId) => computed(() => productsByCategory(categoryId).length),
    };
}
