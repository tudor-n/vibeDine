<script setup lang="ts">

import { ref } from 'vue';

const emit = defineEmits<{
    (e: 'submit', prompt: string): void;
}>();

const props = defineProps<{
    isLoading: boolean;
}>();

const promptText = ref('');

const onSubmit = () => {
    if(promptText.value.trim() === '') return;
    emit('submit', promptText.value);
    promptText.value= '';
}

</script>

<template>
  <div class="bg-white rounded-xl shadow-sm border border-gray-100 p-6 mb-8">
    <h2 class="text-xl font-bold text-gray-800 mb-2">Directiva Zilnică AI</h2>
    <p class="text-sm text-gray-500 mb-4">
      Spune-i inteligenței artificiale ce dorești să promovezi astăzi. AI-ul va genera automat reguli de recomandare pe baza acestui text.
    </p>

    <form @submit.prevent="onSubmit">
      <textarea
        v-model="promptText"
        rows="4"
        placeholder="Ex: Astăzi vrem să sugerăm un Tiramisu sau o Cafea la orice comandă de paste..."
        class="w-full p-4 border border-gray-300 rounded-lg focus:ring-2 focus:ring-brand focus:border-brand outline-none resize-none transition-shadow mb-4"
        :disabled="isLoading"
      ></textarea>

      <div class="flex justify-end">
        <button
          type="submit"
          :disabled="isLoading || promptText.trim() === ''"
          class="bg-brand text-white font-bold py-3 px-6 rounded-lg shadow-md hover:bg-brand-light active:scale-95 transition-all disabled:opacity-50 disabled:cursor-not-allowed flex items-center gap-2"
        >
          <span v-if="isLoading" class="animate-spin text-xl">↻</span>
          <span>{{ isLoading ? 'Se procesează AI...' : 'Generează Reguli' }}</span>
        </button>
      </div>
    </form>
  </div>
</template>