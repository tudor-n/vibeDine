import { defineStore } from 'pinia';
import { ref } from 'vue';
import { apiClient } from '@/services/api-client';
export const useSessionStore = defineStore('session', () => {
    const tableId = ref(sessionStorage.getItem('tableId'));
    const orderId = ref(sessionStorage.getItem('orderId'));
    const qrToken = ref(sessionStorage.getItem('qrToken'));
    async function initializeSession(token) {
        try {
            const response = await apiClient.initSession(token);
            tableId.value = response.tableId;
            orderId.value = response.activeOrder.id;
            qrToken.value = token;
            sessionStorage.setItem('tableId', response.tableId);
            sessionStorage.setItem('orderId', response.activeOrder.id);
            sessionStorage.setItem('qrToken', token);
        }
        catch (error) {
            console.error('Eroare la inițializarea sesiunii:', error);
            throw error;
        }
    }
    function clearSession() {
        tableId.value = null;
        orderId.value = null;
        qrToken.value = null;
        sessionStorage.clear();
    }
    return { tableId, orderId, qrToken, initializeSession, clearSession };
});
