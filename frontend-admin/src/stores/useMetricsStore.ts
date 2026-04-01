import { defineStore } from 'pinia'
import { ref } from 'vue'
import { apiClient } from '@/services/api-client'


export const useMetricsStore = defineStore('metrics', () => {
    const metrics = ref<any>(null)
    const isLoading = ref(false)
    const error = ref<string | null>(null)

    let pollingInterval: ReturnType<typeof setInterval> | null = null;

    async function fetchMetrics() {
        try{
            if(!metrics.value) isLoading.value = true;
            error.value = null;

            const data = await apiClient.getMetrics();
            metrics.value = data;
        }catch(err){
            if(err instanceof Error){
                error.value = err.message;
            }else {
                error.value = 'Eroare la încărcarea metricilor.';
            }
        }finally{
            isLoading.value = false;
        }
    }

    function startPolling() {
        fetchMetrics();
        if(!pollingInterval){
            pollingInterval = setInterval(fetchMetrics, 30000)
        }
    }


    function stopPolling() {
        if(pollingInterval){
            clearInterval(pollingInterval);
            pollingInterval = null;
        }
    }

    return { metrics, isLoading, error, fetchMetrics, startPolling, stopPolling }
})
