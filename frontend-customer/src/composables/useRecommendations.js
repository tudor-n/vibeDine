import { ref, watch } from 'vue';
import { useCartStore } from '@/stores/useCartStore';
import { useSessionStore } from '@/stores/useSessionStore';
import { apiClient } from '@/services/api-client';
export function useRecommendations() {
    const cartStore = useCartStore();
    const sessionStore = useSessionStore();
    const pairings = ref([]);
    const upsells = ref([]);
    const isLoading = ref(false);
    let debounceTimeout;
    watch(() => cartStore.items, (newItems) => {
        clearTimeout(debounceTimeout);
        if (newItems.length === 0 || !sessionStore.orderId) {
            pairings.value = [];
            upsells.value = [];
            return;
        }
        debounceTimeout = setTimeout(async () => {
            try {
                isLoading.value = true;
                const itemIds = newItems.map((item) => item.id);
                const response = await apiClient.getRecommendations(sessionStore.orderId, itemIds);
                if (Array.isArray(response)) {
                    const menuResponse = await apiClient.getMenu();
                    const allProducts = menuResponse.categories.flatMap(c => c.products || []);
                    const mappedRecs = response.map((rec) => {
                        const productInfo = allProducts.find((p) => p.id === rec.productId) || {
                            id: rec.productId,
                            name: 'Produs Recomandat',
                            description: '',
                            price: 0,
                            imageUrl: ''
                        };
                        return {
                            id: rec.productId,
                            type: 'pairing',
                            reason: rec.reason,
                            confidence: rec.confidence || 1,
                            menuItem: {
                                id: productInfo.id,
                                name: productInfo.name,
                                description: productInfo.description,
                                price: productInfo.price,
                                imageUrl: productInfo.imageUrl,
                                categoryName: ''
                            },
                            reasonType: 'pairsWithOrder',
                            discount: { percent: null, originalPrice: null, discountedPrice: null },
                            source: 'ai'
                        };
                    });
                    pairings.value = mappedRecs;
                    upsells.value = [];
                }
                else {
                    pairings.value = response.pairings || [];
                    upsells.value = response.upsells || [];
                }
            }
            catch (error) {
                console.error('Eroare la încărcarea recomandărilor:', error);
            }
            finally {
                isLoading.value = false;
            }
        }, 500);
    }, { deep: true });
    watch(() => sessionStore.orderId, () => {
        pairings.value = [];
        upsells.value = [];
    });
    return { pairings, upsells, isLoading };
}
