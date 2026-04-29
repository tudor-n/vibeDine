import { defineStore } from 'pinia';
import { ref } from 'vue';
export const useFlashDealStore = defineStore('flashDeal', () => {
    const activeDeal = ref(null);
    const activeDealOrderId = ref(null);
    function setActiveDeal(deal, orderId) {
        activeDeal.value = deal;
        activeDealOrderId.value = orderId;
    }
    function clearDeal() {
        activeDeal.value = null;
        activeDealOrderId.value = null;
    }
    return { activeDeal, activeDealOrderId, setActiveDeal, clearDeal };
});
