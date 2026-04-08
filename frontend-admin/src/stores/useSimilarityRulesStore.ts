import { defineStore } from 'pinia'
import { ref } from 'vue'
import { apiClient } from '@/services/api-client'
import type { SimilarityRule } from '@shared/types/models'


export const useSimilarityRulesStore = defineStore('similarityRules', () => {
    const rules = ref<SimilarityRule[]>([]);
    const isLoading = ref(false);
    const error = ref<string | null>(null);


    async function fetchRules() {
        try{
            isLoading.value = true;
            error.value = null;
            rules.value = await apiClient.getSimilarityRules();
        }catch(err){
            if(err instanceof Error){
                error.value = err.message;
            }else{
                error.value = 'Eroare la încărcarea regulilor de similaritate.';
            }
            console.error(err)
        }finally{
            isLoading.value = false;
        }
    }


    async function addRule(newRulePayload: any) {
        try{
            isLoading.value = true;

            const createdRule = await apiClient.postSimilarityRule(newRulePayload);
            rules.value.push(createdRule);
            
        }catch(err){
            console.error('Eroare la adăugarea regulii:', err);
            throw err
        }finally{
            isLoading.value = false;
        }
    }

    async function deleteRule(ruleId: string) {
        try {
            isLoading.value = true;
            await apiClient.deleteSimilarityRule(ruleId);
            rules.value = rules.value.filter(r => r.id !== ruleId);
        } catch(err) {
            console.error('Eroare la ștergerea regulii:', err);
            throw err;
        } finally {
            isLoading.value = false;
        }
    }

    async function autoGenerate(){
        try{
            isLoading.value = true;
            await apiClient.autoGenerateRules();
            await fetchRules();
        }catch(err){
            console.error('Eroare la autogenerarea regulilor:', err);
            throw err;
        }finally{
            isLoading.value = false;
        }
    }


    return { 
    rules, 
    isLoading, 
    error, 
    fetchRules, 
    addRule, 
    deleteRule,
    autoGenerate
  }
    


})