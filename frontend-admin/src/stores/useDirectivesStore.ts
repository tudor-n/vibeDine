import { defineStore } from "pinia";
import { ref, computed} from 'vue';
import { apiClient } from '@/services/api-client'
import type { AdminAIConfig } from '@shared/types/models'


export const useDirectivesStore = defineStore('directives', () => {

    const directives = ref<AdminAIConfig[]>([]);
    const isLoading = ref(false);

    const activeDirective = computed(() => 
        directives.value.find(d => d.is_active) || null
    )

    async function fetchDirectives() {
        try{
            isLoading.value = true;
            directives.value = await apiClient.getDirectives();
        }catch(error){
            console.error('Eroare la aducerea directivelor:', error);
        }finally{
            isLoading.value = false;
        }
    }

    async function postDirective(prompt: string) {
        try{
            isLoading.value = true;
            const newConfig = await apiClient.postDirective(prompt)

            directives.value = directives.value.map(d => ({ ...d, is_active: false}))
            directives.value.unshift(newConfig)
        }catch(error){
            console.error('Eroare la salvarea directivei:', error);
            throw error;
        }finally{
            isLoading.value = false;
        }

    }

    async function toggleActive(id: string, isActive: boolean){
        try{
            await apiClient.toggleActiveDirective(id, isActive)

            await fetchDirectives()
        }catch(error){
            console.error('Eroare la modificarea statusului:', error);
        }
    }

    return { 
    directives, 
    activeDirective, 
    isLoading, 
    fetchDirectives, 
    postDirective, 
    toggleActive 
  }
})