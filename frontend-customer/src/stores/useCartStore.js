import { defineStore } from 'pinia';
import { ref, computed } from 'vue';
import { apiClient } from '@/services/api-client';
import { useSessionStore } from './useSessionStore';
export const useCartStore = defineStore('cart', () => {
    const items = ref([]);
    const recommendedItemIds = ref(new Set());
    const sessionStore = useSessionStore();
    const total = computed(() => {
        return items.value.reduce((sum, item) => sum + item.price * item.quantity, 0);
    });
    let syncTimeout = null;
    async function syncCartWithBackend() {
        if (!sessionStore.orderId) {
            console.warn('Nu există o comandă activă (orderId lipsește).');
            return;
        }
        const payload = {
            items: items.value.map((item) => ({
                productId: item.id,
                quantity: item.quantity,
                wasAiRecommended: recommendedItemIds.value.has(item.id)
            }))
        };
        if (syncTimeout)
            clearTimeout(syncTimeout);
        syncTimeout = setTimeout(async () => {
            try {
                await apiClient.updateCartItems(sessionStore.orderId, payload);
            }
            catch (error) {
                console.error('Eroare la sincronizarea coșului pe backend:', error);
            }
        }, 500);
    }
    async function addItem(product) {
        const existingItem = items.value.find((i) => i.id === product.id);
        if (existingItem) {
            existingItem.quantity++;
        }
        else {
            items.value.push({ ...product, quantity: 1 });
        }
        await syncCartWithBackend();
    }
    async function addRecommendedItem(product) {
        recommendedItemIds.value.add(product.id);
        await addItem(product);
    }
    async function removeItem(productId) {
        const item = items.value.find((i) => i.id === productId);
        if (item) {
            if (item.quantity > 1) {
                item.quantity--;
            }
            else {
                items.value = items.value.filter((i) => i.id !== productId);
                recommendedItemIds.value.delete(productId);
            }
            await syncCartWithBackend();
        }
    }
    function addLocalItemOnly(product) {
        const existingItem = items.value.find((i) => i.id === product.id);
        if (existingItem) {
            existingItem.quantity++;
        }
        else {
            items.value.push({ ...product, quantity: 1 });
        }
    }
    function clearCart() {
        items.value = [];
        recommendedItemIds.value.clear();
    }
    return { items, total, addItem, addRecommendedItem, removeItem, clearCart, addLocalItemOnly };
});
